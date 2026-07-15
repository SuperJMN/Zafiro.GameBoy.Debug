using System.ComponentModel;
using GameBoy.Debug.Core;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GameBoy.Debug.Mcp;

[McpServerToolType]
public static class GameBoyDebugTools
{
    private const int MaxMemoryLength = 4096;
    private const int MaxInstructionCount = 10000;
    private const int MaxDisassemblyCount = 256;
    private const int MaxFrameCount = 600;
    private const int MaxContinueInstructions = 10_000_000;
    private const int MaxTraceInstructions = 10_000_000;
    private const int MaxTileCount = 384;
    private const int MaxWatchpointRangeLength = 4096;
    private const int MaxInputTimelineSteps = 128;
    private const int MaxInputTimelineFrames = 3600;
    private const int ScreenWidth = 160;
    private const int ScreenHeight = 144;
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

    [McpServerTool(Name = "load_rom", ReadOnly = false, Destructive = false)]
    [Description("Loads a Game Boy or Game Boy Color ROM into the active SameBoy debug session.")]
    public static object LoadRom(IGameBoyDebugSession session, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Error("invalid_path", "ROM path is required.");
        }

        return ToToolResult(session.LoadRom(path));
    }

    [McpServerTool(Name = "save_state", ReadOnly = false, Destructive = false)]
    [Description("Saves the active emulator state to a SameBoy savestate file.")]
    public static object SaveState(IGameBoyDebugSession session, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Error("invalid_path", "Save state path is required.");
        }

        return ToToolResult(session.SaveState(path));
    }

    [McpServerTool(Name = "load_state", ReadOnly = false, Destructive = true)]
    [Description("Loads a SameBoy savestate file into the active emulator session.")]
    public static object LoadState(IGameBoyDebugSession session, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Error("invalid_path", "Save state path is required.");
        }

        return ToToolResult(session.LoadState(path));
    }

    [McpServerTool(Name = "reset", ReadOnly = false, Destructive = false)]
    [Description("Resets the active emulator session.")]
    public static object Reset(IGameBoyDebugSession session) => ToToolResult(session.Reset());

    [McpServerTool(Name = "step_instruction", ReadOnly = false, Destructive = false)]
    [Description("Steps one or more CPU instructions and returns the resulting registers and local disassembly.")]
    public static object StepInstruction(IGameBoyDebugSession session, int count = 1)
    {
        if (count is < 1 or > MaxInstructionCount)
        {
            return Error("invalid_count", $"Instruction count must be between 1 and {MaxInstructionCount}.");
        }

        return ToToolResult(session.StepInstruction(count));
    }

    [McpServerTool(Name = "run_frame", ReadOnly = false, Destructive = false)]
    [Description("Runs the emulator for one or more frames.")]
    public static object RunFrame(IGameBoyDebugSession session, int count = 1)
    {
        if (count is < 1 or > MaxFrameCount)
        {
            return Error("invalid_count", $"Frame count must be between 1 and {MaxFrameCount}.");
        }

        return ToToolResult(session.RunFrame(count));
    }

    [McpServerTool(Name = "set_joypad", ReadOnly = false, Destructive = false)]
    [Description("Sets the currently held joypad buttons. Pass an empty array to release every button.")]
    public static object SetJoypad(IGameBoyDebugSession session, string[] buttons)
    {
        var parsed = ParseButtons(buttons);
        return parsed.IsSuccess
            ? ToToolResult(session.SetJoypad(parsed.Value))
            : new ToolError(parsed.Error!);
    }

    [McpServerTool(Name = "press_buttons", ReadOnly = false, Destructive = false)]
    [Description("Holds one or more joypad buttons for a bounded number of frames, then releases them.")]
    public static object PressButtons(IGameBoyDebugSession session, string[] buttons, int frameCount = 1)
    {
        if (frameCount is < 1 or > MaxFrameCount)
        {
            return Error("invalid_frame_count", $"frameCount must be between 1 and {MaxFrameCount}.");
        }

        var parsed = ParseButtons(buttons);
        return parsed.IsSuccess
            ? ToToolResult(session.PressButtons(parsed.Value, frameCount))
            : new ToolError(parsed.Error!);
    }

    [McpServerTool(Name = "continue_until_break", ReadOnly = false, Destructive = false)]
    [Description("Continues execution until a breakpoint, halt, error, or the explicit instruction limit.")]
    public static object ContinueUntilBreak(IGameBoyDebugSession session, int maxInstructions = 1_000_000)
    {
        if (maxInstructions is < 1 or > MaxContinueInstructions)
        {
            return Error("invalid_max_instructions", $"maxInstructions must be between 1 and {MaxContinueInstructions}.");
        }

        return ToToolResult(session.ContinueUntilBreak(maxInstructions));
    }

    [McpServerTool(Name = "step_over", ReadOnly = false, Destructive = false)]
    [Description("Steps over a call-like instruction, or steps one instruction when not on a call.")]
    public static object StepOver(IGameBoyDebugSession session, int maxInstructions = 100_000)
    {
        if (maxInstructions is < 1 or > MaxContinueInstructions)
        {
            return Error("invalid_max_instructions", $"maxInstructions must be between 1 and {MaxContinueInstructions}.");
        }

        return ToToolResult(session.StepOver(maxInstructions));
    }

    [McpServerTool(Name = "step_out", ReadOnly = false, Destructive = false)]
    [Description("Runs until the current subroutine returns, a breakpoint/watchpoint hits, halt, or the instruction limit.")]
    public static object StepOut(IGameBoyDebugSession session, int maxInstructions = 100_000)
    {
        if (maxInstructions is < 1 or > MaxContinueInstructions)
        {
            return Error("invalid_max_instructions", $"maxInstructions must be between 1 and {MaxContinueInstructions}.");
        }

        return ToToolResult(session.StepOut(maxInstructions));
    }

    [McpServerTool(Name = "run_until_condition", ReadOnly = false, Destructive = false)]
    [Description("Runs until a register, PPU/IO alias, or memory condition is true, or a bounded stop condition is reached.")]
    public static object RunUntilCondition(IGameBoyDebugSession session, string condition, int maxInstructions = 1_000_000, int maxFrames = 120)
    {
        if (maxInstructions is < 1 or > MaxContinueInstructions)
        {
            return Error("invalid_max_instructions", $"maxInstructions must be between 1 and {MaxContinueInstructions}.");
        }

        if (maxFrames is < 1 or > MaxFrameCount)
        {
            return Error("invalid_max_frames", $"maxFrames must be between 1 and {MaxFrameCount}.");
        }

        if (!BreakpointCondition.TryParse(condition, out var parsed, out var conditionError) || parsed is null)
        {
            return Error("invalid_condition", $"Invalid condition: {conditionError ?? "Condition is required."}");
        }

        return ToToolResult(session.RunUntilCondition(condition, maxInstructions, maxFrames));
    }

    [McpServerTool(Name = "set_breakpoint", ReadOnly = false, Destructive = false)]
    [Description("Sets an execution breakpoint at a 16-bit CPU address.")]
    public static object SetBreakpoint(IGameBoyDebugSession session, string address, string? condition = null)
    {
        var parsed = ParseAddress(address);
        if (!parsed.IsSuccess)
        {
            return new ToolError(parsed.Error!);
        }

        if (!BreakpointCondition.TryParse(condition, out _, out var conditionError))
        {
            return Error("invalid_breakpoint_condition", $"Invalid breakpoint condition: {conditionError}");
        }

        return ToToolResult(session.SetBreakpoint(parsed.Value.Address, condition));
    }

    [McpServerTool(Name = "clear_breakpoint", ReadOnly = false, Destructive = false)]
    [Description("Clears a breakpoint by breakpoint id.")]
    public static object ClearBreakpoint(IGameBoyDebugSession session, string breakpointId)
    {
        if (string.IsNullOrWhiteSpace(breakpointId))
        {
            return Error("invalid_breakpoint_id", "breakpointId is required.");
        }

        return ToToolResult(session.ClearBreakpoint(breakpointId));
    }

    [McpServerTool(Name = "list_breakpoints", ReadOnly = true, Destructive = false)]
    [Description("Lists all breakpoints currently registered in the active session.")]
    public static object ListBreakpoints(IGameBoyDebugSession session) => ToToolResult(session.ListBreakpoints());

    [McpServerTool(Name = "set_watchpoint", ReadOnly = false, Destructive = false)]
    [Description("Sets a memory watchpoint at a 16-bit CPU address. mode is read, write, or access.")]
    public static object SetWatchpoint(IGameBoyDebugSession session, string address, string mode = "write")
    {
        var parsed = ParseAddress(address);
        if (!parsed.IsSuccess)
        {
            return new ToolError(parsed.Error!);
        }

        var parsedMode = ParseWatchpointMode(mode);
        return parsedMode.IsSuccess
            ? ToToolResult(session.SetWatchpoint(parsed.Value.Address, parsedMode.Value))
            : new ToolError(parsedMode.Error!);
    }

    [McpServerTool(Name = "set_watchpoint_range", ReadOnly = false, Destructive = false)]
    [Description("Sets a bounded memory watchpoint range. mode is read, write, or access.")]
    public static object SetWatchpointRange(IGameBoyDebugSession session, string address, int length, string mode = "write")
    {
        var parsed = ParseAddress(address);
        if (!parsed.IsSuccess)
        {
            return new ToolError(parsed.Error!);
        }

        var range = ValidateAddressRange(parsed.Value.Address, length, MaxWatchpointRangeLength);
        if (!range.IsSuccess)
        {
            return new ToolError(range.Error!);
        }

        var parsedMode = ParseWatchpointMode(mode);
        return parsedMode.IsSuccess
            ? ToToolResult(session.SetWatchpointRange(parsed.Value.Address, length, parsedMode.Value))
            : new ToolError(parsedMode.Error!);
    }

    [McpServerTool(Name = "clear_watchpoint", ReadOnly = false, Destructive = false)]
    [Description("Clears a watchpoint by watchpoint id.")]
    public static object ClearWatchpoint(IGameBoyDebugSession session, string watchpointId)
    {
        if (string.IsNullOrWhiteSpace(watchpointId))
        {
            return Error("invalid_watchpoint_id", "watchpointId is required.");
        }

        return ToToolResult(session.ClearWatchpoint(watchpointId));
    }

    [McpServerTool(Name = "list_watchpoints", ReadOnly = true, Destructive = false)]
    [Description("Lists all watchpoints currently registered in the active session.")]
    public static object ListWatchpoints(IGameBoyDebugSession session) => ToToolResult(session.ListWatchpoints());

    [McpServerTool(Name = "get_state", ReadOnly = true, Destructive = false)]
    [Description("Returns ROM load status, ROM metadata, halt status, and current PC when available.")]
    public static object GetState(IGameBoyDebugSession session) => ToToolResult(session.GetState());

    [McpServerTool(Name = "read_registers", ReadOnly = true, Destructive = false)]
    [Description("Reads CPU registers from the active session.")]
    public static object ReadRegisters(IGameBoyDebugSession session) => ToToolResult(session.ReadRegisters());

    [McpServerTool(Name = "read_memory", ReadOnly = true, Destructive = false)]
    [Description("Reads a bounded range of CPU address-space memory.")]
    public static object ReadMemory(IGameBoyDebugSession session, string address, int length)
    {
        if (length is < 1 or > MaxMemoryLength)
        {
            return Error("invalid_length", $"Memory length must be between 1 and {MaxMemoryLength} bytes.");
        }

        var parsed = ParseAddress(address);
        return parsed.IsSuccess
            ? ToToolResult(session.ReadMemory(parsed.Value.Address, length))
            : new ToolError(parsed.Error!);
    }

    [McpServerTool(Name = "write_memory", ReadOnly = false, Destructive = true)]
    [Description("Writes a bounded byte array to CPU address-space memory.")]
    public static object WriteMemory(IGameBoyDebugSession session, string address, int[] bytes)
    {
        if (bytes.Length is < 1 or > MaxMemoryLength)
        {
            return Error("invalid_length", $"Write length must be between 1 and {MaxMemoryLength} bytes.");
        }

        if (bytes.Any(value => value is < 0 or > 0xFF))
        {
            return Error("invalid_bytes", "Every byte must be in the inclusive range 0..255.");
        }

        var parsed = ParseAddress(address);
        return parsed.IsSuccess
            ? ToToolResult(session.WriteMemory(parsed.Value.Address, bytes.Select(value => (byte)value).ToArray()))
            : new ToolError(parsed.Error!);
    }

    [McpServerTool(Name = "disassemble", ReadOnly = true, Destructive = false)]
    [Description("Disassembles a bounded number of instructions around a CPU address.")]
    public static object Disassemble(IGameBoyDebugSession session, string address, int instructionCount = 16)
    {
        if (instructionCount is < 1 or > MaxDisassemblyCount)
        {
            return Error("invalid_instruction_count", $"instructionCount must be between 1 and {MaxDisassemblyCount}.");
        }

        var parsed = ParseAddress(address);
        return parsed.IsSuccess
            ? ToToolResult(session.Disassemble(parsed.Value.Address, instructionCount))
            : new ToolError(parsed.Error!);
    }

    [McpServerTool(Name = "load_symbols", ReadOnly = false, Destructive = false)]
    [Description("Loads a RGBDS/BGB/SameBoy-style symbol file into the active session.")]
    public static object LoadSymbols(IGameBoyDebugSession session, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Error("invalid_path", "Symbol file path is required.");
        }

        return ToToolResult(session.LoadSymbols(path));
    }

    [McpServerTool(Name = "resolve_symbol", ReadOnly = true, Destructive = false)]
    [Description("Resolves a loaded symbol name to an address.")]
    public static object ResolveSymbol(IGameBoyDebugSession session, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error("invalid_symbol", "Symbol name is required.");
        }

        return ToToolResult(session.ResolveSymbol(name));
    }

    [McpServerTool(Name = "read_symbol", ReadOnly = true, Destructive = false)]
    [Description("Reads memory by loaded symbol name, with an optional bounded byte length.")]
    public static object ReadSymbol(IGameBoyDebugSession session, string name, int? length = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error("invalid_symbol", "Symbol name is required.");
        }

        if (length is < 1 or > MaxMemoryLength)
        {
            return Error("invalid_length", $"Symbol read length must be between 1 and {MaxMemoryLength} bytes.");
        }

        return ToToolResult(session.ReadSymbol(name, length));
    }

    [McpServerTool(Name = "dump_oam", ReadOnly = true, Destructive = false)]
    [Description("Dumps the 40 OAM sprite entries from the active session.")]
    public static object DumpOam(IGameBoyDebugSession session) => ToToolResult(session.ReadOam());

    [McpServerTool(Name = "read_ppu_state", ReadOnly = true, Destructive = false)]
    [Description("Reads compact PPU/LCD state registers from the active session.")]
    public static object ReadPpuState(IGameBoyDebugSession session) => ToToolResult(session.ReadPpuState());

    [McpServerTool(Name = "capture_screen", ReadOnly = true, Destructive = false)]
    [Description("Captures the current 160x144 screen image as inline PNG image content, or saves it to a safe relative PNG path.")]
    public static object CaptureScreen(IGameBoyDebugSession session, string? path = null, bool includeMetadata = false)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            var safePath = ResolveSafeArtifactPath(path);
            if (!safePath.IsSuccess)
            {
                return new ToolError(safePath.Error!);
            }

            var savedCapture = session.CaptureScreen();
            if (!savedCapture.IsSuccess)
            {
                return new ToolError(savedCapture.Error!);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(safePath.Value.FullPath)!);
            File.WriteAllBytes(safePath.Value.FullPath, savedCapture.Value.Data);
            return new ScreenCaptureArtifactResult(
                savedCapture.Value.Width,
                savedCapture.Value.Height,
                savedCapture.Value.MimeType,
                true,
                safePath.Value.RelativePath,
                includeMetadata ? BuildCaptureMetadata(session) : null);
        }

        var result = session.CaptureScreen();
        return result.IsSuccess
            ? ImageContentBlock.FromBytes(result.Value.Data, result.Value.MimeType)
            : new ToolError(result.Error!);
    }

    [McpServerTool(Name = "find_last_writer", ReadOnly = true, Destructive = false)]
    [Description("Returns the last observed write to an address since the session started or tracing began.")]
    public static object FindLastWriter(IGameBoyDebugSession session, string address)
    {
        var parsed = ParseAddress(address);
        return parsed.IsSuccess
            ? ToToolResult(session.FindLastWriter(parsed.Value.Address))
            : new ToolError(parsed.Error!);
    }

    [McpServerTool(Name = "find_last_writers", ReadOnly = true, Destructive = false)]
    [Description("Returns the last observed writes in a bounded address range.")]
    public static object FindLastWriters(IGameBoyDebugSession session, string address, int length)
    {
        var parsed = ParseAddress(address);
        if (!parsed.IsSuccess)
        {
            return new ToolError(parsed.Error!);
        }

        var range = ValidateAddressRange(parsed.Value.Address, length, MaxWatchpointRangeLength);
        return range.IsSuccess
            ? ToToolResult(session.FindLastWriters(parsed.Value.Address, length))
            : new ToolError(range.Error!);
    }

    [McpServerTool(Name = "trace_until_write", ReadOnly = false, Destructive = false)]
    [Description("Runs until the requested address is written or the explicit instruction limit is reached.")]
    public static object TraceUntilWrite(IGameBoyDebugSession session, string address, int maxInstructions)
    {
        if (maxInstructions is < 1 or > MaxTraceInstructions)
        {
            return Error("invalid_max_instructions", $"maxInstructions must be between 1 and {MaxTraceInstructions}.");
        }

        var parsed = ParseAddress(address);
        return parsed.IsSuccess
            ? ToToolResult(session.TraceUntilWrite(parsed.Value.Address, maxInstructions))
            : new ToolError(parsed.Error!);
    }

    [McpServerTool(Name = "trace_until_write_range", ReadOnly = false, Destructive = false)]
    [Description("Runs until any address in a bounded range is written or the explicit instruction limit is reached.")]
    public static object TraceUntilWriteRange(IGameBoyDebugSession session, string address, int length, int maxInstructions)
    {
        if (maxInstructions is < 1 or > MaxTraceInstructions)
        {
            return Error("invalid_max_instructions", $"maxInstructions must be between 1 and {MaxTraceInstructions}.");
        }

        var parsed = ParseAddress(address);
        if (!parsed.IsSuccess)
        {
            return new ToolError(parsed.Error!);
        }

        var range = ValidateAddressRange(parsed.Value.Address, length, MaxWatchpointRangeLength);
        return range.IsSuccess
            ? ToToolResult(session.TraceUntilWriteRange(parsed.Value.Address, length, maxInstructions))
            : new ToolError(range.Error!);
    }

    [McpServerTool(Name = "trace_video_writes", ReadOnly = false, Destructive = false)]
    [Description("Atomically runs bounded frames and continuously records selected VRAM, OAM, and LCD/PPU-register writes with exact pre/post PPU state.")]
    public static object TraceVideoWrites(
        IGameBoyDebugSession session,
        int frameCount = 1,
        int maxEvents = 1000,
        string[]? kinds = null,
        string[]? ppuRegisters = null,
        string[]? buttons = null)
    {
        if (frameCount is < 1 or > MaxFrameCount)
        {
            return Error("invalid_frame_count", $"frameCount must be between 1 and {MaxFrameCount}.");
        }

        if (maxEvents is < 1 or > VideoWriteTracing.MaxEvents)
        {
            return Error("invalid_max_events", $"maxEvents must be between 1 and {VideoWriteTracing.MaxEvents}.");
        }

        var parsedKinds = ParseVideoWriteKinds(kinds);
        if (!parsedKinds.IsSuccess)
        {
            return new ToolError(parsedKinds.Error!);
        }

        var parsedRegisters = ParsePpuRegisters(ppuRegisters);
        if (!parsedRegisters.IsSuccess)
        {
            return new ToolError(parsedRegisters.Error!);
        }

        var parsedButtons = ParseButtons(buttons ?? []);
        if (!parsedButtons.IsSuccess)
        {
            return new ToolError(parsedButtons.Error!);
        }

        return ToToolResult(session.TraceVideoWrites(new VideoWriteTraceRequest(
            frameCount,
            maxEvents,
            parsedKinds.Value,
            parsedRegisters.Value,
            parsedButtons.Value)));
    }

    [McpServerTool(Name = "read_screen_region", ReadOnly = true, Destructive = false)]
    [Description("Reads deterministic DMG-shade or exact RGB24 data from a bounded screen region. Raw formats return every pixel, including a full frame.")]
    public static object ReadScreenRegion(IGameBoyDebugSession session, int x, int y, int width, int height, string format = "dmg_shades")
    {
        if (x < 0 || y < 0 || width < 1 || height < 1 || x + width > ScreenWidth || y + height > ScreenHeight)
        {
            return Error("invalid_screen_region", "Screen region must fit within 160x144.");
        }

        if (format is null || !new[] { "dmg_shades", "dmg_shades_raw", "rgb24", "rgb24_raw" }
                .Contains(format, StringComparer.OrdinalIgnoreCase))
        {
            return Error("invalid_screen_region_format", "format must be dmg_shades, dmg_shades_raw, rgb24, or rgb24_raw.");
        }

        return ToToolResult(session.ReadScreenRegion(x, y, width, height, format));
    }

    [McpServerTool(Name = "observe_screen", ReadOnly = false, Destructive = false)]
    [Description("Runs frames while collecting exact RGB hashes and compact pixel/tile changes for detecting transient corruption and flicker.")]
    public static object ObserveScreen(IGameBoyDebugSession session, int frameCount = 60)
    {
        if (frameCount is < 1 or > ScreenObserver.MaxFrames)
        {
            return Error("invalid_frame_count", $"frameCount must be between 1 and {ScreenObserver.MaxFrames}.");
        }

        return ToToolResult(session.ObserveScreen(frameCount));
    }

    [McpServerTool(Name = "observe_execution", ReadOnly = false, Destructive = false)]
    [Description("Atomically correlates exact rendered frames, safe RAM/VRAM/OAM probes, optional PPU state, tilemap hashes, bounded video writes, input, breakpoints, and timeline counters.")]
    public static object ObserveExecution(
        IGameBoyDebugSession session,
        int frameCount = 60,
        string[]? buttons = null,
        ExecutionMemoryProbeInput[]? memoryProbes = null,
        bool includePpuState = false,
        bool traceVideoWrites = true,
        int maxVideoEvents = 1000,
        string[]? videoKinds = null,
        string[]? ppuRegisters = null)
    {
        if (frameCount is < 1 or > ExecutionObserver.MaxFrames)
        {
            return Error("invalid_frame_count", $"frameCount must be between 1 and {ExecutionObserver.MaxFrames}.");
        }

        var parsedButtons = ParseButtons(buttons ?? []);
        if (!parsedButtons.IsSuccess)
        {
            return new ToolError(parsedButtons.Error!);
        }

        var probes = new List<MemoryProbe>();
        foreach (var input in memoryProbes ?? [])
        {
            var parsed = ParseAddress(input.Address);
            if (!parsed.IsSuccess)
            {
                return new ToolError(parsed.Error!);
            }

            var probe = new MemoryProbe(parsed.Value.Address, input.Length);
            if (!ExecutionObserver.IsSafeProbe(probe) || input.Length > ExecutionObserver.MaxMemoryProbeLength)
            {
                return Error(
                    "invalid_memory_probe",
                    "Each probe must stay within one side-effect-free VRAM/RAM/OAM region and be at most 64 bytes.");
            }

            probes.Add(probe);
        }

        if (probes.Count > ExecutionObserver.MaxMemoryProbes ||
            probes.Sum(probe => probe.Length) > ExecutionObserver.MaxMemoryBytesPerFrame)
        {
            return Error("invalid_memory_probes", "Memory probes exceed the published count or per-frame byte limit.");
        }

        var parsedKinds = ParseVideoWriteKinds(videoKinds);
        if (!parsedKinds.IsSuccess)
        {
            return new ToolError(parsedKinds.Error!);
        }

        var parsedRegisters = ParsePpuRegisters(ppuRegisters);
        if (!parsedRegisters.IsSuccess)
        {
            return new ToolError(parsedRegisters.Error!);
        }

        if (traceVideoWrites && maxVideoEvents is < 1 or > ExecutionObserver.MaxVideoEvents)
        {
            return Error("invalid_max_video_events", $"maxVideoEvents must be between 1 and {ExecutionObserver.MaxVideoEvents}.");
        }

        return ToToolResult(session.ObserveExecution(new ExecutionObservationRequest(
            frameCount,
            parsedButtons.Value,
            probes,
            includePpuState,
            traceVideoWrites,
            maxVideoEvents,
            parsedKinds.Value,
            parsedRegisters.Value)));
    }

    [McpServerTool(Name = "run_input_timeline", ReadOnly = false, Destructive = false)]
    [Description("Runs a bounded deterministic sequence of complete held-button frame steps atomically.")]
    public static object RunInputTimeline(IGameBoyDebugSession session, InputTimelineStep[] steps)
    {
        if (steps is null || steps.Length is < 1 or > MaxInputTimelineSteps)
        {
            return Error("invalid_steps", $"steps must contain between 1 and {MaxInputTimelineSteps} entries.");
        }

        var totalFrames = 0;
        var normalized = new List<InputTimelineStep>(steps.Length);
        foreach (var step in steps)
        {
            if (step.Frames is < 1 or > MaxFrameCount)
            {
                return Error("invalid_frame_count", $"Each step frames value must be between 1 and {MaxFrameCount}.");
            }

            totalFrames += step.Frames;
            if (totalFrames > MaxInputTimelineFrames)
            {
                return Error("invalid_total_frames", $"The scenario must run at most {MaxInputTimelineFrames} frames.");
            }

            var buttons = ParseButtons(step.Buttons);
            if (!buttons.IsSuccess)
            {
                return new ToolError(buttons.Error!);
            }

            if (step.MemoryLength is < 1 or > MaxMemoryLength)
            {
                return Error("invalid_length", $"Memory observation length must be between 1 and {MaxMemoryLength} bytes.");
            }

            if (!string.IsNullOrWhiteSpace(step.MemoryAddress))
            {
                var parsedMemory = ParseAddress(step.MemoryAddress);
                if (!parsedMemory.IsSuccess)
                {
                    return new ToolError(parsedMemory.Error!);
                }
            }

            if (!string.IsNullOrWhiteSpace(step.TilemapAddress))
            {
                var parsedTilemap = ParseAddress(step.TilemapAddress);
                if (!parsedTilemap.IsSuccess)
                {
                    return new ToolError(parsedTilemap.Error!);
                }

                if (parsedTilemap.Value.Address is not (0x9800 or 0x9C00))
                {
                    return Error("invalid_tilemap_address", "Tilemap address must be 0x9800 or 0x9C00.");
                }
            }

            normalized.Add(new InputTimelineStep
            {
                Frames = step.Frames,
                Buttons = buttons.Value.Select(ToButtonName).ToArray(),
                ReadRegisters = step.ReadRegisters,
                ReadPpuState = step.ReadPpuState,
                DumpOam = step.DumpOam,
                Capture = step.Capture,
                DumpTilemap = step.DumpTilemap,
                TilemapAddress = step.TilemapAddress,
                MemoryAddress = step.MemoryAddress,
                MemoryLength = step.MemoryLength,
            });
        }

        return ToToolResult(session.RunInputTimeline(normalized));
    }

    [McpServerTool(Name = "dump_tilemap", ReadOnly = true, Destructive = false)]
    [Description("Dumps a 32x32 background/window tilemap from 0x9800 or 0x9C00.")]
    public static object DumpTilemap(IGameBoyDebugSession session, string address = "0x9800")
    {
        var parsed = ParseAddress(address);
        if (!parsed.IsSuccess)
        {
            return new ToolError(parsed.Error!);
        }

        if (parsed.Value.Address is not (0x9800 or 0x9C00))
        {
            return Error("invalid_tilemap_address", "Tilemap address must be 0x9800 or 0x9C00.");
        }

        return ToToolResult(session.DumpTilemap(parsed.Value.Address));
    }

    [McpServerTool(Name = "dump_tilemaps", ReadOnly = true, Destructive = false)]
    [Description("Atomically snapshots both 32x32 Game Boy tilemaps with hashes and optional CGB attribute-bank detail.")]
    public static object DumpTilemaps(IGameBoyDebugSession session, bool includeDetails = false) =>
        ToToolResult(session.DumpTilemaps(includeDetails));

    [McpServerTool(Name = "dump_tileset", ReadOnly = true, Destructive = false)]
    [Description("Dumps tile data from VRAM. Each tile is 16 bytes.")]
    public static object DumpTileset(IGameBoyDebugSession session, string address = "0x8000", int tileCount = 384)
    {
        if (tileCount is < 1 or > MaxTileCount)
        {
            return Error("invalid_tile_count", $"tileCount must be between 1 and {MaxTileCount}.");
        }

        var parsed = ParseAddress(address);
        if (!parsed.IsSuccess)
        {
            return new ToolError(parsed.Error!);
        }

        if (parsed.Value.Address < 0x8000 || parsed.Value.Address + tileCount * 16 > 0x9800)
        {
            return Error("invalid_tileset_range", "Tileset range must fit within 0x8000..0x97FF.");
        }

        return ToToolResult(session.DumpTileset(parsed.Value.Address, tileCount));
    }

    private static DebugResult<GameBoyAddress> ParseAddress(string address) => GameBoyAddress.Parse(address);

    private static DebugResult<WatchpointMode> ParseWatchpointMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return DebugResult<WatchpointMode>.Failure("invalid_watchpoint_mode", "Watchpoint mode must be read, write, or access.");
        }

        return mode.Trim().ToLowerInvariant() switch
        {
            "read" => DebugResult<WatchpointMode>.Success(WatchpointMode.Read),
            "write" => DebugResult<WatchpointMode>.Success(WatchpointMode.Write),
            "access" => DebugResult<WatchpointMode>.Success(WatchpointMode.Access),
            _ => DebugResult<WatchpointMode>.Failure("invalid_watchpoint_mode", "Watchpoint mode must be read, write, or access."),
        };
    }

    private static DebugResult<IReadOnlySet<VideoWriteKind>> ParseVideoWriteKinds(IReadOnlyList<string>? kinds)
    {
        if (kinds is null || kinds.Count == 0)
        {
            return DebugResult<IReadOnlySet<VideoWriteKind>>.Success(VideoWriteTracing.DefaultKinds);
        }

        var parsed = new HashSet<VideoWriteKind>();
        foreach (var raw in kinds)
        {
            switch (raw?.Trim().ToLowerInvariant())
            {
                case "vram":
                    parsed.Add(VideoWriteKind.Vram);
                    break;
                case "oam":
                    parsed.Add(VideoWriteKind.Oam);
                    break;
                case "ppu_register":
                case "ppu_registers":
                case "lcd_register":
                case "lcd_registers":
                    parsed.Add(VideoWriteKind.PpuRegister);
                    break;
                default:
                    return DebugResult<IReadOnlySet<VideoWriteKind>>.Failure(
                        "invalid_video_write_kind",
                        "Video-write kinds must be vram, oam, or ppu_register.");
            }
        }

        return DebugResult<IReadOnlySet<VideoWriteKind>>.Success(parsed);
    }

    private static DebugResult<IReadOnlySet<ushort>> ParsePpuRegisters(IReadOnlyList<string>? registers)
    {
        if (registers is null || registers.Count == 0)
        {
            return DebugResult<IReadOnlySet<ushort>>.Success(VideoWriteTracing.DefaultPpuRegisters);
        }

        var parsed = new HashSet<ushort>();
        foreach (var register in registers)
        {
            if (!VideoWriteTracing.TryParsePpuRegister(register, out var address))
            {
                return DebugResult<IReadOnlySet<ushort>>.Failure(
                    "invalid_ppu_register",
                    $"'{register}' is not a supported Game Boy LCD/PPU register name or address.");
            }

            parsed.Add(address);
        }

        return DebugResult<IReadOnlySet<ushort>>.Success(parsed);
    }

    private static DebugResult<bool> ValidateAddressRange(ushort address, int length, int maxLength)
    {
        if (length is < 1)
        {
            return DebugResult<bool>.Failure("invalid_range_length", "Range length must be positive.");
        }

        if (length > maxLength)
        {
            return DebugResult<bool>.Failure("invalid_range_length", $"Range length must be at most {maxLength} bytes.");
        }

        if (address + length > 0x10000)
        {
            return DebugResult<bool>.Failure("invalid_range", "Address range must fit within 0x0000..0xFFFF.");
        }

        return DebugResult<bool>.Success(true);
    }

    private static DebugResult<SafeArtifactPath> ResolveSafeArtifactPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return DebugResult<SafeArtifactPath>.Failure("invalid_artifact_path", "Artifact path is required.");
        }

        if (Path.IsPathRooted(path))
        {
            return DebugResult<SafeArtifactPath>.Failure("invalid_artifact_path", "Artifact path must be relative.");
        }

        if (!Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            return DebugResult<SafeArtifactPath>.Failure("invalid_artifact_path", "Screen capture artifact path must end in .png.");
        }

        var root = Path.GetFullPath(Environment.CurrentDirectory);
        var fullPath = Path.GetFullPath(path, root);
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && fullPath != root)
        {
            return DebugResult<SafeArtifactPath>.Failure("invalid_artifact_path", "Artifact path must stay within the current working directory.");
        }

        var relative = Path.GetRelativePath(root, fullPath).Replace(Path.DirectorySeparatorChar, '/');
        if (relative.StartsWith("..", StringComparison.Ordinal))
        {
            return DebugResult<SafeArtifactPath>.Failure("invalid_artifact_path", "Artifact path must stay within the current working directory.");
        }

        return DebugResult<SafeArtifactPath>.Success(new SafeArtifactPath(fullPath, relative));
    }

    private static ScreenCaptureMetadata BuildCaptureMetadata(IGameBoyDebugSession session)
    {
        var state = session.GetState();
        var registers = session.ReadRegisters();
        var ppu = session.ReadPpuState();

        return new ScreenCaptureMetadata(
            state.IsSuccess ? state.Value.Timeline : new TimelineCounters(0, 0),
            registers.IsSuccess ? registers.Value : null,
            ppu.IsSuccess ? ppu.Value : null,
            state.IsSuccess ? state.Value.Title : null,
            state.IsSuccess ? state.Value.Model : null);
    }

    private static DebugResult<IReadOnlyList<JoypadButton>> ParseButtons(IReadOnlyList<string>? buttons)
    {
        if (buttons is null)
        {
            return DebugResult<IReadOnlyList<JoypadButton>>.Failure("invalid_buttons", "buttons is required. Use an empty array to release every button.");
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

    private static object ToToolResult<T>(DebugResult<T> result) => result.IsSuccess ? result.Value! : new ToolError(result.Error!);

    private static ToolError Error(string code, string message) => new(new DebugError(code, message));

    private sealed record SafeArtifactPath(string FullPath, string RelativePath);

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
}

public sealed class ExecutionMemoryProbeInput
{
    public string Address { get; init; } = string.Empty;

    public int Length { get; init; }
}
