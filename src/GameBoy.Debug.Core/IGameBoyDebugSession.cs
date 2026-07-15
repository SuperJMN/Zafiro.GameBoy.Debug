namespace GameBoy.Debug.Core;

public interface IGameBoyDebugSession
{
    DebugResult<LoadRomResult> LoadRom(string path);

    DebugResult<SaveStateResult> SaveState(string path);

    DebugResult<LoadStateResult> LoadState(string path);

    DebugResult<ResetResult> Reset();

    DebugResult<StepInstructionResult> StepInstruction(int count);

    DebugResult<RunFrameResult> RunFrame(int count);

    DebugResult<JoypadStateResult> SetJoypad(IReadOnlyList<JoypadButton> pressedButtons);

    DebugResult<PressButtonsResult> PressButtons(IReadOnlyList<JoypadButton> pressedButtons, int frameCount);

    DebugResult<ContinueResult> ContinueUntilBreak(int maxInstructions);

    DebugResult<ContinueResult> StepOver(int maxInstructions);

    DebugResult<ContinueResult> StepOut(int maxInstructions);

    DebugResult<RunUntilConditionResult> RunUntilCondition(string condition, int maxInstructions, int maxFrames);

    DebugResult<BreakpointSetResult> SetBreakpoint(ushort address, string? condition);

    DebugResult<ClearBreakpointResult> ClearBreakpoint(string breakpointId);

    DebugResult<ListBreakpointsResult> ListBreakpoints();

    DebugResult<WatchpointSetResult> SetWatchpoint(ushort address, WatchpointMode mode);

    DebugResult<WatchpointSetResult> SetWatchpointRange(ushort address, int length, WatchpointMode mode);

    DebugResult<ClearWatchpointResult> ClearWatchpoint(string watchpointId);

    DebugResult<ListWatchpointsResult> ListWatchpoints();

    DebugResult<SessionStateResult> GetState();

    DebugResult<CpuRegisters> ReadRegisters();

    DebugResult<MemoryReadResult> ReadMemory(ushort address, int length);

    DebugResult<WriteMemoryResult> WriteMemory(ushort address, IReadOnlyList<byte> bytes);

    DebugResult<DisassembleResult> Disassemble(ushort address, int instructionCount);

    DebugResult<OamDumpResult> ReadOam();

    DebugResult<PpuStateResult> ReadPpuState();

    DebugResult<ScreenCaptureResult> CaptureScreen();

    DebugResult<LastWriterResult> FindLastWriter(ushort address);

    DebugResult<LastWritersResult> FindLastWriters(ushort address, int length);

    DebugResult<TraceUntilWriteResult> TraceUntilWrite(ushort address, int maxInstructions);

    DebugResult<TraceUntilWriteRangeResult> TraceUntilWriteRange(ushort address, int length, int maxInstructions);

    DebugResult<VideoWriteTraceResult> TraceVideoWrites(VideoWriteTraceRequest request);

    DebugResult<TilemapDumpResult> DumpTilemap(ushort address);

    DebugResult<TilemapSetDumpResult> DumpTilemaps(bool includeDetails);

    DebugResult<TilesetDumpResult> DumpTileset(ushort address, int tileCount);

    DebugResult<LoadSymbolsResult> LoadSymbols(string path);

    DebugResult<ResolveSymbolResult> ResolveSymbol(string name);

    DebugResult<ReadSymbolResult> ReadSymbol(string name, int? length);

    DebugResult<ScreenRegionResult> ReadScreenRegion(int x, int y, int width, int height, string format);

    DebugResult<ScreenObservationResult> ObserveScreen(int frameCount);

    DebugResult<ExecutionObservationResult> ObserveExecution(ExecutionObservationRequest request);

    DebugResult<InputTimelineResult> RunInputTimeline(IReadOnlyList<InputTimelineStep> steps);
}
