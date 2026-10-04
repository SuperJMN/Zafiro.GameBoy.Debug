using System.Globalization;
using System.Text;
using Zafiro.GameBoy.Debug.Core;
using Zafiro.GameBoy.Debug.Symbols;

namespace Zafiro.GameBoy.Debug.SameBoy;

public sealed class SameBoyDebugSession : IGameBoyDebugSession, IRgbFrameSource, IDisposable
{
    private const int ScreenWidth = 160;
    private const int ScreenHeight = 144;
    private const int ScreenPixelCount = ScreenWidth * ScreenHeight;
    private static readonly JoypadButton[] CanonicalButtons =
    [
        JoypadButton.Right,
        JoypadButton.Left,
        JoypadButton.Up,
        JoypadButton.Down,
        JoypadButton.A,
        JoypadButton.B,
        JoypadButton.Select,
        JoypadButton.Start,
    ];
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
    private readonly BreakpointCollection breakpoints = new();
    private readonly SymbolService symbols = new();
    private IntPtr handle;
    private bool disposed;
    private bool romLoaded;
    private string? romTitle;
    private string? romModel;
    private ulong totalFrames;
    private ulong totalCycles;
    private ulong totalInstructions;

    public DebugResult<LoadRomResult> LoadRom(string path)
    {
        var native = EnsureHandle<LoadRomResult>();
        if (!native.IsSuccess)
        {
            return native;
        }

        if (!File.Exists(path))
        {
            return DebugResult<LoadRomResult>.Failure("rom_not_found", $"ROM was not found: {path}");
        }

        var title = new StringBuilder(64);
        var model = new StringBuilder(16);
        var result = SameBoyNative.LoadRom(handle, path, title, (UIntPtr)title.Capacity, model, (UIntPtr)model.Capacity);
        if (result != 0)
        {
            return NativeFailure<LoadRomResult>("load_rom_failed");
        }

        breakpoints.ClearAll();
        totalFrames = 0;
        totalCycles = 0;
        totalInstructions = 0;
        romLoaded = true;
        romTitle = title.ToString();
        romModel = model.ToString();
        return DebugResult<LoadRomResult>.Success(new LoadRomResult(true, romTitle, romModel));
    }

    public DebugResult<ResetResult> Reset()
    {
        var native = EnsureHandle<ResetResult>();
        if (!native.IsSuccess)
        {
            return native;
        }

        var result = SameBoyNative.Reset(handle);
        if (result != 0)
        {
            return NativeFailure<ResetResult>("reset_failed");
        }

        totalFrames = 0;
        totalCycles = 0;
        totalInstructions = 0;
        return DebugResult<ResetResult>.Success(new ResetResult(true));
    }

    public DebugResult<SaveStateResult> SaveState(string path)
    {
        var native = EnsureHandle<SaveStateResult>();
        if (!native.IsSuccess)
        {
            return native;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return DebugResult<SaveStateResult>.Failure("invalid_path", "Save state path is required.");
        }

        return SameBoyNative.SaveState(handle, path) == 0
            ? DebugResult<SaveStateResult>.Success(new SaveStateResult(true, path))
            : NativeFailure<SaveStateResult>("save_state_failed");
    }

    public DebugResult<LoadStateResult> LoadState(string path)
    {
        var native = EnsureHandle<LoadStateResult>();
        if (!native.IsSuccess)
        {
            return native;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return DebugResult<LoadStateResult>.Failure("invalid_path", "Save state path is required.");
        }

        if (!File.Exists(path))
        {
            return DebugResult<LoadStateResult>.Failure("state_not_found", $"Save state was not found: {path}");
        }

        return SameBoyNative.LoadState(handle, path) == 0
            ? DebugResult<LoadStateResult>.Success(new LoadStateResult(true, path))
            : NativeFailure<LoadStateResult>("load_state_failed");
    }

    public DebugResult<StepInstructionResult> StepInstruction(int count)
    {
        var before = ReadRegisters();
        if (!before.IsSuccess)
        {
            return DebugResult<StepInstructionResult>.Failure(before.Error!.Code, before.Error.Message);
        }

        var disassembly = Disassemble(ParseWord(before.Value.Pc), Math.Min(count, 16));
        for (var i = 0; i < count; i++)
        {
            var step = StepOnce();
            if (!step.IsSuccess)
            {
                return DebugResult<StepInstructionResult>.Failure(step.Error!.Code, step.Error.Message);
            }
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
        var native = EnsureHandle<RunFrameResult>();
        if (!native.IsSuccess)
        {
            return native;
        }

        for (var i = 0; i < count; i++)
        {
            if (SameBoyNative.RunFrame(handle) != 0)
            {
                return NativeFailure<RunFrameResult>("run_frame_failed");
            }
        }

        var registers = ReadRegisters();
        if (!registers.IsSuccess)
        {
            return DebugResult<RunFrameResult>.Failure(registers.Error!.Code, registers.Error.Message);
        }

        var hitBreakpoint = IsBreakpointHit(ParseWord(registers.Value.Pc), registers.Value);
        if (!hitBreakpoint.IsSuccess)
        {
            return DebugResult<RunFrameResult>.Failure(hitBreakpoint.Error!.Code, hitBreakpoint.Error.Message);
        }

        totalFrames += (uint)count;
        totalCycles += (ulong)count * 70224UL;

        return DebugResult<RunFrameResult>.Success(new RunFrameResult(count, registers.Value, hitBreakpoint.Value, GetTimeline()));
    }

    public DebugResult<JoypadStateResult> SetJoypad(IReadOnlyList<JoypadButton> pressedButtons)
    {
        var native = EnsureHandle<JoypadStateResult>();
        if (!native.IsSuccess)
        {
            return native;
        }

        var mask = ToButtonMask(pressedButtons);
        if (!mask.IsSuccess)
        {
            return DebugResult<JoypadStateResult>.Failure(mask.Error!.Code, mask.Error.Message);
        }

        return SameBoyNative.SetJoypad(handle, mask.Value) == 0
            ? DebugResult<JoypadStateResult>.Success(ToJoypadState(mask.Value))
            : NativeFailure<JoypadStateResult>("set_joypad_failed");
    }

    public DebugResult<PressButtonsResult> PressButtons(IReadOnlyList<JoypadButton> pressedButtons, int frameCount)
    {
        var pressed = SetJoypad(pressedButtons);
        if (!pressed.IsSuccess)
        {
            return DebugResult<PressButtonsResult>.Failure(pressed.Error!.Code, pressed.Error.Message);
        }

        var run = RunFrame(frameCount);
        var released = SetJoypad([]);
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
        for (var i = 0; i < maxInstructions; i++)
        {
            var registersBefore = ReadRegisters();
            if (!registersBefore.IsSuccess)
            {
                return DebugResult<ContinueResult>.Failure(registersBefore.Error!.Code, registersBefore.Error.Message);
            }

            var pcBefore = ParseWord(registersBefore.Value.Pc);
            var hitBreakpoint = IsBreakpointHit(pcBefore, registersBefore.Value);
            if (!hitBreakpoint.IsSuccess)
            {
                return DebugResult<ContinueResult>.Failure(hitBreakpoint.Error!.Code, hitBreakpoint.Error.Message);
            }

            if (hitBreakpoint.Value)
            {
                return Stop("breakpoint", registersBefore.Value);
            }

            if (registersBefore.Value.Halted)
            {
                return Stop("halt", registersBefore.Value);
            }

            var step = StepOnce();
            if (!step.IsSuccess)
            {
                return DebugResult<ContinueResult>.Failure(step.Error!.Code, step.Error.Message);
            }
        }

        var registers = ReadRegisters();
        if (!registers.IsSuccess)
        {
            return DebugResult<ContinueResult>.Failure(registers.Error!.Code, registers.Error.Message);
        }

        return Stop("maxInstructions", registers.Value);

        DebugResult<ContinueResult> Stop(string reason, CpuRegisters registers)
        {
            return DebugResult<ContinueResult>.Success(new ContinueResult(true, reason, registers.Pc, registers, GetTimeline(), 0));
        }
    }

    public DebugResult<ContinueResult> StepOver(int maxInstructions)
    {
        var before = ReadRegisters();
        if (!before.IsSuccess)
        {
            return DebugResult<ContinueResult>.Failure(before.Error!.Code, before.Error.Message);
        }

        var pc = ParseWord(before.Value.Pc);
        var opcode = ReadBytes(pc, 1);
        if (!opcode.IsSuccess)
        {
            return DebugResult<ContinueResult>.Failure(opcode.Error!.Code, opcode.Error.Message);
        }

        if (!IsCallOrRst(opcode.Value[0], out var length))
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
        if (!BreakpointCondition.TryParse(condition, out var parsedCondition, out var conditionError) || parsedCondition is null)
        {
            return DebugResult<RunUntilConditionResult>.Failure("invalid_condition", $"Invalid condition: {conditionError ?? "Condition is required."}");
        }

        var startFrames = totalFrames;
        for (var i = 0; i < maxInstructions; i++)
        {
            var registers = ReadRegisters();
            if (!registers.IsSuccess)
            {
                return DebugResult<RunUntilConditionResult>.Failure(registers.Error!.Code, registers.Error.Message);
            }

            var conditionResult = parsedCondition.Evaluate(new BreakpointConditionContext(this, registers.Value));
            if (!conditionResult.IsSuccess)
            {
                return DebugResult<RunUntilConditionResult>.Failure(conditionResult.Error!.Code, conditionResult.Error.Message);
            }

            if (conditionResult.Value)
            {
                return StopRunUntilCondition("condition", registers.Value, (uint)i, startFrames);
            }

            if (registers.Value.Halted)
            {
                return StopRunUntilCondition("halt", registers.Value, (uint)i, startFrames);
            }

            if (totalFrames - startFrames >= (ulong)maxFrames)
            {
                return StopRunUntilCondition("maxFrames", registers.Value, (uint)i, startFrames);
            }

            var step = StepOnce();
            if (!step.IsSuccess)
            {
                return DebugResult<RunUntilConditionResult>.Failure(step.Error!.Code, step.Error.Message);
            }
        }

        var final = ReadRegisters();
        return final.IsSuccess
            ? StopRunUntilCondition("maxInstructions", final.Value, (uint)maxInstructions, startFrames)
            : DebugResult<RunUntilConditionResult>.Failure(final.Error!.Code, final.Error.Message);
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
        return DebugResult<WatchpointSetResult>.Failure("watchpoints_not_supported", "Watchpoints are only supported by the managed backend.");
    }

    public DebugResult<WatchpointSetResult> SetWatchpointRange(ushort address, int length, WatchpointMode mode)
    {
        return DebugResult<WatchpointSetResult>.Failure("watchpoints_not_supported", "Watchpoints are only supported by the managed backend.");
    }

    public DebugResult<ClearWatchpointResult> ClearWatchpoint(string watchpointId)
    {
        return DebugResult<ClearWatchpointResult>.Failure("watchpoints_not_supported", "Watchpoints are only supported by the managed backend.");
    }

    public DebugResult<ListWatchpointsResult> ListWatchpoints()
    {
        return DebugResult<ListWatchpointsResult>.Success(new ListWatchpointsResult([]));
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
        var native = EnsureHandle<CpuRegisters>();
        if (!native.IsSuccess)
        {
            return native;
        }

        return SameBoyNative.ReadRegisters(handle, out var registers) == 0
            ? DebugResult<CpuRegisters>.Success(ToRegisters(registers))
            : NativeFailure<CpuRegisters>("read_registers_failed");
    }

    public DebugResult<MemoryReadResult> ReadMemory(ushort address, int length)
    {
        var bytes = ReadBytes(address, length);
        return bytes.IsSuccess
            ? DebugResult<MemoryReadResult>.Success(MemoryFormatter.Format(address, bytes.Value))
            : DebugResult<MemoryReadResult>.Failure(bytes.Error!.Code, bytes.Error.Message);
    }

    public DebugResult<WriteMemoryResult> WriteMemory(ushort address, IReadOnlyList<byte> bytes)
    {
        var native = EnsureHandle<WriteMemoryResult>();
        if (!native.IsSuccess)
        {
            return native;
        }

        var array = bytes.ToArray();
        return SameBoyNative.WriteMemory(handle, address, array, (UIntPtr)array.Length) == 0
            ? DebugResult<WriteMemoryResult>.Success(new WriteMemoryResult(true, Hex.FormatWord(address), array.Length))
            : NativeFailure<WriteMemoryResult>("write_memory_failed");
    }

    public DebugResult<DisassembleResult> Disassemble(ushort address, int instructionCount)
    {
        var native = EnsureHandle<DisassembleResult>();
        if (!native.IsSuccess)
        {
            return native;
        }

        var buffer = new StringBuilder(Math.Max(4096, instructionCount * 96));
        if (SameBoyNative.Disassemble(handle, address, (ushort)instructionCount, buffer, (UIntPtr)buffer.Capacity) != 0)
        {
            return NativeFailure<DisassembleResult>("disassemble_failed");
        }

        var instructions = ParseDisassembly(buffer.ToString(), instructionCount);
        return DebugResult<DisassembleResult>.Success(new DisassembleResult(Hex.FormatWord(address), instructions));
    }

    public DebugResult<OamDumpResult> ReadOam()
    {
        var native = EnsureHandle<OamDumpResult>();
        if (!native.IsSuccess)
        {
            return native;
        }

        var oam = new byte[0xA0];
        if (SameBoyNative.ReadOam(handle, oam, (UIntPtr)oam.Length) != 0)
        {
            return NativeFailure<OamDumpResult>("read_oam_failed");
        }

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
        var lcdc = ReadIo(0xFF40);
        var stat = ReadIo(0xFF41);
        var scy = ReadIo(0xFF42);
        var scx = ReadIo(0xFF43);
        var ly = ReadIo(0xFF44);
        var lyc = ReadIo(0xFF45);
        var bgp = ReadIo(0xFF47);
        var obp0 = ReadIo(0xFF48);
        var obp1 = ReadIo(0xFF49);
        var wy = ReadIo(0xFF4A);
        var wx = ReadIo(0xFF4B);
        var vbk = ReadIo(0xFF4F);
        var reads = new[] { lcdc, stat, scy, scx, ly, lyc, bgp, obp0, obp1, wy, wx, vbk };
        var failed = reads.FirstOrDefault(result => !result.IsSuccess);
        if (!failed.IsSuccess && failed.Error is not null)
        {
            return DebugResult<PpuStateResult>.Failure(failed.Error.Code, failed.Error.Message);
        }

        return DebugResult<PpuStateResult>.Success(PpuStateBuilder.Build(new PpuRegistersSnapshot(
            lcdc.Value,
            stat.Value,
            ly.Value,
            lyc.Value,
            scy.Value,
            scx.Value,
            wy.Value,
            wx.Value,
            bgp.Value,
            obp0.Value,
            obp1.Value,
            vbk.Value,
            romModel == "CGB",
            Dot: null,
            TimingAuthoritative: false,
            Timeline: GetTimeline())));
    }

    public DebugResult<ScreenCaptureResult> CaptureScreen()
    {
        var native = EnsureHandle<ScreenCaptureResult>();
        if (!native.IsSuccess)
        {
            return native;
        }

        var pixels = new uint[ScreenPixelCount];
        if (SameBoyNative.CaptureScreen(handle, pixels, (UIntPtr)pixels.Length) != 0)
        {
            return NativeFailure<ScreenCaptureResult>("capture_screen_failed");
        }

        var data = PngEncoder.EncodeRgb24(pixels, ScreenWidth, ScreenHeight);
        return DebugResult<ScreenCaptureResult>.Success(new ScreenCaptureResult(ScreenWidth, ScreenHeight, "image/png", data));
    }

    public DebugResult<LastWriterResult> FindLastWriter(ushort address)
    {
        var native = EnsureHandle<LastWriterResult>();
        if (!native.IsSuccess)
        {
            return native;
        }

        var result = SameBoyNative.GetLastWriter(handle, address, out var pc, out var value, out var count);
        if (result < 0)
        {
            return NativeFailure<LastWriterResult>("find_last_writer_failed");
        }

        return DebugResult<LastWriterResult>.Success(result == 0
            ? new LastWriterResult(true, Hex.FormatWord(address), Hex.FormatWord(pc), Hex.FormatByte(value), count)
            : new LastWriterResult(false, Hex.FormatWord(address), null, null, 0));
    }

    public DebugResult<LastWritersResult> FindLastWriters(ushort address, int length)
    {
        if (length < 1 || address + length > 0x10000)
        {
            return DebugResult<LastWritersResult>.Failure("invalid_range", "Writer range must fit within 0x0000..0xFFFF.");
        }

        var writers = new List<LastWriterResult>(length);
        for (var i = 0; i < length; i++)
        {
            var writer = FindLastWriter((ushort)(address + i));
            if (!writer.IsSuccess)
            {
                return DebugResult<LastWritersResult>.Failure(writer.Error!.Code, writer.Error.Message);
            }

            writers.Add(writer.Value);
        }

        return DebugResult<LastWritersResult>.Success(new LastWritersResult(writers));
    }

    public DebugResult<TraceUntilWriteResult> TraceUntilWrite(ushort address, int maxInstructions)
    {
        var native = EnsureHandle<TraceUntilWriteResult>();
        if (!native.IsSuccess)
        {
            return native;
        }

        var result = SameBoyNative.TraceUntilWrite(handle, address, (uint)maxInstructions, out var instructionsRun, out var pc, out var value);
        if (result < 0)
        {
            return NativeFailure<TraceUntilWriteResult>("trace_until_write_failed");
        }

        var registers = ReadRegisters();
        if (!registers.IsSuccess)
        {
            return DebugResult<TraceUntilWriteResult>.Failure(registers.Error!.Code, registers.Error.Message);
        }

        return DebugResult<TraceUntilWriteResult>.Success(result == 0
            ? new TraceUntilWriteResult(true, "write", Hex.FormatWord(address), Hex.FormatWord(pc), Hex.FormatByte(value), instructionsRun, registers.Value, GetTimeline())
            : new TraceUntilWriteResult(true, "maxInstructions", Hex.FormatWord(address), null, null, instructionsRun, registers.Value, GetTimeline()));
    }

    public DebugResult<TraceUntilWriteRangeResult> TraceUntilWriteRange(ushort address, int length, int maxInstructions)
    {
        if (length == 1)
        {
            var trace = TraceUntilWrite(address, maxInstructions);
            if (!trace.IsSuccess)
            {
                return DebugResult<TraceUntilWriteRangeResult>.Failure(trace.Error!.Code, trace.Error.Message);
            }

            var ppu = ReadPpuState();
            if (!ppu.IsSuccess)
            {
                return DebugResult<TraceUntilWriteRangeResult>.Failure(ppu.Error!.Code, ppu.Error.Message);
            }

            var disassembly = Disassemble(trace.Value.Pc is null ? ParseWord(trace.Value.Registers.Pc) : ParseWord(trace.Value.Pc), 4);
            if (!disassembly.IsSuccess)
            {
                return DebugResult<TraceUntilWriteRangeResult>.Failure(disassembly.Error!.Code, disassembly.Error.Message);
            }

            return DebugResult<TraceUntilWriteRangeResult>.Success(new TraceUntilWriteRangeResult(
                trace.Value.Stopped,
                trace.Value.Reason,
                trace.Value.Address,
                1,
                trace.Value.Reason == "write" ? trace.Value.Address : null,
                trace.Value.Pc,
                trace.Value.Value,
                trace.Value.InstructionsRun,
                trace.Value.Registers,
                ppu.Value,
                disassembly.Value,
                GetTimeline()));
        }

        return DebugResult<TraceUntilWriteRangeResult>.Failure("range_trace_not_supported", "Range tracing is only supported by the managed backend.");
    }

    public DebugResult<VideoWriteTraceResult> TraceVideoWrites(VideoWriteTraceRequest request) =>
        DebugResult<VideoWriteTraceResult>.Failure(
            "video_write_trace_not_supported",
            "Continuous VRAM, OAM, and PPU-register tracing is only supported by the managed backend.");

    public DebugResult<TilemapDumpResult> DumpTilemap(ushort address)
    {
        var bytes = ReadBytes(address, 32 * 32);
        if (!bytes.IsSuccess)
        {
            return DebugResult<TilemapDumpResult>.Failure(bytes.Error!.Code, bytes.Error.Message);
        }

        var rows = Enumerable.Range(0, 32)
            .Select(row => Hex.FormatBytes(bytes.Value.Skip(row * 32).Take(32)))
            .ToArray();

        return DebugResult<TilemapDumpResult>.Success(new TilemapDumpResult(Hex.FormatWord(address), 32, 32, rows));
    }

    public DebugResult<TilemapSetDumpResult> DumpTilemaps(bool includeDetails) =>
        DebugResult<TilemapSetDumpResult>.Failure(
            "tilemap_snapshot_not_supported",
            "Atomic multi-bank tilemap snapshots are only supported by the managed backend.");

    public DebugResult<TilesetDumpResult> DumpTileset(ushort address, int tileCount)
    {
        var bytes = ReadBytes(address, tileCount * 16);
        if (!bytes.IsSuccess)
        {
            return DebugResult<TilesetDumpResult>.Failure(bytes.Error!.Code, bytes.Error.Message);
        }

        var tiles = Enumerable.Range(0, tileCount)
            .Select(index => new TileDump(index, Hex.FormatWord((ushort)(address + index * 16)), Hex.FormatBytes(bytes.Value.Skip(index * 16).Take(16))))
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

        var bytes = ReadBytes(resolved.Value.Address, length ?? 1);
        return bytes.IsSuccess
            ? DebugResult<ReadSymbolResult>.Success(new ReadSymbolResult(name, Hex.FormatWord(resolved.Value.Address), bytes.Value, Hex.FormatBytes(bytes.Value)))
            : DebugResult<ReadSymbolResult>.Failure(bytes.Error!.Code, bytes.Error.Message);
    }

    public DebugResult<ScreenRegionResult> ReadScreenRegion(int x, int y, int width, int height, string format)
    {
        return DebugResult<ScreenRegionResult>.Failure("screen_region_not_supported", "Screen region probes are only supported by the managed backend.");
    }

    public DebugResult<ScreenObservationResult> ObserveScreen(int frameCount)
    {
        if (breakpoints.HasAny)
        {
            return DebugResult<ScreenObservationResult>.Failure(
                "screen_observation_breakpoints_not_supported",
                "The SameBoy frame API cannot stop at managed breakpoints during screen observation. Clear breakpoints or use the managed backend.");
        }

        return ScreenObserver.Observe(this, frameCount);
    }

    public DebugResult<ExecutionObservationResult> ObserveExecution(ExecutionObservationRequest request) =>
        DebugResult<ExecutionObservationResult>.Failure(
            "execution_observation_not_supported",
            "Correlated multi-bank execution observation is only supported by the managed backend.");

    public DebugResult<int> CopyRgbFrame(Memory<uint> destination)
    {
        var native = EnsureHandle<int>();
        if (!native.IsSuccess)
        {
            return native;
        }

        if (destination.Length < ScreenPixelCount)
        {
            return DebugResult<int>.Failure(
                "invalid_screen_frame_buffer",
                $"destination must contain at least {ScreenPixelCount} pixels.");
        }

        var pixels = new uint[ScreenPixelCount];
        if (SameBoyNative.CaptureScreen(handle, pixels, (UIntPtr)pixels.Length) != 0)
        {
            return NativeFailure<int>("capture_screen_failed");
        }

        pixels.CopyTo(destination);
        return DebugResult<int>.Success(ScreenPixelCount);
    }

    public DebugResult<InputTimelineResult> RunInputTimeline(IReadOnlyList<InputTimelineStep> steps)
    {
        var stepResults = new List<InputTimelineStepResult>(steps.Count);
        var framesRun = 0;
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

                var set = SetJoypad(buttons.Value);
                if (!set.IsSuccess)
                {
                    return DebugResult<InputTimelineResult>.Failure(set.Error!.Code, set.Error.Message);
                }

                var run = RunFrame(step.Frames);
                if (!run.IsSuccess)
                {
                    return DebugResult<InputTimelineResult>.Failure(run.Error!.Code, run.Error.Message);
                }

                framesRun += run.Value.FramesRun;
                stepResults.Add(new InputTimelineStepResult(
                    index,
                    run.Value.FramesRun,
                    totalFrames,
                    buttons.Value.Select(ToButtonName).ToArray(),
                    step.ReadRegisters ? ReadRegisters().Value : null,
                    step.ReadPpuState ? ReadPpuState().Value : null,
                    step.DumpOam ? ReadOam().Value : null,
                    step.Capture ? CaptureScreen().Value : null,
                    null,
                    null,
                    GetTimeline()));
            }
        }
        finally
        {
            _ = SetJoypad([]);
        }

        var released = SetJoypad([]);
        if (!released.IsSuccess)
        {
            return DebugResult<InputTimelineResult>.Failure(released.Error!.Code, released.Error.Message);
        }

        return DebugResult<InputTimelineResult>.Success(new InputTimelineResult(framesRun, released.Value, stepResults, GetTimeline()));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        if (handle != IntPtr.Zero)
        {
            SameBoyNative.Destroy(handle);
            handle = IntPtr.Zero;
        }

        romLoaded = false;
        romTitle = null;
        romModel = null;

        disposed = true;
    }

    private DebugResult<bool> ShouldBreak(BreakpointInfo breakpoint, CpuRegisters registers)
    {
        if (breakpoint.ParsedCondition is null)
        {
            return string.IsNullOrWhiteSpace(breakpoint.Condition)
                ? DebugResult<bool>.Success(true)
                : DebugResult<bool>.Failure("invalid_breakpoint_condition", $"Breakpoint '{breakpoint.Id}' has an invalid condition.");
        }

        return breakpoint.ParsedCondition.Evaluate(new BreakpointConditionContext(this, registers));
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

    private DebugResult<ContinueResult> StepSingle(string reason)
    {
        var step = StepOnce();
        if (!step.IsSuccess)
        {
            return DebugResult<ContinueResult>.Failure(step.Error!.Code, step.Error.Message);
        }

        var registers = ReadRegisters();
        if (!registers.IsSuccess)
        {
            return DebugResult<ContinueResult>.Failure(registers.Error!.Code, registers.Error.Message);
        }

        var breakpoint = IsBreakpointHit(ParseWord(registers.Value.Pc), registers.Value);
        if (!breakpoint.IsSuccess)
        {
            return DebugResult<ContinueResult>.Failure(breakpoint.Error!.Code, breakpoint.Error.Message);
        }

        if (breakpoint.Value)
        {
            return Stop("breakpoint", registers.Value);
        }

        return registers.Value.Halted ? Stop("halt", registers.Value) : Stop(reason, registers.Value);
    }

    private DebugResult<ContinueResult> StepUntil(int maxInstructions, Func<CpuRegisters, bool> completed, string completedReason)
    {
        for (var i = 0; i < maxInstructions; i++)
        {
            var before = ReadRegisters();
            if (!before.IsSuccess)
            {
                return DebugResult<ContinueResult>.Failure(before.Error!.Code, before.Error.Message);
            }

            if (before.Value.Halted)
            {
                return Stop("halt", before.Value);
            }

            var step = StepOnce();
            if (!step.IsSuccess)
            {
                return DebugResult<ContinueResult>.Failure(step.Error!.Code, step.Error.Message);
            }

            var registers = ReadRegisters();
            if (!registers.IsSuccess)
            {
                return DebugResult<ContinueResult>.Failure(registers.Error!.Code, registers.Error.Message);
            }

            var breakpoint = IsBreakpointHit(ParseWord(registers.Value.Pc), registers.Value);
            if (!breakpoint.IsSuccess)
            {
                return DebugResult<ContinueResult>.Failure(breakpoint.Error!.Code, breakpoint.Error.Message);
            }

            if (breakpoint.Value)
            {
                return Stop("breakpoint", registers.Value);
            }

            if (registers.Value.Halted)
            {
                return Stop("halt", registers.Value);
            }

            if (completed(registers.Value))
            {
                return Stop(completedReason, registers.Value);
            }
        }

        var final = ReadRegisters();
        return final.IsSuccess
            ? Stop("maxInstructions", final.Value)
            : DebugResult<ContinueResult>.Failure(final.Error!.Code, final.Error.Message);
    }

    private DebugResult<ContinueResult> Stop(string reason, CpuRegisters registers)
    {
        return DebugResult<ContinueResult>.Success(new ContinueResult(true, reason, registers.Pc, registers, GetTimeline(), 0));
    }

    private static bool IsCallOrRst(byte opcode, out int length)
    {
        length = opcode is 0xCD or 0xC4 or 0xCC or 0xD4 or 0xDC ? 3 : 1;
        return opcode is 0xCD or 0xC4 or 0xCC or 0xD4 or 0xDC
            or 0xC7 or 0xCF or 0xD7 or 0xDF or 0xE7 or 0xEF or 0xF7 or 0xFF;
    }

    private DebugResult<byte[]> ReadBytes(ushort address, int length)
    {
        var native = EnsureHandle<byte[]>();
        if (!native.IsSuccess)
        {
            return native;
        }

        var buffer = new byte[length];
        return SameBoyNative.ReadMemory(handle, address, buffer, (UIntPtr)buffer.Length) == 0
            ? DebugResult<byte[]>.Success(buffer)
            : NativeFailure<byte[]>("read_memory_failed");
    }

    private DebugResult<byte> ReadIo(ushort address)
    {
        var bytes = ReadBytes(address, 1);
        return bytes.IsSuccess
            ? DebugResult<byte>.Success(bytes.Value[0])
            : DebugResult<byte>.Failure(bytes.Error!.Code, bytes.Error.Message);
    }

    private sealed class BreakpointConditionContext(SameBoyDebugSession session, CpuRegisters registers) : IBreakpointConditionContext
    {
        public CpuRegisters Registers { get; } = registers;

        public DebugResult<byte> ReadByte(ushort address)
        {
            var bytes = session.ReadBytes(address, 1);
            return bytes.IsSuccess
                ? DebugResult<byte>.Success(bytes.Value[0])
                : DebugResult<byte>.Failure(bytes.Error!.Code, bytes.Error.Message);
        }
    }

    private DebugResult<bool> StepOnce()
    {
        var native = EnsureHandle<bool>();
        if (!native.IsSuccess)
        {
            return native;
        }

        if (SameBoyNative.Step(handle) != 0)
        {
            return NativeFailure<bool>("step_instruction_failed");
        }

        totalInstructions++;
        return DebugResult<bool>.Success(true);
    }

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

    private static string ToButtonName(JoypadButton button)
    {
        return button switch
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
    }

    private TimelineCounters GetTimeline() => new(totalFrames, totalCycles, totalInstructions);

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

    private DebugResult<T> EnsureHandle<T>()
    {
        if (disposed)
        {
            return DebugResult<T>.Failure("session_disposed", "The SameBoy debug session has been disposed.");
        }

        if (handle != IntPtr.Zero)
        {
            return DebugResult<T>.Success(default!);
        }

        try
        {
            handle = SameBoyNative.Create();
            return handle != IntPtr.Zero
                ? DebugResult<T>.Success(default!)
                : DebugResult<T>.Failure("sameboy_create_failed", "SameBoy native session could not be created.");
        }
        catch (DllNotFoundException ex)
        {
            return DebugResult<T>.Failure("sameboy_native_not_found", ex.Message);
        }
        catch (EntryPointNotFoundException ex)
        {
            return DebugResult<T>.Failure("sameboy_bridge_incompatible", ex.Message);
        }
    }

    private DebugResult<T> NativeFailure<T>(string code)
    {
        var buffer = new StringBuilder(512);
        var message = "SameBoy native call failed.";
        if (handle != IntPtr.Zero && SameBoyNative.GetLastError(handle, buffer, (UIntPtr)buffer.Capacity) == 0 && buffer.Length > 0)
        {
            message = buffer.ToString();
        }

        return DebugResult<T>.Failure(code, message);
    }

    private static CpuRegisters ToRegisters(NativeRegisters registers)
    {
        return new CpuRegisters(
            Hex.FormatWord(registers.Af),
            Hex.FormatWord(registers.Bc),
            Hex.FormatWord(registers.De),
            Hex.FormatWord(registers.Hl),
            Hex.FormatWord(registers.Sp),
            Hex.FormatWord(registers.Pc),
            Hex.FormatByte(registers.A),
            Hex.FormatByte(registers.F),
            Hex.FormatByte(registers.B),
            Hex.FormatByte(registers.C),
            Hex.FormatByte(registers.D),
            Hex.FormatByte(registers.E),
            Hex.FormatByte(registers.H),
            Hex.FormatByte(registers.L),
            registers.Ime,
            registers.Halted);
    }

    private IReadOnlyList<DisassembledInstruction> ParseDisassembly(string text, int maxInstructions)
    {
        var parsed = new List<(ushort Address, string Text, string? Symbol)>();
        string? pendingSymbol = null;

        foreach (var rawLine in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = rawLine.Trim();
            if (line.EndsWith(':') && !line.Contains(' '))
            {
                pendingSymbol = line[..^1];
                continue;
            }

            if (line.StartsWith("->", StringComparison.Ordinal))
            {
                line = line[2..].TrimStart();
            }

            if (line.Length < 4 || !ushort.TryParse(line[..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address))
            {
                continue;
            }

            var separator = line.IndexOf(": ", StringComparison.Ordinal);
            if (separator < 0)
            {
                separator = line.IndexOf(">: ", StringComparison.Ordinal);
                if (separator >= 0)
                {
                    separator++;
                }
            }

            if (separator < 0)
            {
                continue;
            }

            parsed.Add((address, line[(separator + 2)..].Trim(), pendingSymbol));
            pendingSymbol = null;
            if (parsed.Count == maxInstructions)
            {
                break;
            }
        }

        return parsed.Select(item =>
        {
            var bytes = ReadBytes(item.Address, GetInstructionLength(item.Address));
            return new DisassembledInstruction(
                Hex.FormatWord(item.Address),
                bytes.IsSuccess ? Hex.FormatBytes(bytes.Value) : "",
                item.Text,
                item.Symbol);
        }).ToArray();
    }

    private int GetInstructionLength(ushort address)
    {
        var opcode = ReadBytes(address, 1);
        if (!opcode.IsSuccess)
        {
            return 1;
        }

        return InstructionLengths[opcode.Value[0]];
    }

    private static ushort ParseWord(string text)
    {
        var normalized = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        return ushort.Parse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static readonly byte[] InstructionLengths =
    [
        1,3,1,1,1,1,2,1,3,1,1,1,1,1,2,1,
        2,3,1,1,1,1,2,1,2,1,1,1,1,1,2,1,
        2,3,1,1,1,1,2,1,2,1,1,1,1,1,2,1,
        2,3,1,1,1,1,2,1,2,1,1,1,1,1,2,1,
        1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,
        1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,
        1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,
        1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,
        1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,
        1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,
        1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,
        1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,
        1,1,3,3,3,1,2,1,1,1,3,2,3,3,2,1,
        1,1,3,1,3,1,2,1,1,1,3,1,3,1,2,1,
        2,1,1,1,1,1,2,1,2,1,3,1,1,1,2,1,
        2,1,1,1,1,1,2,1,2,1,3,1,1,1,2,1,
    ];
}
