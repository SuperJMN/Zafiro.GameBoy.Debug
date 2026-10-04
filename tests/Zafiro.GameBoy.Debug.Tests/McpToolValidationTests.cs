using Zafiro.GameBoy.Debug.Core;
using Zafiro.GameBoy.Debug.Mcp;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Reflection;
using System.Text.Json;

namespace Zafiro.GameBoy.Debug.Tests;

public sealed class McpToolValidationTests
{
    [Fact]
    public void Tool_surface_includes_corruption_observation_workflows()
    {
        var tools = typeof(GameBoyDebugTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("observe_screen", tools);
        Assert.Contains("observe_execution", tools);
        Assert.Contains("trace_video_writes", tools);
        Assert.Contains("dump_tilemaps", tools);
    }

    [Fact]
    public void Read_memory_rejects_non_positive_length_without_calling_session()
    {
        var session = new FakeDebugSession();

        var result = GameBoyDebugTools.ReadMemory(session, "0xC000", 0);

        var error = Assert.IsType<ToolError>(result);
        Assert.Equal("invalid_length", error.Error.Code);
        Assert.False(session.ReadMemoryCalled);
    }

    [Fact]
    public void Read_memory_returns_session_payload_for_valid_input()
    {
        var session = new FakeDebugSession
        {
            ReadMemoryResult = DebugResult<MemoryReadResult>.Success(
                new MemoryReadResult("0xC000", "2A", [0x2A], "*")),
        };

        var result = GameBoyDebugTools.ReadMemory(session, "0xC000", 1);

        var payload = Assert.IsType<MemoryReadResult>(result);
        Assert.True(session.ReadMemoryCalled);
        Assert.Equal((ushort)0xC000, session.LastReadAddress);
        Assert.Equal(1, session.LastReadLength);
        Assert.Equal("2A", payload.BytesHex);
    }

    [Fact]
    public void Set_joypad_rejects_unknown_buttons_without_calling_session()
    {
        var session = new FakeDebugSession();

        var result = GameBoyDebugTools.SetJoypad(session, ["right", "jump"]);

        var error = Assert.IsType<ToolError>(result);
        Assert.Equal("invalid_button", error.Error.Code);
        Assert.False(session.SetJoypadCalled);
    }

    [Fact]
    public void Set_joypad_sends_normalized_button_set_to_session()
    {
        var session = new FakeDebugSession
        {
            SetJoypadResult = DebugResult<JoypadStateResult>.Success(
                new JoypadStateResult(true, false, false, false, true, false, false, false, ["right", "a"])),
        };

        var result = GameBoyDebugTools.SetJoypad(session, ["RIGHT", "a", "right"]);

        var payload = Assert.IsType<JoypadStateResult>(result);
        Assert.True(session.SetJoypadCalled);
        Assert.Equal([JoypadButton.Right, JoypadButton.A], session.LastJoypadButtons);
        Assert.Equal(["right", "a"], payload.Pressed);
    }

    [Fact]
    public void Press_buttons_validates_frame_count_before_calling_session()
    {
        var session = new FakeDebugSession();

        var result = GameBoyDebugTools.PressButtons(session, ["a"], 0);

        var error = Assert.IsType<ToolError>(result);
        Assert.Equal("invalid_frame_count", error.Error.Code);
        Assert.False(session.PressButtonsCalled);
    }

    [Fact]
    public void Press_buttons_sends_normalized_button_set_and_frame_count_to_session()
    {
        var registers = new CpuRegisters(
            "0x0000", "0x0000", "0x0000", "0x0000", "0xFFFE", "0x0150",
            "0x00", "0x00", "0x00", "0x00", "0x00", "0x00", "0x00", "0x00",
            false, false);
        var session = new FakeDebugSession
        {
            PressButtonsResult = DebugResult<PressButtonsResult>.Success(
                new PressButtonsResult(5, new JoypadStateResult(false, false, false, false, false, false, false, false, []), registers)),
        };

        var result = GameBoyDebugTools.PressButtons(session, ["left", "b"], 5);

        var payload = Assert.IsType<PressButtonsResult>(result);
        Assert.True(session.PressButtonsCalled);
        Assert.Equal([JoypadButton.Left, JoypadButton.B], session.LastPressedButtons);
        Assert.Equal(5, session.LastPressFrameCount);
        Assert.Equal(5, payload.FramesRun);
    }

    [Fact]
    public void List_breakpoints_returns_session_payload()
    {
        var session = new FakeDebugSession
        {
            ListBreakpointsResult = DebugResult<ListBreakpointsResult>.Success(
                new ListBreakpointsResult(
                [
                    new BreakpointEntry("bp-1", "0x0150", true, null),
                    new BreakpointEntry("bp-2", "0xC000", true, "a == 1"),
                ])),
        };

        var result = GameBoyDebugTools.ListBreakpoints(session);

        var payload = Assert.IsType<ListBreakpointsResult>(result);
        Assert.True(session.ListBreakpointsCalled);
        Assert.Equal(2, payload.Breakpoints.Count);
        Assert.Equal("bp-2", payload.Breakpoints[1].Id);
        Assert.Equal("a == 1", payload.Breakpoints[1].Condition);
    }

    [Fact]
    public void Get_state_returns_session_payload()
    {
        var session = new FakeDebugSession
        {
            GetStateResult = DebugResult<SessionStateResult>.Success(
                new SessionStateResult(true, "MCPTEST", "DMG", false, "0x0100")),
        };

        var result = GameBoyDebugTools.GetState(session);

        var payload = Assert.IsType<SessionStateResult>(result);
        Assert.True(session.GetStateCalled);
        Assert.True(payload.RomLoaded);
        Assert.Equal("MCPTEST", payload.Title);
        Assert.Equal("DMG", payload.Model);
        Assert.Equal("0x0100", payload.Pc);
    }

    [Fact]
    public void Capture_screen_returns_inline_png_image_content()
    {
        var pngBytes = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' };
        var session = new FakeDebugSession
        {
            CaptureScreenResult = DebugResult<ScreenCaptureResult>.Success(
                new ScreenCaptureResult(160, 144, "image/png", pngBytes)),
        };

        var result = GameBoyDebugTools.CaptureScreen(session);

        var image = Assert.IsType<ImageContentBlock>(result);
        Assert.True(session.CaptureScreenCalled);
        Assert.Equal("image", image.Type);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(Convert.ToBase64String(pngBytes), System.Text.Encoding.UTF8.GetString(image.Data.Span));
        Assert.Equal(pngBytes, image.DecodedData.ToArray());
    }

    [Fact]
    public void Capture_screen_rejects_unsafe_artifact_path_without_calling_session()
    {
        var session = new FakeDebugSession();

        var result = GameBoyDebugTools.CaptureScreen(session, "../escape.png", includeMetadata: true);

        var error = Assert.IsType<ToolError>(result);
        Assert.Equal("invalid_artifact_path", error.Error.Code);
        Assert.False(session.CaptureScreenCalled);
    }

    [Fact]
    public void Capture_screen_can_save_png_artifact_with_metadata()
    {
        var previousDirectory = Environment.CurrentDirectory;
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"zafiro-gameboy-debug-artifact-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        Environment.CurrentDirectory = tempDirectory;
        var pngBytes = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' };
        var registers = RegistersAt("0x0100");
        var ppuState = PpuState();
        var session = new FakeDebugSession
        {
            CaptureScreenResult = DebugResult<ScreenCaptureResult>.Success(
                new ScreenCaptureResult(160, 144, "image/png", pngBytes)),
            GetStateResult = DebugResult<SessionStateResult>.Success(
                new SessionStateResult(true, "MCPTEST", "DMG", false, "0x0100", new TimelineCounters(3, 210672))),
            ReadRegistersResult = DebugResult<CpuRegisters>.Success(registers),
            ReadPpuStateResult = DebugResult<PpuStateResult>.Success(ppuState),
        };

        try
        {
            var result = GameBoyDebugTools.CaptureScreen(session, "artifacts/frame.png", includeMetadata: true);

            var payload = Assert.IsType<ScreenCaptureArtifactResult>(result);
            Assert.True(session.CaptureScreenCalled);
            Assert.Equal("artifacts/frame.png", payload.Path);
            Assert.Equal(160, payload.Width);
            Assert.True(File.Exists(Path.Combine(tempDirectory, "artifacts", "frame.png")));
            Assert.Equal(pngBytes, File.ReadAllBytes(Path.Combine(tempDirectory, "artifacts", "frame.png")));
            Assert.NotNull(payload.Metadata);
            Assert.Equal("MCPTEST", payload.Metadata.RomTitle);
            Assert.Equal(3UL, payload.Metadata.Timeline.Frames);
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Set_breakpoint_rejects_invalid_condition_without_calling_session()
    {
        var session = new FakeDebugSession();

        var result = GameBoyDebugTools.SetBreakpoint(session, "0x0150", "A = 1");

        var error = Assert.IsType<ToolError>(result);
        Assert.Equal("invalid_breakpoint_condition", error.Error.Code);
        Assert.False(session.SetBreakpointCalled);
    }

    [Fact]
    public void Save_state_rejects_blank_path_without_calling_session()
    {
        var session = new FakeDebugSession();

        var result = GameBoyDebugTools.SaveState(session, " ");

        var error = Assert.IsType<ToolError>(result);
        Assert.Equal("invalid_path", error.Error.Code);
        Assert.False(session.SaveStateCalled);
    }

    [Fact]
    public void Save_state_returns_session_payload_for_valid_path()
    {
        var session = new FakeDebugSession
        {
            SaveStateResult = DebugResult<SaveStateResult>.Success(new SaveStateResult(true, "state.s0")),
        };

        var result = GameBoyDebugTools.SaveState(session, "state.s0");

        var payload = Assert.IsType<SaveStateResult>(result);
        Assert.True(session.SaveStateCalled);
        Assert.Equal("state.s0", session.LastSaveStatePath);
        Assert.Equal("state.s0", payload.Path);
    }

    [Fact]
    public void Load_state_rejects_blank_path_without_calling_session()
    {
        var session = new FakeDebugSession();

        var result = GameBoyDebugTools.LoadState(session, "");

        var error = Assert.IsType<ToolError>(result);
        Assert.Equal("invalid_path", error.Error.Code);
        Assert.False(session.LoadStateCalled);
    }

    [Fact]
    public void Load_state_returns_session_payload_for_valid_path()
    {
        var session = new FakeDebugSession
        {
            LoadStateResult = DebugResult<LoadStateResult>.Success(new LoadStateResult(true, "state.s0")),
        };

        var result = GameBoyDebugTools.LoadState(session, "state.s0");

        var payload = Assert.IsType<LoadStateResult>(result);
        Assert.True(session.LoadStateCalled);
        Assert.Equal("state.s0", session.LastLoadStatePath);
        Assert.Equal("state.s0", payload.Path);
    }

    [Theory]
    [InlineData("read", WatchpointMode.Read)]
    [InlineData("ACCESS", WatchpointMode.Access)]
    public void Set_watchpoint_accepts_supported_modes(string mode, WatchpointMode expectedMode)
    {
        var session = new FakeDebugSession
        {
            SetWatchpointResult = DebugResult<WatchpointSetResult>.Success(
                new WatchpointSetResult("wp-1", "0xC000", mode.ToLowerInvariant(), true)),
        };

        var result = GameBoyDebugTools.SetWatchpoint(session, "0xC000", mode);

        var payload = Assert.IsType<WatchpointSetResult>(result);
        Assert.True(session.SetWatchpointCalled);
        Assert.Equal((ushort)0xC000, session.LastWatchpointAddress);
        Assert.Equal(expectedMode, session.LastWatchpointMode);
        Assert.Equal(mode.ToLowerInvariant(), payload.Mode);
    }

    [Fact]
    public void Set_watchpoint_rejects_invalid_mode_without_calling_session()
    {
        var session = new FakeDebugSession();

        var result = GameBoyDebugTools.SetWatchpoint(session, "0xC000", "execute");

        var error = Assert.IsType<ToolError>(result);
        Assert.Equal("invalid_watchpoint_mode", error.Error.Code);
        Assert.False(session.SetWatchpointCalled);
    }

    [Fact]
    public void Set_watchpoint_range_validates_length_before_calling_session()
    {
        var session = new FakeDebugSession();

        var result = GameBoyDebugTools.SetWatchpointRange(session, "0xC000", 0, "write");

        var error = Assert.IsType<ToolError>(result);
        Assert.Equal("invalid_range_length", error.Error.Code);
        Assert.False(session.SetWatchpointRangeCalled);
    }

    [Fact]
    public void Set_watchpoint_range_passes_range_and_mode_to_session()
    {
        var session = new FakeDebugSession
        {
            SetWatchpointRangeResult = DebugResult<WatchpointSetResult>.Success(
                new WatchpointSetResult("wp-1", "0xC000", "write", true, 32)),
        };

        var result = GameBoyDebugTools.SetWatchpointRange(session, "0xC000", 32, "write");

        var payload = Assert.IsType<WatchpointSetResult>(result);
        Assert.True(session.SetWatchpointRangeCalled);
        Assert.Equal((ushort)0xC000, session.LastWatchpointAddress);
        Assert.Equal(32, session.LastWatchpointLength);
        Assert.Equal(WatchpointMode.Write, session.LastWatchpointMode);
        Assert.Equal(32, payload.Length);
    }

    [Fact]
    public void Run_until_condition_rejects_invalid_condition_without_calling_session()
    {
        var session = new FakeDebugSession();

        var result = GameBoyDebugTools.RunUntilCondition(session, "LY => 0x90", 100, 2);

        var error = Assert.IsType<ToolError>(result);
        Assert.Equal("invalid_condition", error.Error.Code);
        Assert.False(session.RunUntilConditionCalled);
    }

    [Fact]
    public void Trace_until_write_range_validates_address_range_before_calling_session()
    {
        var session = new FakeDebugSession();

        var result = GameBoyDebugTools.TraceUntilWriteRange(session, "0xFFF0", 32, 1000);

        var error = Assert.IsType<ToolError>(result);
        Assert.Equal("invalid_range", error.Error.Code);
        Assert.False(session.TraceUntilWriteRangeCalled);
    }

    [Fact]
    public void Read_screen_region_validates_bounds_before_calling_session()
    {
        var session = new FakeDebugSession();

        var result = GameBoyDebugTools.ReadScreenRegion(session, 159, 0, 2, 1);

        var error = Assert.IsType<ToolError>(result);
        Assert.Equal("invalid_screen_region", error.Error.Code);
        Assert.False(session.ReadScreenRegionCalled);
    }

    [Fact]
    public void Read_screen_region_forwards_full_frame_rgb_raw_format()
    {
        var session = new FakeDebugSession();

        var result = GameBoyDebugTools.ReadScreenRegion(session, 0, 0, 160, 144, "rgb24_raw");

        Assert.True(session.ReadScreenRegionCalled);
        Assert.Equal("rgb24_raw", session.LastScreenRegionFormat);
    }

    [Fact]
    public void Observe_execution_rejects_io_memory_probes_before_calling_session()
    {
        var session = new FakeDebugSession();

        var result = GameBoyDebugTools.ObserveExecution(
            session,
            memoryProbes: [new ExecutionMemoryProbeInput { Address = "0xFF40", Length = 1 }]);

        var error = Assert.IsType<ToolError>(result);
        Assert.Equal("invalid_memory_probe", error.Error.Code);
        Assert.False(session.ObserveExecutionCalled);
    }

    [Fact]
    public void Trace_video_writes_normalizes_kinds_registers_and_buttons()
    {
        var session = new FakeDebugSession();

        var result = GameBoyDebugTools.TraceVideoWrites(
            session,
            frameCount: 2,
            maxEvents: 10,
            kinds: ["vram", "ppu_register"],
            ppuRegisters: ["LCDC", "$FF43"],
            buttons: ["RIGHT"]);

        Assert.True(session.TraceVideoWritesCalled);
        Assert.Equal(2, session.LastVideoWriteTraceRequest!.FrameCount);
        Assert.Equal([VideoWriteKind.Vram, VideoWriteKind.PpuRegister], session.LastVideoWriteTraceRequest.Kinds.Order());
        Assert.Equal([(ushort)0xFF40, (ushort)0xFF43], session.LastVideoWriteTraceRequest.PpuRegisters.Order());
        Assert.Equal([JoypadButton.Right], session.LastVideoWriteTraceRequest.Buttons);
    }

    [Fact]
    public void Run_input_timeline_passes_normalized_steps_to_session_atomically()
    {
        var session = new FakeDebugSession
        {
            RunInputTimelineResult = DebugResult<InputTimelineResult>.Success(
                new InputTimelineResult(
                    4,
                    new JoypadStateResult(false, false, false, false, false, false, false, false, []),
                    [
                        new InputTimelineStepResult(0, 4, 4, ["right", "a"], RegistersAt("0x0102"), PpuState(), null, null, null, null, null),
                    ],
                    new TimelineCounters(4, 280896))),
        };

        var result = GameBoyDebugTools.RunInputTimeline(
            session,
            [
                new InputTimelineStep { Frames = 4, Buttons = ["right", "a"], ReadRegisters = true, ReadPpuState = true },
            ]);

        var payload = Assert.IsType<InputTimelineResult>(result);
        Assert.Equal(4, payload.FramesRun);
        Assert.True(session.RunInputTimelineCalled);
        Assert.Equal(["right", "a"], session.LastInputTimelineSteps[0].Buttons);
        var step = Assert.Single(payload.Steps);
        Assert.NotNull(step.Registers);
        Assert.NotNull(step.PpuState);
    }

    [Fact]
    public void Watchpoint_result_serializes_mode_as_lowercase_string()
    {
        var result = new WatchpointSetResult("wp-1", "0xC000", "read", true);

        var json = JsonSerializer.Serialize(result);

        Assert.Contains("\"mode\":\"read\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Step_over_validates_instruction_limit_before_calling_session()
    {
        var session = new FakeDebugSession();

        var result = GameBoyDebugTools.StepOver(session, 0);

        var error = Assert.IsType<ToolError>(result);
        Assert.Equal("invalid_max_instructions", error.Error.Code);
        Assert.False(session.StepOverCalled);
    }

    private static CpuRegisters RegistersAt(string pc) =>
        new(
            "0x0000",
            "0x0000",
            "0x0000",
            "0x0000",
            "0xFFFE",
            pc,
            "0x00",
            "0x00",
            "0x00",
            "0x00",
            "0x00",
            "0x00",
            "0x00",
            "0x00",
            false,
            false);

    private static PpuStateResult PpuState() =>
        new(
            "0x91",
            "0x85",
            1,
            "0x90",
            "0x00",
            "0x00",
            "0x00",
            "0x00",
            "0x00",
            "0xFC",
            "0xFF",
            "0xFF",
            "0x00",
            true,
            false,
            false,
            true);

    private sealed class FakeDebugSession : IGameBoyDebugSession
    {
        public bool ReadMemoryCalled { get; private set; }

        public ushort LastReadAddress { get; private set; }

        public int LastReadLength { get; private set; }

        public bool SetJoypadCalled { get; private set; }

        public IReadOnlyList<JoypadButton> LastJoypadButtons { get; private set; } = [];

        public bool PressButtonsCalled { get; private set; }

        public IReadOnlyList<JoypadButton> LastPressedButtons { get; private set; } = [];

        public int LastPressFrameCount { get; private set; }

        public bool ListBreakpointsCalled { get; private set; }

        public bool GetStateCalled { get; private set; }

        public bool CaptureScreenCalled { get; private set; }

        public bool SetBreakpointCalled { get; private set; }

        public bool SaveStateCalled { get; private set; }

        public string? LastSaveStatePath { get; private set; }

        public bool LoadStateCalled { get; private set; }

        public string? LastLoadStatePath { get; private set; }

        public bool SetWatchpointCalled { get; private set; }

        public bool SetWatchpointRangeCalled { get; private set; }

        public ushort LastWatchpointAddress { get; private set; }

        public int LastWatchpointLength { get; private set; }

        public WatchpointMode LastWatchpointMode { get; private set; }

        public bool StepOverCalled { get; private set; }

        public bool RunUntilConditionCalled { get; private set; }

        public bool TraceUntilWriteRangeCalled { get; private set; }

        public bool ReadScreenRegionCalled { get; private set; }

        public bool RunInputTimelineCalled { get; private set; }

        public bool TraceVideoWritesCalled { get; private set; }

        public bool ObserveExecutionCalled { get; private set; }

        public string? LastScreenRegionFormat { get; private set; }

        public VideoWriteTraceRequest? LastVideoWriteTraceRequest { get; private set; }

        public IReadOnlyList<InputTimelineStep> LastInputTimelineSteps { get; private set; } = [];

        public DebugResult<MemoryReadResult> ReadMemoryResult { get; init; } =
            DebugResult<MemoryReadResult>.Failure("not_configured", "The fake session was not configured.");

        public DebugResult<JoypadStateResult> SetJoypadResult { get; init; } =
            DebugResult<JoypadStateResult>.Failure("not_configured", "The fake session was not configured.");

        public DebugResult<PressButtonsResult> PressButtonsResult { get; init; } =
            DebugResult<PressButtonsResult>.Failure("not_configured", "The fake session was not configured.");

        public DebugResult<ListBreakpointsResult> ListBreakpointsResult { get; init; } =
            DebugResult<ListBreakpointsResult>.Failure("not_configured", "The fake session was not configured.");

        public DebugResult<SessionStateResult> GetStateResult { get; init; } =
            DebugResult<SessionStateResult>.Failure("not_configured", "The fake session was not configured.");

        public DebugResult<ScreenCaptureResult> CaptureScreenResult { get; init; } =
            DebugResult<ScreenCaptureResult>.Failure("not_configured", "The fake session was not configured.");

        public DebugResult<SaveStateResult> SaveStateResult { get; init; } =
            DebugResult<SaveStateResult>.Failure("not_configured", "The fake session was not configured.");

        public DebugResult<LoadStateResult> LoadStateResult { get; init; } =
            DebugResult<LoadStateResult>.Failure("not_configured", "The fake session was not configured.");

        public DebugResult<WatchpointSetResult> SetWatchpointResult { get; init; } =
            DebugResult<WatchpointSetResult>.Failure("not_configured", "The fake session was not configured.");

        public DebugResult<WatchpointSetResult> SetWatchpointRangeResult { get; init; } =
            DebugResult<WatchpointSetResult>.Failure("not_configured", "The fake session was not configured.");

        public DebugResult<CpuRegisters> ReadRegistersResult { get; init; } =
            DebugResult<CpuRegisters>.Failure("not_configured", "The fake session was not configured.");

        public DebugResult<PpuStateResult> ReadPpuStateResult { get; init; } =
            DebugResult<PpuStateResult>.Failure("not_configured", "The fake session was not configured.");

        public DebugResult<InputTimelineResult> RunInputTimelineResult { get; init; } =
            DebugResult<InputTimelineResult>.Failure("not_configured", "The fake session was not configured.");

        public DebugResult<LoadRomResult> LoadRom(string path) => throw new NotSupportedException();

        public DebugResult<SaveStateResult> SaveState(string path)
        {
            SaveStateCalled = true;
            LastSaveStatePath = path;
            return SaveStateResult;
        }

        public DebugResult<LoadStateResult> LoadState(string path)
        {
            LoadStateCalled = true;
            LastLoadStatePath = path;
            return LoadStateResult;
        }

        public DebugResult<ResetResult> Reset() => throw new NotSupportedException();

        public DebugResult<StepInstructionResult> StepInstruction(int count) => throw new NotSupportedException();

        public DebugResult<RunFrameResult> RunFrame(int count) => throw new NotSupportedException();

        public DebugResult<JoypadStateResult> SetJoypad(IReadOnlyList<JoypadButton> pressedButtons)
        {
            SetJoypadCalled = true;
            LastJoypadButtons = pressedButtons.ToArray();
            return SetJoypadResult;
        }

        public DebugResult<PressButtonsResult> PressButtons(IReadOnlyList<JoypadButton> pressedButtons, int frameCount)
        {
            PressButtonsCalled = true;
            LastPressedButtons = pressedButtons.ToArray();
            LastPressFrameCount = frameCount;
            return PressButtonsResult;
        }

        public DebugResult<ContinueResult> ContinueUntilBreak(int maxInstructions) => throw new NotSupportedException();

        public DebugResult<ContinueResult> StepOver(int maxInstructions)
        {
            StepOverCalled = true;
            throw new NotSupportedException();
        }

        public DebugResult<ContinueResult> StepOut(int maxInstructions) => throw new NotSupportedException();

        public DebugResult<RunUntilConditionResult> RunUntilCondition(string condition, int maxInstructions, int maxFrames)
        {
            RunUntilConditionCalled = true;
            throw new NotSupportedException();
        }

        public DebugResult<BreakpointSetResult> SetBreakpoint(ushort address, string? condition)
        {
            SetBreakpointCalled = true;
            return DebugResult<BreakpointSetResult>.Failure("not_configured", "The fake session was not configured.");
        }

        public DebugResult<ClearBreakpointResult> ClearBreakpoint(string breakpointId) => throw new NotSupportedException();

        public DebugResult<ListBreakpointsResult> ListBreakpoints()
        {
            ListBreakpointsCalled = true;
            return ListBreakpointsResult;
        }

        public DebugResult<WatchpointSetResult> SetWatchpoint(ushort address, WatchpointMode mode)
        {
            SetWatchpointCalled = true;
            LastWatchpointAddress = address;
            LastWatchpointMode = mode;
            return SetWatchpointResult;
        }

        public DebugResult<WatchpointSetResult> SetWatchpointRange(ushort address, int length, WatchpointMode mode)
        {
            SetWatchpointRangeCalled = true;
            LastWatchpointAddress = address;
            LastWatchpointLength = length;
            LastWatchpointMode = mode;
            return SetWatchpointRangeResult;
        }

        public DebugResult<ClearWatchpointResult> ClearWatchpoint(string watchpointId) => throw new NotSupportedException();

        public DebugResult<ListWatchpointsResult> ListWatchpoints() => throw new NotSupportedException();

        public DebugResult<SessionStateResult> GetState()
        {
            GetStateCalled = true;
            return GetStateResult;
        }

        public DebugResult<CpuRegisters> ReadRegisters() => ReadRegistersResult;

        public DebugResult<MemoryReadResult> ReadMemory(ushort address, int length)
        {
            ReadMemoryCalled = true;
            LastReadAddress = address;
            LastReadLength = length;
            return ReadMemoryResult;
        }

        public DebugResult<WriteMemoryResult> WriteMemory(ushort address, IReadOnlyList<byte> bytes) => throw new NotSupportedException();

        public DebugResult<DisassembleResult> Disassemble(ushort address, int instructionCount) => throw new NotSupportedException();

        public DebugResult<OamDumpResult> ReadOam() => throw new NotSupportedException();

        public DebugResult<PpuStateResult> ReadPpuState() => ReadPpuStateResult;

        public DebugResult<ScreenCaptureResult> CaptureScreen()
        {
            CaptureScreenCalled = true;
            return CaptureScreenResult;
        }

        public DebugResult<LastWriterResult> FindLastWriter(ushort address) => throw new NotSupportedException();

        public DebugResult<LastWritersResult> FindLastWriters(ushort address, int length) => throw new NotSupportedException();

        public DebugResult<TraceUntilWriteResult> TraceUntilWrite(ushort address, int maxInstructions) => throw new NotSupportedException();

        public DebugResult<TraceUntilWriteRangeResult> TraceUntilWriteRange(ushort address, int length, int maxInstructions)
        {
            TraceUntilWriteRangeCalled = true;
            throw new NotSupportedException();
        }

        public DebugResult<VideoWriteTraceResult> TraceVideoWrites(VideoWriteTraceRequest request)
        {
            TraceVideoWritesCalled = true;
            LastVideoWriteTraceRequest = request;
            return DebugResult<VideoWriteTraceResult>.Failure("not_configured", "The fake session was not configured.");
        }

        public DebugResult<TilemapDumpResult> DumpTilemap(ushort address) => throw new NotSupportedException();

        public DebugResult<TilemapSetDumpResult> DumpTilemaps(bool includeDetails) => throw new NotSupportedException();

        public DebugResult<TilesetDumpResult> DumpTileset(ushort address, int tileCount) => throw new NotSupportedException();

        public DebugResult<LoadSymbolsResult> LoadSymbols(string path) => throw new NotSupportedException();

        public DebugResult<ResolveSymbolResult> ResolveSymbol(string name) => throw new NotSupportedException();

        public DebugResult<ReadSymbolResult> ReadSymbol(string name, int? length) => throw new NotSupportedException();

        public DebugResult<ScreenRegionResult> ReadScreenRegion(int x, int y, int width, int height, string format)
        {
            ReadScreenRegionCalled = true;
            LastScreenRegionFormat = format;
            return DebugResult<ScreenRegionResult>.Failure("not_configured", "The fake session was not configured.");
        }

        public DebugResult<ScreenObservationResult> ObserveScreen(int frameCount) => throw new NotSupportedException();

        public DebugResult<ExecutionObservationResult> ObserveExecution(ExecutionObservationRequest request)
        {
            ObserveExecutionCalled = true;
            return DebugResult<ExecutionObservationResult>.Failure("not_configured", "The fake session was not configured.");
        }

        public DebugResult<InputTimelineResult> RunInputTimeline(IReadOnlyList<InputTimelineStep> steps)
        {
            RunInputTimelineCalled = true;
            LastInputTimelineSteps = steps.ToArray();
            return RunInputTimelineResult;
        }
    }
}
