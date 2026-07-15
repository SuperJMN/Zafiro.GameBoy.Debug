using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoreBoy;
using CoreBoy.controller;
using CoreBoy.cpu;
using CoreBoy.memory.cart;
using CoreBoy.serial;
using CoreBoy.sound;
using GameBoy.Debug.Core;
using GameBoy.Debug.Symbols;

namespace GameBoy.Debug.Emulator
{
    /// <summary>
    /// Pure-managed <see cref="IGameBoyDebugSession"/> backed by the vendored CoreBoy emulator core.
    /// No native dependencies; runs anywhere .NET runs.
    /// </summary>
    public sealed class ManagedGameBoyDebugSession : IGameBoyDebugSession, IRgbFrameSource, IDisposable
    {
        private const int ScreenWidth = FrameBufferDisplay.Width;
        private const int ScreenHeight = FrameBufferDisplay.Height;
        private const int MaxStepCycles = 1_000_000;
        private const int CyclesPerFrame = 70224;
        private const int MaxRawScreenRegionPixels = 1024;
        private const uint StateMagic = 0x31534D47; // "GMS1"

        private static readonly (int Start, int Length)[] StateRegions =
        {
            (0x8000, 0x2000), // VRAM
            (0xA000, 0x2000), // cartridge RAM
            (0xC000, 0x2000), // WRAM
            (0xFE00, 0x00A0), // OAM
            (0xFF00, 0x0080), // I/O registers
            (0xFF80, 0x007F), // HRAM
            (0xFFFF, 0x0001), // interrupt enable
        };

        private static readonly IReadOnlyDictionary<JoypadButton, Button> ButtonMap =
            new Dictionary<JoypadButton, Button>
            {
                [JoypadButton.Right] = Button.Right,
                [JoypadButton.Left] = Button.Left,
                [JoypadButton.Up] = Button.Up,
                [JoypadButton.Down] = Button.Down,
                [JoypadButton.A] = Button.A,
                [JoypadButton.B] = Button.B,
                [JoypadButton.Select] = Button.Select,
                [JoypadButton.Start] = Button.Start,
            };

        private static readonly IReadOnlyDictionary<string, JoypadButton> ButtonNames =
            new Dictionary<string, JoypadButton>(StringComparer.OrdinalIgnoreCase)
            {
                ["right"] = JoypadButton.Right,
                ["left"] = JoypadButton.Left,
                ["up"] = JoypadButton.Up,
                ["down"] = JoypadButton.Down,
                ["a"] = JoypadButton.A,
                ["b"] = JoypadButton.B,
                ["select"] = JoypadButton.Select,
                ["start"] = JoypadButton.Start,
            };

        private static readonly JoypadButton[] CanonicalButtons =
        {
            JoypadButton.Right, JoypadButton.Left, JoypadButton.Up, JoypadButton.Down,
            JoypadButton.A, JoypadButton.B, JoypadButton.Select, JoypadButton.Start,
        };

        private readonly BreakpointCollection breakpoints = new();
        private readonly WatchpointCollection watchpoints = new();
        private readonly SymbolService symbols = new();
        private readonly Dictionary<int, WriteRecord> lastWriters = new();

        private Gameboy gameboy;
        private FrameBufferDisplay display;
        private HeadlessController controller;
        private CoreBoy.gpu.Gpu.Mode? lastMode;
        private string romTitle;
        private string romModel;
        private bool romLoaded;
        private bool trackWrites;
        private bool trackReads;
        private WatchHit? watchHit;
        private bool disposed;
        private ulong totalCycles;
        private ulong totalFrames;
        private ulong totalInstructions;
        private bool instructionRetired;

        public DebugResult<LoadRomResult> LoadRom(string path)
        {
            if (disposed)
            {
                return DebugResult<LoadRomResult>.Failure("session_disposed", "The debug session has been disposed.");
            }

            if (!File.Exists(path))
            {
                return DebugResult<LoadRomResult>.Failure("rom_not_found", $"ROM was not found: {path}");
            }

            try
            {
                BuildMachine(path);
                breakpoints.ClearAll();
                watchpoints.ClearAll();
                return DebugResult<LoadRomResult>.Success(new LoadRomResult(true, romTitle, romModel));
            }
            catch (Exception ex)
            {
                romLoaded = false;
                return DebugResult<LoadRomResult>.Failure("load_rom_failed", ex.Message);
            }
        }

        private void BuildMachine(string path)
        {
            var options = new GameboyOptions { Rom = path, DisableBatterySaves = true };
            var cartridge = new Cartridge(options);
            display = new FrameBufferDisplay();
            controller = new HeadlessController();
            gameboy = new Gameboy(options, cartridge, display, controller, new NullSoundOutput(), new NullSerialEndpoint());
            gameboy.Cpu.InstructionCompleted = () =>
            {
                totalInstructions++;
                instructionRetired = true;
            };
            gameboy.Mmu.BeforeWriteObserver = OnBeforeMemoryWrite;
            gameboy.Mmu.WriteObserver = OnMemoryWrite;
            gameboy.Mmu.ReadObserver = null;
            lastMode = null;
            lastWriters.Clear();
            trackWrites = true;
            trackReads = false;
            watchHit = null;
            totalCycles = 0;
            totalFrames = 0;
            totalInstructions = 0;
            romPath = path;
            romTitle = cartridge.Title;
            romModel = cartridge.Gbc ? "CGB" : "DMG";
            romLoaded = true;
        }

        public DebugResult<ResetResult> Reset()
        {
            if (!romLoaded)
            {
                return NoRom<ResetResult>();
            }

            try
            {
                BuildMachine(romPath);
                return DebugResult<ResetResult>.Success(new ResetResult(true));
            }
            catch (Exception ex)
            {
                return DebugResult<ResetResult>.Failure("reset_failed", ex.Message);
            }
        }

        public DebugResult<StepInstructionResult> StepInstruction(int count)
        {
            if (!romLoaded)
            {
                return NoRom<StepInstructionResult>();
            }

            var before = ReadRegisters();
            if (!before.IsSuccess)
            {
                return DebugResult<StepInstructionResult>.Failure(before.Error!.Code, before.Error.Message);
            }

            var disassembly = Disassemble(ParseWord(before.Value.Pc), Math.Min(count, 16));
            for (var i = 0; i < count; i++)
            {
                StepOnce();
            }

            var after = ReadRegisters();
            if (!after.IsSuccess)
            {
                return DebugResult<StepInstructionResult>.Failure(after.Error!.Code, after.Error.Message);
            }

            var text = disassembly.IsSuccess
                ? string.Join('\n', disassembly.Value.Instructions.Select(instruction => $"{instruction.Address}: {instruction.Text}"))
                : "";

            return DebugResult<StepInstructionResult>.Success(new StepInstructionResult(before.Value.Pc, after.Value.Pc, after.Value, text, count, GetTimeline()));
        }

        public DebugResult<RunFrameResult> RunFrame(int count)
        {
            if (!romLoaded)
            {
                return NoRom<RunFrameResult>();
            }

            watchHit = null;
            var initialRegisters = ReadRegisters();
            if (!initialRegisters.IsSuccess)
            {
                return DebugResult<RunFrameResult>.Failure(initialRegisters.Error!.Code, initialRegisters.Error.Message);
            }

            var initialBreakpoint = IsBreakpointHit(ParseWord(initialRegisters.Value.Pc), initialRegisters.Value);
            if (!initialBreakpoint.IsSuccess)
            {
                return DebugResult<RunFrameResult>.Failure(initialBreakpoint.Error!.Code, initialBreakpoint.Error.Message);
            }

            if (initialBreakpoint.Value)
            {
                return DebugResult<RunFrameResult>.Success(
                    new RunFrameResult(0, initialRegisters.Value, true, GetTimeline()));
            }

            var framesRun = 0;
            AttachReadObserverIfNeeded();
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var completed = RunSingleFrame();
                    if (completed)
                    {
                        framesRun++;
                    }

                    if (!completed || watchHit.HasValue)
                    {
                        break;
                    }
                }
            }
            finally
            {
                DetachReadObserver();
            }

            var registers = ReadRegisters();
            if (!registers.IsSuccess)
            {
                return DebugResult<RunFrameResult>.Failure(registers.Error!.Code, registers.Error.Message);
            }

            var hit = IsBreakpointHit(ParseWord(registers.Value.Pc), registers.Value);
            if (!hit.IsSuccess)
            {
                return DebugResult<RunFrameResult>.Failure(hit.Error!.Code, hit.Error.Message);
            }

            return DebugResult<RunFrameResult>.Success(new RunFrameResult(framesRun, registers.Value, hit.Value, GetTimeline()));
        }

        public DebugResult<JoypadStateResult> SetJoypad(IReadOnlyList<JoypadButton> pressedButtons)
        {
            if (!romLoaded)
            {
                return NoRom<JoypadStateResult>();
            }

            var mask = ToButtonMask(pressedButtons);
            if (!mask.IsSuccess)
            {
                return DebugResult<JoypadStateResult>.Failure(mask.Error!.Code, mask.Error.Message);
            }

            controller.SetPressed(pressedButtons.Select(button => ButtonMap[button]));
            return DebugResult<JoypadStateResult>.Success(ToJoypadState(mask.Value));
        }

        public DebugResult<PressButtonsResult> PressButtons(IReadOnlyList<JoypadButton> pressedButtons, int frameCount)
        {
            var pressed = SetJoypad(pressedButtons);
            if (!pressed.IsSuccess)
            {
                return DebugResult<PressButtonsResult>.Failure(pressed.Error!.Code, pressed.Error.Message);
            }

            var run = RunFrame(frameCount);
            var released = SetJoypad(Array.Empty<JoypadButton>());
            if (!run.IsSuccess)
            {
                return DebugResult<PressButtonsResult>.Failure(run.Error!.Code, run.Error.Message);
            }

            if (!released.IsSuccess)
            {
                return DebugResult<PressButtonsResult>.Failure(released.Error!.Code, released.Error.Message);
            }

            return DebugResult<PressButtonsResult>.Success(new PressButtonsResult(run.Value.FramesRun, released.Value, run.Value.Registers));
        }

        public DebugResult<ContinueResult> ContinueUntilBreak(int maxInstructions)
        {
            if (!romLoaded)
            {
                return NoRom<ContinueResult>();
            }

            watchHit = null;
            AttachReadObserverIfNeeded();
            try
            {
                for (var i = 0; i < maxInstructions; i++)
                {
                    var cpu = gameboy.Cpu;
                    var pc = (ushort)cpu.Registers.PC;

                    // Fast path: only materialize the full register set when we actually need it
                    // (a breakpoint sits at this PC, or we are about to stop). This avoids formatting
                    // ~16 hex strings on every single instruction.
                    if (breakpoints.HasBreakpointAt(pc))
                    {
                        var registers = ReadRegisters();
                        if (!registers.IsSuccess)
                        {
                            return DebugResult<ContinueResult>.Failure(registers.Error!.Code, registers.Error.Message);
                        }

                        var hit = IsBreakpointHit(pc, registers.Value);
                        if (!hit.IsSuccess)
                        {
                            return DebugResult<ContinueResult>.Failure(hit.Error!.Code, hit.Error.Message);
                        }

                        if (hit.Value)
                        {
                            return Stop("breakpoint", registers.Value, (ulong)i);
                        }
                    }

                    if (cpu.State == State.HALTED || cpu.State == State.STOPPED)
                    {
                        var halted = ReadRegisters();
                        return halted.IsSuccess
                            ? Stop("halt", halted.Value, (ulong)i)
                            : DebugResult<ContinueResult>.Failure(halted.Error!.Code, halted.Error.Message);
                    }

                    StepOnce();
                    if (watchHit.HasValue)
                    {
                        var registers = ReadRegisters();
                        return registers.IsSuccess
                            ? Stop("watchpoint", registers.Value, (ulong)i + 1)
                            : DebugResult<ContinueResult>.Failure(registers.Error!.Code, registers.Error.Message);
                    }
                }

                var final = ReadRegisters();
                return final.IsSuccess
                    ? Stop("maxInstructions", final.Value, (ulong)maxInstructions)
                    : DebugResult<ContinueResult>.Failure(final.Error!.Code, final.Error.Message);
            }
            finally
            {
                DetachReadObserver();
            }

            DebugResult<ContinueResult> Stop(string reason, CpuRegisters registers, ulong instructionsRun) =>
                DebugResult<ContinueResult>.Success(new ContinueResult(true, reason, registers.Pc, registers, GetTimeline(), instructionsRun));
        }

        public DebugResult<ContinueResult> StepOver(int maxInstructions)
        {
            if (!romLoaded)
            {
                return NoRom<ContinueResult>();
            }

            var before = ReadRegisters();
            if (!before.IsSuccess)
            {
                return DebugResult<ContinueResult>.Failure(before.Error!.Code, before.Error.Message);
            }

            var pc = ParseWord(before.Value.Pc);
            var opcode = ReadByte(pc);
            if (!IsCallOrRst(opcode, out var length))
            {
                return StepSingle("step");
            }

            var returnAddress = (ushort)(pc + length);
            var startSp = ParseWord(before.Value.Sp);
            return StepUntil(
                maxInstructions,
                registers => ParseWord(registers.Pc) == returnAddress && ParseWord(registers.Sp) >= startSp,
                "step_over");
        }

        public DebugResult<ContinueResult> StepOut(int maxInstructions)
        {
            if (!romLoaded)
            {
                return NoRom<ContinueResult>();
            }

            var before = ReadRegisters();
            if (!before.IsSuccess)
            {
                return DebugResult<ContinueResult>.Failure(before.Error!.Code, before.Error.Message);
            }

            var startSp = ParseWord(before.Value.Sp);
            return StepUntil(maxInstructions, registers => ParseWord(registers.Sp) > startSp, "step_out");
        }

        public DebugResult<RunUntilConditionResult> RunUntilCondition(string condition, int maxInstructions, int maxFrames)
        {
            if (!romLoaded)
            {
                return NoRom<RunUntilConditionResult>();
            }

            if (!BreakpointCondition.TryParse(condition, out var parsedCondition, out var conditionError) || parsedCondition is null)
            {
                return DebugResult<RunUntilConditionResult>.Failure("invalid_condition", $"Invalid condition: {conditionError ?? "Condition is required."}");
            }

            watchHit = null;
            var startFrames = totalFrames;
            AttachReadObserverIfNeeded();
            try
            {
                for (var i = 0; i < maxInstructions; i++)
                {
                    var before = ReadRegisters();
                    if (!before.IsSuccess)
                    {
                        return DebugResult<RunUntilConditionResult>.Failure(before.Error!.Code, before.Error.Message);
                    }

                    var conditionResult = parsedCondition.Evaluate(new ConditionContext(this, before.Value));
                    if (!conditionResult.IsSuccess)
                    {
                        return DebugResult<RunUntilConditionResult>.Failure(conditionResult.Error!.Code, conditionResult.Error.Message);
                    }

                    if (conditionResult.Value)
                    {
                        return StopRunUntilCondition("condition", before.Value, (uint)i, startFrames);
                    }

                    var hit = IsBreakpointHit(ParseWord(before.Value.Pc), before.Value);
                    if (!hit.IsSuccess)
                    {
                        return DebugResult<RunUntilConditionResult>.Failure(hit.Error!.Code, hit.Error.Message);
                    }

                    if (hit.Value)
                    {
                        return StopRunUntilCondition("breakpoint", before.Value, (uint)i, startFrames);
                    }

                    if (before.Value.Halted)
                    {
                        return StopRunUntilCondition("halt", before.Value, (uint)i, startFrames);
                    }

                    if (totalFrames - startFrames >= (ulong)maxFrames)
                    {
                        return StopRunUntilCondition("maxFrames", before.Value, (uint)i, startFrames);
                    }

                    StepOnce();

                    var after = ReadRegisters();
                    if (!after.IsSuccess)
                    {
                        return DebugResult<RunUntilConditionResult>.Failure(after.Error!.Code, after.Error.Message);
                    }

                    conditionResult = parsedCondition.Evaluate(new ConditionContext(this, after.Value));
                    if (!conditionResult.IsSuccess)
                    {
                        return DebugResult<RunUntilConditionResult>.Failure(conditionResult.Error!.Code, conditionResult.Error.Message);
                    }

                    if (conditionResult.Value)
                    {
                        return StopRunUntilCondition("condition", after.Value, (uint)i + 1, startFrames);
                    }

                    if (watchHit.HasValue)
                    {
                        return StopRunUntilCondition("watchpoint", after.Value, (uint)i + 1, startFrames);
                    }

                    if (totalFrames - startFrames >= (ulong)maxFrames)
                    {
                        return StopRunUntilCondition("maxFrames", after.Value, (uint)i + 1, startFrames);
                    }
                }

                var final = ReadRegisters();
                return final.IsSuccess
                    ? StopRunUntilCondition("maxInstructions", final.Value, (uint)maxInstructions, startFrames)
                    : DebugResult<RunUntilConditionResult>.Failure(final.Error!.Code, final.Error.Message);
            }
            finally
            {
                DetachReadObserver();
            }
        }

        public DebugResult<BreakpointSetResult> SetBreakpoint(ushort address, string? condition)
        {
            if (!BreakpointCondition.TryParse(condition, out var parsedCondition, out var conditionError))
            {
                return DebugResult<BreakpointSetResult>.Failure(
                    "invalid_breakpoint_condition",
                    $"Invalid breakpoint condition: {conditionError}");
            }

            var breakpoint = breakpoints.Set(address, condition, parsedCondition);
            return DebugResult<BreakpointSetResult>.Success(new BreakpointSetResult(breakpoint.Id, breakpoint.Address, breakpoint.Enabled));
        }

        public DebugResult<ClearBreakpointResult> ClearBreakpoint(string breakpointId)
        {
            return breakpoints.Clear(breakpointId)
                ? DebugResult<ClearBreakpointResult>.Success(new ClearBreakpointResult(true))
                : DebugResult<ClearBreakpointResult>.Failure("breakpoint_not_found", $"Breakpoint '{breakpointId}' was not found.");
        }

        public DebugResult<ListBreakpointsResult> ListBreakpoints()
        {
            var entries = breakpoints.All
                .Select(breakpoint => new BreakpointEntry(breakpoint.Id, breakpoint.Address, breakpoint.Enabled, breakpoint.Condition))
                .ToArray();

            return DebugResult<ListBreakpointsResult>.Success(new ListBreakpointsResult(entries));
        }

        public DebugResult<WatchpointSetResult> SetWatchpoint(ushort address, WatchpointMode mode)
        {
            var watchpoint = watchpoints.Set(address, mode);
            return ToWatchpointSetResult(watchpoint);
        }

        public DebugResult<WatchpointSetResult> SetWatchpointRange(ushort address, int length, WatchpointMode mode)
        {
            if (length < 1 || address + length > 0x10000)
            {
                return DebugResult<WatchpointSetResult>.Failure("invalid_range", "Watchpoint range must fit within 0x0000..0xFFFF.");
            }

            var watchpoint = watchpoints.Set(address, length, mode);
            return ToWatchpointSetResult(watchpoint);
        }

        private static DebugResult<WatchpointSetResult> ToWatchpointSetResult(WatchpointInfo watchpoint)
        {
            return DebugResult<WatchpointSetResult>.Success(
                new WatchpointSetResult(watchpoint.Id, watchpoint.Address, ToWatchpointModeName(watchpoint.Mode), watchpoint.Enabled, watchpoint.Length));
        }

        public DebugResult<ClearWatchpointResult> ClearWatchpoint(string watchpointId)
        {
            return watchpoints.Clear(watchpointId)
                ? DebugResult<ClearWatchpointResult>.Success(new ClearWatchpointResult(true))
                : DebugResult<ClearWatchpointResult>.Failure("watchpoint_not_found", $"Watchpoint '{watchpointId}' was not found.");
        }

        public DebugResult<ListWatchpointsResult> ListWatchpoints()
        {
            var entries = watchpoints.All
                .Select(watchpoint => new WatchpointEntry(watchpoint.Id, watchpoint.Address, ToWatchpointModeName(watchpoint.Mode), watchpoint.Enabled, watchpoint.Length))
                .ToArray();

            return DebugResult<ListWatchpointsResult>.Success(new ListWatchpointsResult(entries));
        }

        public DebugResult<SessionStateResult> GetState()
        {
            if (!romLoaded)
            {
                return DebugResult<SessionStateResult>.Success(new SessionStateResult(false, null, null, false, null, new TimelineCounters(0, 0)));
            }

            var registers = ReadRegisters();
            return registers.IsSuccess
                ? DebugResult<SessionStateResult>.Success(
                    new SessionStateResult(true, romTitle, romModel, registers.Value.Halted, registers.Value.Pc, GetTimeline()))
                : DebugResult<SessionStateResult>.Failure(registers.Error!.Code, registers.Error.Message);
        }

        public DebugResult<CpuRegisters> ReadRegisters()
        {
            if (!romLoaded)
            {
                return NoRom<CpuRegisters>();
            }

            var r = gameboy.Cpu.Registers;
            return DebugResult<CpuRegisters>.Success(new CpuRegisters(
                Hex.FormatWord((ushort)r.AF),
                Hex.FormatWord((ushort)r.BC),
                Hex.FormatWord((ushort)r.DE),
                Hex.FormatWord((ushort)r.HL),
                Hex.FormatWord((ushort)r.SP),
                Hex.FormatWord((ushort)r.PC),
                Hex.FormatByte((byte)r.A),
                Hex.FormatByte((byte)r.Flags.FlagsByte),
                Hex.FormatByte((byte)r.B),
                Hex.FormatByte((byte)r.C),
                Hex.FormatByte((byte)r.D),
                Hex.FormatByte((byte)r.E),
                Hex.FormatByte((byte)r.H),
                Hex.FormatByte((byte)r.L),
                gameboy.Cpu.InterruptMasterEnabled,
                gameboy.Cpu.State == State.HALTED || gameboy.Cpu.State == State.STOPPED));
        }

        public DebugResult<MemoryReadResult> ReadMemory(ushort address, int length)
        {
            if (!romLoaded)
            {
                return NoRom<MemoryReadResult>();
            }

            return DebugResult<MemoryReadResult>.Success(MemoryFormatter.Format(address, ReadBytes(address, length)));
        }

        public DebugResult<WriteMemoryResult> WriteMemory(ushort address, IReadOnlyList<byte> bytes)
        {
            if (!romLoaded)
            {
                return NoRom<WriteMemoryResult>();
            }

            trackWrites = false;
            try
            {
                for (var i = 0; i < bytes.Count; i++)
                {
                    gameboy.Mmu.SetByte((address + i) & 0xFFFF, bytes[i]);
                }
            }
            finally
            {
                trackWrites = true;
            }

            return DebugResult<WriteMemoryResult>.Success(new WriteMemoryResult(true, Hex.FormatWord(address), bytes.Count));
        }

        public DebugResult<DisassembleResult> Disassemble(ushort address, int instructionCount)
        {
            if (!romLoaded)
            {
                return NoRom<DisassembleResult>();
            }

            var instructions = Disassembler.Disassemble(address, instructionCount, ReadByte, symbols.ResolveAddress);
            return DebugResult<DisassembleResult>.Success(new DisassembleResult(Hex.FormatWord(address), instructions));
        }

        public DebugResult<OamDumpResult> ReadOam()
        {
            if (!romLoaded)
            {
                return NoRom<OamDumpResult>();
            }

            var oam = ReadBytes(0xFE00, 0xA0);
            var sprites = Enumerable.Range(0, 40)
                .Select(index =>
                {
                    var offset = index * 4;
                    var y = oam[offset];
                    var x = oam[offset + 1];
                    return new OamSprite(index, y, x, Hex.FormatByte(oam[offset + 2]), Hex.FormatByte(oam[offset + 3]), y is > 0 and < 160 && x is > 0 and < 168);
                })
                .ToArray();

            return DebugResult<OamDumpResult>.Success(new OamDumpResult(sprites));
        }

        public DebugResult<PpuStateResult> ReadPpuState()
        {
            if (!romLoaded)
            {
                return NoRom<PpuStateResult>();
            }

            var lcdc = ReadByte(0xFF40);
            var stat = ReadByte(0xFF41);
            var scy = ReadByte(0xFF42);
            var scx = ReadByte(0xFF43);
            var ly = ReadByte(0xFF44);
            var lyc = ReadByte(0xFF45);
            var bgp = ReadByte(0xFF47);
            var obp0 = ReadByte(0xFF48);
            var obp1 = ReadByte(0xFF49);
            var wy = ReadByte(0xFF4A);
            var wx = ReadByte(0xFF4B);
            var vbk = ReadByte(0xFF4F);

            return DebugResult<PpuStateResult>.Success(PpuStateBuilder.Build(new PpuRegistersSnapshot(
                lcdc,
                stat,
                ly,
                lyc,
                scy,
                scx,
                wy,
                wx,
                bgp,
                obp0,
                obp1,
                vbk,
                romModel == "CGB",
                gameboy.GpuTicksInLine,
                TimingAuthoritative: true,
                Timeline: GetTimeline())));
        }

        public DebugResult<ScreenCaptureResult> CaptureScreen()
        {
            if (!romLoaded)
            {
                return NoRom<ScreenCaptureResult>();
            }

            var pixels = display.Snapshot();
            var data = PngEncoder.EncodeRgb24(pixels, ScreenWidth, ScreenHeight);
            return DebugResult<ScreenCaptureResult>.Success(new ScreenCaptureResult(ScreenWidth, ScreenHeight, "image/png", data));
        }

        public DebugResult<LastWriterResult> FindLastWriter(ushort address)
        {
            if (!romLoaded)
            {
                return NoRom<LastWriterResult>();
            }

            return lastWriters.TryGetValue(address, out var record)
                ? DebugResult<LastWriterResult>.Success(new LastWriterResult(true, Hex.FormatWord(address), Hex.FormatWord(record.Pc), Hex.FormatByte(record.Value), record.Count))
                : DebugResult<LastWriterResult>.Success(new LastWriterResult(false, Hex.FormatWord(address), null, null, 0));
        }

        public DebugResult<LastWritersResult> FindLastWriters(ushort address, int length)
        {
            if (!romLoaded)
            {
                return NoRom<LastWritersResult>();
            }

            if (length < 1 || address + length > 0x10000)
            {
                return DebugResult<LastWritersResult>.Failure("invalid_range", "Writer range must fit within 0x0000..0xFFFF.");
            }

            var writers = Enumerable.Range(0, length)
                .Select(offset =>
                {
                    var current = (ushort)(address + offset);
                    return lastWriters.TryGetValue(current, out var record)
                        ? new LastWriterResult(true, Hex.FormatWord(current), Hex.FormatWord(record.Pc), Hex.FormatByte(record.Value), record.Count)
                        : new LastWriterResult(false, Hex.FormatWord(current), null, null, 0);
                })
                .ToArray();

            return DebugResult<LastWritersResult>.Success(new LastWritersResult(writers));
        }

        public DebugResult<TraceUntilWriteResult> TraceUntilWrite(ushort address, int maxInstructions)
        {
            if (!romLoaded)
            {
                return NoRom<TraceUntilWriteResult>();
            }

            traceAddress = address;
            traceLength = 1;
            traceHit = false;
            uint instructionsRun = 0;
            try
            {
                for (var i = 0; i < maxInstructions && !traceHit; i++)
                {
                    StepOnce();
                    instructionsRun++;
                }
            }
            finally
            {
                traceAddress = -1;
                traceLength = 0;
            }

            var registers = ReadRegisters();
            if (!registers.IsSuccess)
            {
                return DebugResult<TraceUntilWriteResult>.Failure(registers.Error!.Code, registers.Error.Message);
            }

            return DebugResult<TraceUntilWriteResult>.Success(traceHit
                ? new TraceUntilWriteResult(true, "write", Hex.FormatWord(address), Hex.FormatWord(traceHitPc), Hex.FormatByte(traceHitValue), instructionsRun, registers.Value, GetTimeline())
                : new TraceUntilWriteResult(true, "maxInstructions", Hex.FormatWord(address), null, null, instructionsRun, registers.Value, GetTimeline()));
        }

        public DebugResult<TraceUntilWriteRangeResult> TraceUntilWriteRange(ushort address, int length, int maxInstructions)
        {
            if (!romLoaded)
            {
                return NoRom<TraceUntilWriteRangeResult>();
            }

            if (length < 1 || address + length > 0x10000)
            {
                return DebugResult<TraceUntilWriteRangeResult>.Failure("invalid_range", "Trace range must fit within 0x0000..0xFFFF.");
            }

            traceAddress = address;
            traceLength = length;
            traceHit = false;
            uint instructionsRun = 0;
            try
            {
                for (var i = 0; i < maxInstructions && !traceHit; i++)
                {
                    StepOnce();
                    instructionsRun++;
                }
            }
            finally
            {
                traceAddress = -1;
                traceLength = 0;
            }

            var registers = ReadRegisters();
            if (!registers.IsSuccess)
            {
                return DebugResult<TraceUntilWriteRangeResult>.Failure(registers.Error!.Code, registers.Error.Message);
            }

            var ppu = ReadPpuState();
            if (!ppu.IsSuccess)
            {
                return DebugResult<TraceUntilWriteRangeResult>.Failure(ppu.Error!.Code, ppu.Error.Message);
            }

            var disassemblyAddress = traceHit ? traceHitPc : ParseWord(registers.Value.Pc);
            var disassembly = Disassemble(disassemblyAddress, 4);
            if (!disassembly.IsSuccess)
            {
                return DebugResult<TraceUntilWriteRangeResult>.Failure(disassembly.Error!.Code, disassembly.Error.Message);
            }

            return DebugResult<TraceUntilWriteRangeResult>.Success(new TraceUntilWriteRangeResult(
                true,
                traceHit ? "write" : "maxInstructions",
                Hex.FormatWord(address),
                length,
                traceHit ? Hex.FormatWord(traceHitAddress) : null,
                traceHit ? Hex.FormatWord(traceHitPc) : null,
                traceHit ? Hex.FormatByte(traceHitValue) : null,
                instructionsRun,
                registers.Value,
                ppu.Value,
                disassembly.Value,
                GetTimeline()));
        }

        public DebugResult<VideoWriteTraceResult> TraceVideoWrites(VideoWriteTraceRequest request)
        {
            if (!romLoaded)
            {
                return NoRom<VideoWriteTraceResult>();
            }

            var validation = ValidateVideoTraceRequest(request);
            if (!validation.IsSuccess)
            {
                return DebugResult<VideoWriteTraceResult>.Failure(validation.Error!.Code, validation.Error.Message);
            }

            var initial = CapturePpuStateWithoutWatchpoints();
            if (!initial.IsSuccess)
            {
                return DebugResult<VideoWriteTraceResult>.Failure(initial.Error!.Code, initial.Error.Message);
            }

            var held = SetJoypad(request.Buttons);
            if (!held.IsSuccess)
            {
                return DebugResult<VideoWriteTraceResult>.Failure(held.Error!.Code, held.Error.Message);
            }

            var trace = new ActiveVideoTrace(request, totalFrames);
            var framesRun = 0;
            var hitBreakpoint = false;
            JoypadStateResult? released = null;
            try
            {
                activeVideoTrace = trace;
                for (var frame = 0; frame < request.FrameCount; frame++)
                {
                    var run = RunFrame(1);
                    if (!run.IsSuccess)
                    {
                        return DebugResult<VideoWriteTraceResult>.Failure(run.Error!.Code, run.Error.Message);
                    }

                    framesRun += run.Value.FramesRun;
                    hitBreakpoint = run.Value.HitBreakpoint;
                    if (run.Value.FramesRun == 0 || hitBreakpoint)
                    {
                        break;
                    }
                }
            }
            finally
            {
                activeVideoTrace = null;
                pendingVideoWrite = null;
                var release = SetJoypad([]);
                if (release.IsSuccess)
                {
                    released = release.Value;
                }
            }

            if (released is null)
            {
                return DebugResult<VideoWriteTraceResult>.Failure("release_joypad_failed", "Could not release joypad input after video tracing.");
            }

            var final = CapturePpuStateWithoutWatchpoints();
            if (!final.IsSuccess)
            {
                return DebugResult<VideoWriteTraceResult>.Failure(final.Error!.Code, final.Error.Message);
            }

            return DebugResult<VideoWriteTraceResult>.Success(new VideoWriteTraceResult(
                request.FrameCount,
                framesRun,
                initial.Value,
                final.Value,
                trace.Events,
                trace.Events.Count,
                trace.EventsObserved,
                trace.Truncated,
                hitBreakpoint,
                hitBreakpoint ? "breakpoint" : framesRun == request.FrameCount ? "frame_limit" : "execution_stop",
                released,
                GetTimeline()));
        }

        public DebugResult<TilemapDumpResult> DumpTilemap(ushort address)
        {
            if (!romLoaded)
            {
                return NoRom<TilemapDumpResult>();
            }

            var bytes = ReadBytes(address, 32 * 32);
            var rows = Enumerable.Range(0, 32)
                .Select(row => Hex.FormatBytes(bytes.Skip(row * 32).Take(32)))
                .ToArray();

            return DebugResult<TilemapDumpResult>.Success(new TilemapDumpResult(Hex.FormatWord(address), 32, 32, rows));
        }

        public DebugResult<TilemapSetDumpResult> DumpTilemaps(bool includeDetails)
        {
            if (!romLoaded)
            {
                return NoRom<TilemapSetDumpResult>();
            }

            var bank0 = new byte[0x2000];
            var bank1 = new byte[0x2000];
            if (!gameboy.TryCopyVideoRamBank(0, bank0))
            {
                return DebugResult<TilemapSetDumpResult>.Failure("tilemap_snapshot_failed", "Could not snapshot VRAM bank 0.");
            }

            var hasBank1 = gameboy.TryCopyVideoRamBank(1, bank1);
            return DebugResult<TilemapSetDumpResult>.Success(TilemapReader.Build(
                bank0,
                hasBank1 ? bank1 : [],
                romModel,
                includeDetails,
                GetTimeline()));
        }

        public DebugResult<TilesetDumpResult> DumpTileset(ushort address, int tileCount)
        {
            if (!romLoaded)
            {
                return NoRom<TilesetDumpResult>();
            }

            var bytes = ReadBytes(address, tileCount * 16);
            var tiles = Enumerable.Range(0, tileCount)
                .Select(index => new TileDump(index, Hex.FormatWord((ushort)(address + index * 16)), Hex.FormatBytes(bytes.Skip(index * 16).Take(16))))
                .ToArray();

            return DebugResult<TilesetDumpResult>.Success(new TilesetDumpResult(Hex.FormatWord(address), tileCount, tiles));
        }

        public DebugResult<LoadSymbolsResult> LoadSymbols(string path)
        {
            var loaded = symbols.Load(path);
            return loaded.IsSuccess
                ? DebugResult<LoadSymbolsResult>.Success(new LoadSymbolsResult(true, loaded.Value))
                : DebugResult<LoadSymbolsResult>.Failure(loaded.Error!.Code, loaded.Error.Message);
        }

        public DebugResult<ResolveSymbolResult> ResolveSymbol(string name)
        {
            var resolved = symbols.Resolve(name);
            return resolved.IsSuccess
                ? DebugResult<ResolveSymbolResult>.Success(new ResolveSymbolResult(name, Hex.FormatWord(resolved.Value.Address), resolved.Value.Bank))
                : DebugResult<ResolveSymbolResult>.Failure(resolved.Error!.Code, resolved.Error.Message);
        }

        public DebugResult<ReadSymbolResult> ReadSymbol(string name, int? length)
        {
            var resolved = symbols.Resolve(name);
            if (!resolved.IsSuccess)
            {
                return DebugResult<ReadSymbolResult>.Failure(resolved.Error!.Code, resolved.Error.Message);
            }

            if (!romLoaded)
            {
                return NoRom<ReadSymbolResult>();
            }

            var bytes = ReadBytes(resolved.Value.Address, length ?? 1);
            return DebugResult<ReadSymbolResult>.Success(new ReadSymbolResult(name, Hex.FormatWord(resolved.Value.Address), bytes, Hex.FormatBytes(bytes)));
        }

        public DebugResult<ScreenRegionResult> ReadScreenRegion(int x, int y, int width, int height, string format)
        {
            if (!romLoaded)
            {
                return NoRom<ScreenRegionResult>();
            }

            if (x < 0 || y < 0 || width < 1 || height < 1 || x + width > ScreenWidth || y + height > ScreenHeight)
            {
                return DebugResult<ScreenRegionResult>.Failure("invalid_screen_region", "Screen region must fit within 160x144.");
            }

            if (format.Equals("dmg_shades", StringComparison.OrdinalIgnoreCase) ||
                format.Equals("dmg_shades_raw", StringComparison.OrdinalIgnoreCase))
            {
                return DebugResult<ScreenRegionResult>.Success(BuildDmgShadeRegion(
                    x,
                    y,
                    width,
                    height,
                    forceRaw: format.EndsWith("_raw", StringComparison.OrdinalIgnoreCase)));
            }

            if (format.Equals("rgb24", StringComparison.OrdinalIgnoreCase) ||
                format.Equals("rgb24_raw", StringComparison.OrdinalIgnoreCase))
            {
                return DebugResult<ScreenRegionResult>.Success(BuildRgb24Region(
                    x,
                    y,
                    width,
                    height,
                    forceRaw: format.EndsWith("_raw", StringComparison.OrdinalIgnoreCase)));
            }

            return DebugResult<ScreenRegionResult>.Failure(
                "invalid_screen_region_format",
                "format must be dmg_shades, dmg_shades_raw, rgb24, or rgb24_raw.");
        }

        public DebugResult<ScreenObservationResult> ObserveScreen(int frameCount) =>
            ScreenObserver.Observe(this, frameCount);

        public DebugResult<ExecutionObservationResult> ObserveExecution(ExecutionObservationRequest request)
        {
            if (!romLoaded)
            {
                return NoRom<ExecutionObservationResult>();
            }

            var validation = ValidateExecutionObservationRequest(request);
            if (!validation.IsSuccess)
            {
                return DebugResult<ExecutionObservationResult>.Failure(validation.Error!.Code, validation.Error.Message);
            }

            var initialTilemaps = DumpTilemaps(includeDetails: false);
            if (!initialTilemaps.IsSuccess)
            {
                return DebugResult<ExecutionObservationResult>.Failure(initialTilemaps.Error!.Code, initialTilemaps.Error.Message);
            }

            var previous = display.Snapshot();
            var current = new uint[ScreenFrameAnalyzer.PixelCount];
            var initialHash = ScreenFrameAnalyzer.Hash(previous);
            var frames = new List<ExecutionFrameObservation>(request.FrameCount);
            var videoRequest = new VideoWriteTraceRequest(
                request.FrameCount,
                request.MaxVideoEvents,
                request.VideoKinds,
                request.PpuRegisters,
                request.Buttons);
            var videoTrace = request.TraceVideoWrites ? new ActiveVideoTrace(videoRequest, totalFrames) : null;
            var held = SetJoypad(request.Buttons);
            if (!held.IsSuccess)
            {
                return DebugResult<ExecutionObservationResult>.Failure(held.Error!.Code, held.Error.Message);
            }

            var framesRun = 0;
            var hitBreakpoint = false;
            var stopReason = "frame_limit";
            JoypadStateResult? released = null;
            try
            {
                activeVideoTrace = videoTrace;
                for (var frameOffset = 1; frameOffset <= request.FrameCount; frameOffset++)
                {
                    var run = RunFrame(1);
                    if (!run.IsSuccess)
                    {
                        return DebugResult<ExecutionObservationResult>.Failure(run.Error!.Code, run.Error.Message);
                    }

                    framesRun += run.Value.FramesRun;
                    hitBreakpoint = run.Value.HitBreakpoint;
                    if (run.Value.FramesRun == 0)
                    {
                        stopReason = hitBreakpoint
                            ? "breakpoint"
                            : watchHit.HasValue ? "watchpoint" : "execution_stop";
                        break;
                    }

                    display.Snapshot().CopyTo(current, 0);
                    var screen = ScreenFrameAnalyzer.Compare(previous, current, frameOffset, run.Value.Timeline.Frames);
                    var memory = request.MemoryProbes
                        .Select(probe => new MemoryProbeObservation(
                            Hex.FormatWord(probe.Address),
                            probe.Length,
                            Hex.FormatBytes(ReadBytes(probe.Address, probe.Length))))
                        .ToArray();
                    PpuStateResult? ppuState = null;
                    if (request.IncludePpuState)
                    {
                        var ppu = CapturePpuStateWithoutWatchpoints();
                        if (!ppu.IsSuccess)
                        {
                            return DebugResult<ExecutionObservationResult>.Failure(ppu.Error!.Code, ppu.Error.Message);
                        }

                        ppuState = ppu.Value;
                    }

                    frames.Add(new ExecutionFrameObservation(screen, memory, ppuState));
                    (previous, current) = (current, previous);
                    if (hitBreakpoint)
                    {
                        stopReason = "breakpoint";
                    }
                }
            }
            finally
            {
                activeVideoTrace = null;
                pendingVideoWrite = null;
                var release = SetJoypad([]);
                if (release.IsSuccess)
                {
                    released = release.Value;
                }
            }

            if (released is null)
            {
                return DebugResult<ExecutionObservationResult>.Failure("release_joypad_failed", "Could not release joypad input after execution observation.");
            }

            var finalTilemaps = DumpTilemaps(includeDetails: false);
            if (!finalTilemaps.IsSuccess)
            {
                return DebugResult<ExecutionObservationResult>.Failure(finalTilemaps.Error!.Code, finalTilemaps.Error.Message);
            }

            var events = videoTrace?.Events ?? [];
            return DebugResult<ExecutionObservationResult>.Success(new ExecutionObservationResult(
                request.FrameCount,
                framesRun,
                request.Buttons.Select(ToButtonName).ToArray(),
                initialHash,
                frames,
                events,
                events.Count,
                videoTrace?.EventsObserved ?? 0,
                videoTrace?.Truncated ?? false,
                initialTilemaps.Value,
                finalTilemaps.Value,
                hitBreakpoint,
                stopReason,
                released,
                ExecutionObserver.AppliedLimits,
                GetTimeline()));
        }

        public DebugResult<int> CopyRgbFrame(Memory<uint> destination)
        {
            if (!romLoaded)
            {
                return NoRom<int>();
            }

            if (destination.Length < ScreenFrameAnalyzer.PixelCount)
            {
                return DebugResult<int>.Failure(
                    "invalid_screen_frame_buffer",
                    $"destination must contain at least {ScreenFrameAnalyzer.PixelCount} pixels.");
            }

            display.Snapshot().CopyTo(destination);
            return DebugResult<int>.Success(ScreenFrameAnalyzer.PixelCount);
        }

        public DebugResult<InputTimelineResult> RunInputTimeline(IReadOnlyList<InputTimelineStep> steps)
        {
            if (!romLoaded)
            {
                return NoRom<InputTimelineResult>();
            }

            var results = new List<InputTimelineStepResult>(steps.Count);
            var totalFramesRun = 0;
            var completed = false;
            try
            {
                for (var index = 0; index < steps.Count; index++)
                {
                    var step = steps[index];
                    var buttons = ParseButtonNames(step.Buttons);
                    if (!buttons.IsSuccess)
                    {
                        return DebugResult<InputTimelineResult>.Failure(buttons.Error!.Code, buttons.Error.Message);
                    }

                    var joypad = SetJoypad(buttons.Value);
                    if (!joypad.IsSuccess)
                    {
                        return DebugResult<InputTimelineResult>.Failure(joypad.Error!.Code, joypad.Error.Message);
                    }

                    var run = RunFrame(step.Frames);
                    if (!run.IsSuccess)
                    {
                        return DebugResult<InputTimelineResult>.Failure(run.Error!.Code, run.Error.Message);
                    }

                    totalFramesRun += run.Value.FramesRun;

                    CpuRegisters? registers = null;
                    if (step.ReadRegisters)
                    {
                        var readRegisters = ReadRegisters();
                        if (!readRegisters.IsSuccess)
                        {
                            return DebugResult<InputTimelineResult>.Failure(readRegisters.Error!.Code, readRegisters.Error.Message);
                        }

                        registers = readRegisters.Value;
                    }

                    PpuStateResult? ppuState = null;
                    if (step.ReadPpuState)
                    {
                        var readPpu = ReadPpuState();
                        if (!readPpu.IsSuccess)
                        {
                            return DebugResult<InputTimelineResult>.Failure(readPpu.Error!.Code, readPpu.Error.Message);
                        }

                        ppuState = readPpu.Value;
                    }

                    OamDumpResult? oam = null;
                    if (step.DumpOam)
                    {
                        var readOam = ReadOam();
                        if (!readOam.IsSuccess)
                        {
                            return DebugResult<InputTimelineResult>.Failure(readOam.Error!.Code, readOam.Error.Message);
                        }

                        oam = readOam.Value;
                    }

                    ScreenCaptureResult? capture = null;
                    if (step.Capture)
                    {
                        var screen = CaptureScreen();
                        if (!screen.IsSuccess)
                        {
                            return DebugResult<InputTimelineResult>.Failure(screen.Error!.Code, screen.Error.Message);
                        }

                        capture = screen.Value;
                    }

                    TilemapDumpResult? tilemap = null;
                    if (step.DumpTilemap)
                    {
                        var address = (ushort)0x9800;
                        if (!string.IsNullOrWhiteSpace(step.TilemapAddress))
                        {
                            var parsed = GameBoyAddress.Parse(step.TilemapAddress);
                            if (!parsed.IsSuccess)
                            {
                                return DebugResult<InputTimelineResult>.Failure(parsed.Error!.Code, parsed.Error.Message);
                            }

                            address = parsed.Value.Address;
                        }

                        var dump = DumpTilemap(address);
                        if (!dump.IsSuccess)
                        {
                            return DebugResult<InputTimelineResult>.Failure(dump.Error!.Code, dump.Error.Message);
                        }

                        tilemap = dump.Value;
                    }

                    MemoryReadResult? memory = null;
                    if (!string.IsNullOrWhiteSpace(step.MemoryAddress))
                    {
                        var parsed = GameBoyAddress.Parse(step.MemoryAddress);
                        if (!parsed.IsSuccess)
                        {
                            return DebugResult<InputTimelineResult>.Failure(parsed.Error!.Code, parsed.Error.Message);
                        }

                        var memoryLength = step.MemoryLength ?? 1;
                        var readMemory = ReadMemory(parsed.Value.Address, memoryLength);
                        if (!readMemory.IsSuccess)
                        {
                            return DebugResult<InputTimelineResult>.Failure(readMemory.Error!.Code, readMemory.Error.Message);
                        }

                        memory = readMemory.Value;
                    }

                    results.Add(new InputTimelineStepResult(
                        index,
                        run.Value.FramesRun,
                        totalFrames,
                        buttons.Value.Select(ToButtonName).ToArray(),
                        registers,
                        ppuState,
                        oam,
                        capture,
                        tilemap,
                        memory,
                        GetTimeline()));
                }

                completed = true;
            }
            finally
            {
                if (!completed)
                {
                    _ = SetJoypad([]);
                }
            }

            var released = SetJoypad([]);
            if (!released.IsSuccess)
            {
                return DebugResult<InputTimelineResult>.Failure(released.Error!.Code, released.Error.Message);
            }

            return DebugResult<InputTimelineResult>.Success(new InputTimelineResult(totalFramesRun, released.Value, results, GetTimeline()));
        }

        public DebugResult<SaveStateResult> SaveState(string path)
        {
            if (!romLoaded)
            {
                return NoRom<SaveStateResult>();
            }

            try
            {
                using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
                using var writer = new BinaryWriter(stream);
                writer.Write(StateMagic);

                var r = gameboy.Cpu.Registers;
                writer.Write((byte)r.A);
                writer.Write((byte)r.B);
                writer.Write((byte)r.C);
                writer.Write((byte)r.D);
                writer.Write((byte)r.E);
                writer.Write((byte)r.H);
                writer.Write((byte)r.L);
                writer.Write((byte)r.Flags.FlagsByte);
                writer.Write((ushort)r.SP);
                writer.Write((ushort)r.PC);

                foreach (var (start, length) in StateRegions)
                {
                    for (var i = 0; i < length; i++)
                    {
                        writer.Write(ReadByte((ushort)(start + i)));
                    }
                }

                writer.Write(totalFrames);
                writer.Write(totalCycles);
                writer.Write(totalInstructions);

                return DebugResult<SaveStateResult>.Success(new SaveStateResult(true, path));
            }
            catch (Exception ex)
            {
                return DebugResult<SaveStateResult>.Failure("save_state_failed", ex.Message);
            }
        }

        public DebugResult<LoadStateResult> LoadState(string path)
        {
            if (!romLoaded)
            {
                return NoRom<LoadStateResult>();
            }

            if (!File.Exists(path))
            {
                return DebugResult<LoadStateResult>.Failure("state_not_found", $"Save state was not found: {path}");
            }

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
                using var reader = new BinaryReader(stream);
                if (reader.ReadUInt32() != StateMagic)
                {
                    return DebugResult<LoadStateResult>.Failure("invalid_state", "The file is not a managed Game Boy save state.");
                }

                var a = reader.ReadByte();
                var b = reader.ReadByte();
                var c = reader.ReadByte();
                var d = reader.ReadByte();
                var e = reader.ReadByte();
                var h = reader.ReadByte();
                var l = reader.ReadByte();
                var f = reader.ReadByte();
                var sp = reader.ReadUInt16();
                var pc = reader.ReadUInt16();

                trackWrites = false;
                try
                {
                    foreach (var (start, length) in StateRegions)
                    {
                        for (var i = 0; i < length; i++)
                        {
                            gameboy.Mmu.SetByte((start + i) & 0xFFFF, reader.ReadByte());
                        }
                    }
                }
                finally
                {
                    trackWrites = true;
                }

                var r = gameboy.Cpu.Registers;
                r.SetAf((a << 8) | f);
                r.SetBc((b << 8) | c);
                r.SetDe((d << 8) | e);
                r.SetHl((h << 8) | l);
                r.SP = sp;
                r.PC = pc;

                if (stream.Position + sizeof(ulong) * 3 <= stream.Length)
                {
                    totalFrames = reader.ReadUInt64();
                    totalCycles = reader.ReadUInt64();
                    totalInstructions = reader.ReadUInt64();
                }
                else
                {
                    totalFrames = 0;
                    totalCycles = 0;
                    totalInstructions = 0;
                }

                return DebugResult<LoadStateResult>.Success(new LoadStateResult(true, path));
            }
            catch (Exception ex)
            {
                return DebugResult<LoadStateResult>.Failure("load_state_failed", ex.Message);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            romLoaded = false;
            gameboy = null;
            disposed = true;
        }

        private int traceAddress = -1;
        private int traceLength;
        private bool traceHit;
        private ushort traceHitAddress;
        private ushort traceHitPc;
        private byte traceHitValue;
        private string romPath;
        private ActiveVideoTrace activeVideoTrace;
        private PendingVideoWrite pendingVideoWrite;

        private void OnBeforeMemoryWrite(int address, int value)
        {
            var trace = activeVideoTrace;
            if (!trackWrites || trace is null)
            {
                return;
            }

            var masked = (ushort)(address & 0xFFFF);
            if (!VideoWriteTracing.TryClassify(masked, out var kind) ||
                !trace.Request.Kinds.Contains(kind) ||
                (kind == VideoWriteKind.PpuRegister && !trace.Request.PpuRegisters.Contains(masked)))
            {
                return;
            }

            if (trace.Events.Count >= trace.Request.MaxEvents)
            {
                pendingVideoWrite = new PendingVideoWrite(masked, (byte)value, kind, null, null);
                return;
            }

            var before = CapturePpuStateWithoutWatchpoints();
            if (!before.IsSuccess)
            {
                return;
            }

            int? vramBank = kind == VideoWriteKind.Vram
                ? romModel == "CGB" ? ParseByte(before.Value.Vbk) & 1 : 0
                : null;
            pendingVideoWrite = new PendingVideoWrite(
                masked,
                (byte)(value & 0xFF),
                kind,
                before.Value,
                vramBank);
        }

        private void OnMemoryWrite(int address, int value)
        {
            if (!trackWrites)
            {
                return;
            }

            var pc = (ushort)gameboy.Cpu.InstructionAddress;
            var masked = address & 0xFFFF;
            var byteValue = (byte)(value & 0xFF);
            lastWriters[masked] = lastWriters.TryGetValue(masked, out var existing)
                ? new WriteRecord(pc, byteValue, existing.Count + 1)
                : new WriteRecord(pc, byteValue, 1);

            if (traceAddress >= 0 && masked >= traceAddress && masked < traceAddress + traceLength)
            {
                traceHit = true;
                traceHitAddress = (ushort)masked;
                traceHitPc = pc;
                traceHitValue = byteValue;
            }

            if (watchpoints.TryMatch((ushort)masked, isWrite: true, out var watchpoint))
            {
                watchHit = new WatchHit((ushort)masked, watchpoint.Mode, pc, byteValue);
            }

            var trace = activeVideoTrace;
            var pending = pendingVideoWrite;
            pendingVideoWrite = null;
            if (trace is null || pending is null || pending.Address != masked)
            {
                return;
            }

            trace.EventsObserved++;
            if (trace.Events.Count >= trace.Request.MaxEvents || pending.Before is null)
            {
                trace.Truncated = true;
                return;
            }

            var after = CapturePpuStateWithoutWatchpoints();
            if (!after.IsSuccess)
            {
                return;
            }

            trace.Events.Add(new VideoWriteEvent(
                (int)(totalFrames - trace.StartFrame),
                totalFrames,
                totalCycles,
                totalInstructions,
                Hex.FormatWord(pc),
                Hex.FormatWord((ushort)masked),
                pending.Kind,
                VideoWriteTracing.RegisterName((ushort)masked),
                Hex.FormatByte(byteValue),
                pending.VramBank,
                pending.Before,
                after.Value));
        }

        private DebugResult<PpuStateResult> CapturePpuStateWithoutWatchpoints()
        {
            var previousTrackReads = trackReads;
            trackReads = false;
            try
            {
                return ReadPpuState();
            }
            finally
            {
                trackReads = previousTrackReads;
            }
        }

        private static DebugResult<bool> ValidateVideoTraceRequest(VideoWriteTraceRequest request)
        {
            if (request.FrameCount is < 1 or > ScreenObserver.MaxFrames)
            {
                return DebugResult<bool>.Failure("invalid_frame_count", $"frameCount must be between 1 and {ScreenObserver.MaxFrames}.");
            }

            if (request.MaxEvents is < 1 or > VideoWriteTracing.MaxEvents)
            {
                return DebugResult<bool>.Failure("invalid_max_events", $"maxEvents must be between 1 and {VideoWriteTracing.MaxEvents}.");
            }

            if (request.Kinds.Count == 0)
            {
                return DebugResult<bool>.Failure("invalid_video_write_kinds", "At least one video-write kind is required.");
            }

            if (request.Kinds.Contains(VideoWriteKind.PpuRegister) &&
                (request.PpuRegisters.Count == 0 || request.PpuRegisters.Any(address => VideoWriteTracing.RegisterName(address) is null)))
            {
                return DebugResult<bool>.Failure("invalid_ppu_registers", "PPU register filters must contain known Game Boy LCD/PPU register addresses.");
            }

            return DebugResult<bool>.Success(true);
        }

        private static DebugResult<bool> ValidateExecutionObservationRequest(ExecutionObservationRequest request)
        {
            if (request.FrameCount is < 1 or > ExecutionObserver.MaxFrames)
            {
                return DebugResult<bool>.Failure("invalid_frame_count", $"frameCount must be between 1 and {ExecutionObserver.MaxFrames}.");
            }

            if (request.MemoryProbes.Count > ExecutionObserver.MaxMemoryProbes ||
                request.MemoryProbes.Any(probe => probe.Length is < 1 or > ExecutionObserver.MaxMemoryProbeLength || !ExecutionObserver.IsSafeProbe(probe)) ||
                request.MemoryProbes.Sum(probe => probe.Length) > ExecutionObserver.MaxMemoryBytesPerFrame)
            {
                return DebugResult<bool>.Failure(
                    "invalid_memory_probes",
                    "Memory probes must stay inside one side-effect-free VRAM/RAM/OAM region and respect the published count and byte limits.");
            }

            if (!request.TraceVideoWrites)
            {
                return DebugResult<bool>.Success(true);
            }

            if (request.MaxVideoEvents is < 1 or > ExecutionObserver.MaxVideoEvents)
            {
                return DebugResult<bool>.Failure("invalid_max_video_events", $"maxVideoEvents must be between 1 and {ExecutionObserver.MaxVideoEvents}.");
            }

            return ValidateVideoTraceRequest(new VideoWriteTraceRequest(
                request.FrameCount,
                request.MaxVideoEvents,
                request.VideoKinds,
                request.PpuRegisters,
                request.Buttons));
        }

        private void OnMemoryRead(int address)
        {
            if (!trackReads)
            {
                return;
            }

            var masked = (ushort)(address & 0xFFFF);
            if (watchpoints.TryMatch(masked, isWrite: false, out var watchpoint))
            {
                watchHit = new WatchHit(masked, watchpoint.Mode, (ushort)gameboy.Cpu.InstructionAddress, null);
            }
        }

        private void StepOnce()
        {
            var previousTrackReads = trackReads;
            trackReads = gameboy.Mmu.ReadObserver != null;
            try
            {
                var cpu = gameboy.Cpu;
                var guard = 0;

                // When halted/stopped, advance (bounded) until an interrupt wakes the CPU.
                if (cpu.State == State.HALTED || cpu.State == State.STOPPED)
                {
                    do
                    {
                        TickOnce();
                        guard++;
                    }
                    while ((cpu.State == State.HALTED || cpu.State == State.STOPPED) && guard < MaxStepCycles);
                    return;
                }

                // Cpu.Tick() is clock-divided (4 ticks per machine cycle). An instruction always leaves
                // State.OPCODE during decode and returns to State.OPCODE once it retires. Tick until the
                // opcode is consumed, then until the next fetch boundary.
                var leftOpcode = false;
                while (guard < MaxStepCycles)
                {
                    TickOnce();
                    guard++;

                    var state = cpu.State;
                    if (state == State.HALTED || state == State.STOPPED)
                    {
                        return;
                    }

                    if (!leftOpcode)
                    {
                        if (state != State.OPCODE)
                        {
                            leftOpcode = true;
                        }
                    }
                    else if (state == State.OPCODE)
                    {
                        return;
                    }
                }
            }
            finally
            {
                trackReads = previousTrackReads;
            }
        }

        private bool RunSingleFrame()
        {
            var previousTrackReads = trackReads;
            trackReads = gameboy.Mmu.ReadObserver != null;
            instructionRetired = false;
            try
            {
                for (var cycle = 0; cycle < CyclesPerFrame; cycle++)
                {
                    TickOnce();
                    if (watchHit.HasValue)
                    {
                        return false;
                    }

                    if (instructionRetired)
                    {
                        instructionRetired = false;
                        var registers = ReadRegisters();
                        if (registers.IsSuccess)
                        {
                            var breakpoint = IsBreakpointHit(ParseWord(registers.Value.Pc), registers.Value);
                            if (breakpoint.IsSuccess && breakpoint.Value)
                            {
                                return false;
                            }
                        }
                    }
                }

                return true;
            }
            finally
            {
                trackReads = previousTrackReads;
            }
        }

        private void TickOnce()
        {
            totalCycles++;
            totalFrames = totalCycles / CyclesPerFrame;
            var mode = gameboy.Tick();
            if (mode.HasValue)
            {
                if (mode.Value == CoreBoy.gpu.Gpu.Mode.VBlank && lastMode != CoreBoy.gpu.Gpu.Mode.VBlank)
                {
                    display.RequestRefresh();
                }

                lastMode = mode;
            }
        }

        private byte ReadByte(ushort address) => (byte)(gameboy.Mmu.GetByte(address) & 0xFF);

        private TimelineCounters GetTimeline() => new(totalFrames, totalCycles, totalInstructions);

        private byte[] ReadBytes(ushort address, int length)
        {
            var buffer = new byte[length];
            for (var i = 0; i < length; i++)
            {
                buffer[i] = ReadByte((ushort)((address + i) & 0xFFFF));
            }

            return buffer;
        }

        private DebugResult<bool> IsBreakpointHit(ushort address, CpuRegisters registers)
        {
            foreach (var breakpoint in breakpoints.FindAll(address))
            {
                var shouldBreak = ShouldBreak(breakpoint, registers);
                if (!shouldBreak.IsSuccess)
                {
                    return shouldBreak;
                }

                if (shouldBreak.Value)
                {
                    return DebugResult<bool>.Success(true);
                }
            }

            return DebugResult<bool>.Success(false);
        }

        private DebugResult<bool> ShouldBreak(BreakpointInfo breakpoint, CpuRegisters registers)
        {
            if (breakpoint.ParsedCondition is null)
            {
                return string.IsNullOrWhiteSpace(breakpoint.Condition)
                    ? DebugResult<bool>.Success(true)
                    : DebugResult<bool>.Failure("invalid_breakpoint_condition", $"Breakpoint '{breakpoint.Id}' has an invalid condition.");
            }

            return breakpoint.ParsedCondition.Evaluate(new ConditionContext(this, registers));
        }

        private DebugResult<ContinueResult> StepSingle(string reason)
        {
            watchHit = null;
            AttachReadObserverIfNeeded();
            try
            {
                StepOnce();
                var registers = ReadRegisters();
                if (!registers.IsSuccess)
                {
                    return DebugResult<ContinueResult>.Failure(registers.Error!.Code, registers.Error.Message);
                }

                if (watchHit.HasValue)
                {
                    return Stop("watchpoint", registers.Value, 1);
                }

                var breakpoint = IsBreakpointHit(ParseWord(registers.Value.Pc), registers.Value);
                if (!breakpoint.IsSuccess)
                {
                    return DebugResult<ContinueResult>.Failure(breakpoint.Error!.Code, breakpoint.Error.Message);
                }

                if (breakpoint.Value)
                {
                    return Stop("breakpoint", registers.Value, 1);
                }

                return registers.Value.Halted ? Stop("halt", registers.Value, 1) : Stop(reason, registers.Value, 1);
            }
            finally
            {
                DetachReadObserver();
            }
        }

        private DebugResult<ContinueResult> StepUntil(int maxInstructions, Func<CpuRegisters, bool> completed, string completedReason)
        {
            watchHit = null;
            AttachReadObserverIfNeeded();
            try
            {
                for (var i = 0; i < maxInstructions; i++)
                {
                    if (gameboy.Cpu.State == State.HALTED || gameboy.Cpu.State == State.STOPPED)
                    {
                        var halted = ReadRegisters();
                        return halted.IsSuccess
                            ? Stop("halt", halted.Value, (ulong)i)
                            : DebugResult<ContinueResult>.Failure(halted.Error!.Code, halted.Error.Message);
                    }

                    StepOnce();
                    var registers = ReadRegisters();
                    if (!registers.IsSuccess)
                    {
                        return DebugResult<ContinueResult>.Failure(registers.Error!.Code, registers.Error.Message);
                    }

                    if (watchHit.HasValue)
                    {
                        return Stop("watchpoint", registers.Value, (ulong)i + 1);
                    }

                    var breakpoint = IsBreakpointHit(ParseWord(registers.Value.Pc), registers.Value);
                    if (!breakpoint.IsSuccess)
                    {
                        return DebugResult<ContinueResult>.Failure(breakpoint.Error!.Code, breakpoint.Error.Message);
                    }

                    if (breakpoint.Value)
                    {
                        return Stop("breakpoint", registers.Value, (ulong)i + 1);
                    }

                    if (registers.Value.Halted)
                    {
                        return Stop("halt", registers.Value, (ulong)i + 1);
                    }

                    if (completed(registers.Value))
                    {
                        return Stop(completedReason, registers.Value, (ulong)i + 1);
                    }
                }

                var final = ReadRegisters();
                return final.IsSuccess
                    ? Stop("maxInstructions", final.Value, (ulong)maxInstructions)
                    : DebugResult<ContinueResult>.Failure(final.Error!.Code, final.Error.Message);
            }
            finally
            {
                DetachReadObserver();
            }
        }

        private void AttachReadObserverIfNeeded()
        {
            trackReads = false;
            if (watchpoints.HasEnabledReadWatchpoints)
            {
                gameboy.Mmu.ReadObserver = OnMemoryRead;
            }
        }

        private void DetachReadObserver()
        {
            if (gameboy != null)
            {
                gameboy.Mmu.ReadObserver = null;
            }

            trackReads = false;
        }

        private DebugResult<ContinueResult> Stop(string reason, CpuRegisters registers, ulong instructionsRun = 0) =>
            DebugResult<ContinueResult>.Success(new ContinueResult(true, reason, registers.Pc, registers, GetTimeline(), instructionsRun));

        private DebugResult<RunUntilConditionResult> StopRunUntilCondition(
            string reason,
            CpuRegisters registers,
            uint instructionsRun,
            ulong startFrames)
        {
            var ppu = ReadPpuState();
            return ppu.IsSuccess
                ? DebugResult<RunUntilConditionResult>.Success(
                    new RunUntilConditionResult(
                        true,
                        reason,
                        registers.Pc,
                        instructionsRun,
                        totalFrames - startFrames,
                        registers,
                        ppu.Value,
                        GetTimeline()))
                : DebugResult<RunUntilConditionResult>.Failure(ppu.Error!.Code, ppu.Error.Message);
        }

        private static bool IsCallOrRst(byte opcode, out int length)
        {
            length = opcode is 0xCD or 0xC4 or 0xCC or 0xD4 or 0xDC ? 3 : 1;
            return opcode is 0xCD or 0xC4 or 0xCC or 0xD4 or 0xDC
                or 0xC7 or 0xCF or 0xD7 or 0xDF or 0xE7 or 0xEF or 0xF7 or 0xFF;
        }

        private static string ToWatchpointModeName(WatchpointMode mode) => mode switch
        {
            WatchpointMode.Read => "read",
            WatchpointMode.Write => "write",
            WatchpointMode.Access => "access",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };

        private static DebugResult<byte> ToButtonMask(IReadOnlyList<JoypadButton> pressedButtons)
        {
            byte mask = 0;
            foreach (var button in pressedButtons)
            {
                if ((int)button is < 0 or > 7)
                {
                    return DebugResult<byte>.Failure("invalid_button", $"Unsupported joypad button value: {(int)button}.");
                }

                mask |= (byte)(1 << (int)button);
            }

            return DebugResult<byte>.Success(mask);
        }

        private static DebugResult<IReadOnlyList<JoypadButton>> ParseButtonNames(IReadOnlyList<string>? buttons)
        {
            if (buttons is null)
            {
                return DebugResult<IReadOnlyList<JoypadButton>>.Failure("invalid_buttons", "buttons is required.");
            }

            var selected = new HashSet<JoypadButton>();
            foreach (var rawButton in buttons)
            {
                var button = rawButton?.Trim();
                if (string.IsNullOrEmpty(button))
                {
                    return DebugResult<IReadOnlyList<JoypadButton>>.Failure("invalid_button", "Button names must not be empty.");
                }

                if (!ButtonNames.TryGetValue(button, out var parsed))
                {
                    return DebugResult<IReadOnlyList<JoypadButton>>.Failure(
                        "invalid_button",
                        $"Unknown button '{button}'. Valid buttons: {string.Join(", ", ButtonNames.Keys)}.");
                }

                selected.Add(parsed);
            }

            return DebugResult<IReadOnlyList<JoypadButton>>.Success(CanonicalButtons.Where(selected.Contains).ToArray());
        }

        private static JoypadStateResult ToJoypadState(byte mask)
        {
            return new JoypadStateResult(
                IsPressed(mask, JoypadButton.Right),
                IsPressed(mask, JoypadButton.Left),
                IsPressed(mask, JoypadButton.Up),
                IsPressed(mask, JoypadButton.Down),
                IsPressed(mask, JoypadButton.A),
                IsPressed(mask, JoypadButton.B),
                IsPressed(mask, JoypadButton.Select),
                IsPressed(mask, JoypadButton.Start),
                CanonicalButtons.Where(button => IsPressed(mask, button)).Select(ToButtonName).ToArray());
        }

        private static bool IsPressed(byte mask, JoypadButton button) => (mask & (1 << (int)button)) != 0;

        private static string ToButtonName(JoypadButton button) => button switch
        {
            JoypadButton.Right => "right",
            JoypadButton.Left => "left",
            JoypadButton.Up => "up",
            JoypadButton.Down => "down",
            JoypadButton.A => "a",
            JoypadButton.B => "b",
            JoypadButton.Select => "select",
            JoypadButton.Start => "start",
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, null),
        };

        private ScreenRegionResult BuildDmgShadeRegion(int x, int y, int width, int height, bool forceRaw)
        {
            var shades = display.SnapshotDmgShades();
            var values = new List<int>(Math.Min(width * height, MaxRawScreenRegionPixels));
            var includeRaw = forceRaw || width * height <= MaxRawScreenRegionPixels;
            var histogram = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["0"] = 0,
                ["1"] = 0,
                ["2"] = 0,
                ["3"] = 0,
            };
            var rowHashes = new List<string>(height);

            for (var row = 0; row < height; row++)
            {
                var hash = 2166136261u;
                for (var column = 0; column < width; column++)
                {
                    var shade = shades[(y + row) * ScreenWidth + x + column];
                    if (includeRaw)
                    {
                        values.Add(shade);
                    }

                    histogram[shade.ToString()]++;
                    hash ^= shade;
                    hash *= 16777619u;
                }

                rowHashes.Add($"0x{hash:X8}");
            }

            var ppu = ReadPpuState();
            var mapping = ppu.IsSuccess
                ? BuildScreenMapping(x, y, ppu.Value)
                : null;

            return new ScreenRegionResult(
                x,
                y,
                width,
                height,
                forceRaw ? "dmg_shades_raw" : "dmg_shades",
                width * height,
                includeRaw ? values : null,
                histogram,
                rowHashes,
                mapping);
        }

        private ScreenRegionResult BuildRgb24Region(int x, int y, int width, int height, bool forceRaw)
        {
            var pixels = display.Snapshot();
            var includeRaw = forceRaw || width * height <= MaxRawScreenRegionPixels;
            var values = includeRaw ? new List<int>(width * height) : null;
            var histogram = new Dictionary<string, int>(StringComparer.Ordinal);
            var rowHashes = new List<string>(height);

            for (var row = 0; row < height; row++)
            {
                var hash = 2166136261u;
                for (var column = 0; column < width; column++)
                {
                    var rgb = (int)(pixels[(y + row) * ScreenWidth + x + column] & 0xFFFFFF);
                    values?.Add(rgb);
                    var key = $"0x{rgb:X6}";
                    histogram[key] = histogram.TryGetValue(key, out var count) ? count + 1 : 1;
                    hash ^= (byte)(rgb >> 16);
                    hash *= 16777619u;
                    hash ^= (byte)(rgb >> 8);
                    hash *= 16777619u;
                    hash ^= (byte)rgb;
                    hash *= 16777619u;
                }

                rowHashes.Add($"0x{hash:X8}");
            }

            var ppu = ReadPpuState();
            var mapping = ppu.IsSuccess ? BuildScreenMapping(x, y, ppu.Value) : null;
            return new ScreenRegionResult(
                x,
                y,
                width,
                height,
                forceRaw ? "rgb24_raw" : "rgb24",
                width * height,
                values,
                histogram,
                rowHashes,
                mapping);
        }

        private static ScreenToBgTileMapping BuildScreenMapping(int x, int y, PpuStateResult ppu)
        {
            var scx = ParseByte(ppu.Scx);
            var scy = ParseByte(ppu.Scy);
            var lcdc = ParseByte(ppu.Lcdc);
            var tilemapAddress = (lcdc & 0x08) != 0 ? 0x9C00 : 0x9800;
            return new ScreenToBgTileMapping(
                ((x + scx) & 0xFF) / 8,
                ((y + scy) & 0xFF) / 8,
                Hex.FormatWord((ushort)tilemapAddress));
        }

        private static byte ParseByte(string value) =>
            Convert.ToByte(value.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase), 16);

        private static ushort ParseWord(string value) =>
            (ushort)Convert.ToInt32(value.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase), 16);

        private static DebugResult<T> NoRom<T>() =>
            DebugResult<T>.Failure("no_rom_loaded", "No ROM has been loaded into the session.");

        private readonly struct WriteRecord
        {
            public WriteRecord(ushort pc, byte value, ulong count)
            {
                Pc = pc;
                Value = value;
                Count = count;
            }

            public ushort Pc { get; }

            public byte Value { get; }

            public ulong Count { get; }
        }

        private readonly struct WatchHit
        {
            public WatchHit(ushort address, WatchpointMode mode, ushort pc, byte? value)
            {
                Address = address;
                Mode = mode;
                Pc = pc;
                Value = value;
            }

            public ushort Address { get; }

            public WatchpointMode Mode { get; }

            public ushort Pc { get; }

            public byte? Value { get; }
        }

        private sealed class ActiveVideoTrace
        {
            public ActiveVideoTrace(VideoWriteTraceRequest request, ulong startFrame)
            {
                Request = request;
                StartFrame = startFrame;
            }

            public VideoWriteTraceRequest Request { get; }

            public ulong StartFrame { get; }

            public List<VideoWriteEvent> Events { get; } = new();

            public int EventsObserved { get; set; }

            public bool Truncated { get; set; }
        }

        private sealed class PendingVideoWrite
        {
            public PendingVideoWrite(ushort address, byte value, VideoWriteKind kind, PpuStateResult before, int? vramBank)
            {
                Address = address;
                Value = value;
                Kind = kind;
                Before = before;
                VramBank = vramBank;
            }

            public ushort Address { get; }

            public byte Value { get; }

            public VideoWriteKind Kind { get; }

            public PpuStateResult Before { get; }

            public int? VramBank { get; }
        }

        private sealed class ConditionContext : IBreakpointConditionContext
        {
            private readonly ManagedGameBoyDebugSession session;

            public ConditionContext(ManagedGameBoyDebugSession session, CpuRegisters registers)
            {
                this.session = session;
                Registers = registers;
            }

            public CpuRegisters Registers { get; }

            public DebugResult<byte> ReadByte(ushort address) => DebugResult<byte>.Success(session.ReadByte(address));
        }
    }
}
