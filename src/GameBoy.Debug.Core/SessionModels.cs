using System.Text.Json.Serialization;

namespace GameBoy.Debug.Core;

public sealed record LoadRomResult(
    [property: JsonPropertyName("loaded")] bool Loaded,
    [property: JsonPropertyName("romTitle")] string RomTitle,
    [property: JsonPropertyName("model")] string Model);

public sealed record SaveStateResult(
    [property: JsonPropertyName("saved")] bool Saved,
    [property: JsonPropertyName("path")] string Path);

public sealed record LoadStateResult(
    [property: JsonPropertyName("loaded")] bool Loaded,
    [property: JsonPropertyName("path")] string Path);

public sealed record ResetResult([property: JsonPropertyName("reset")] bool Reset);

public sealed record TimelineCounters(
    [property: JsonPropertyName("frames")] ulong Frames,
    [property: JsonPropertyName("cycles")] ulong Cycles,
    [property: JsonPropertyName("instructions")] ulong Instructions = 0);

public sealed record StepInstructionResult(
    [property: JsonPropertyName("pcBefore")] string PcBefore,
    [property: JsonPropertyName("pcAfter")] string PcAfter,
    [property: JsonPropertyName("registers")] CpuRegisters Registers,
    [property: JsonPropertyName("disassembly")] string Disassembly,
    [property: JsonPropertyName("instructionsRun")] int InstructionsRun,
    [property: JsonPropertyName("timeline")] TimelineCounters Timeline)
{
    public StepInstructionResult(string pcBefore, string pcAfter, CpuRegisters registers, string disassembly)
        : this(pcBefore, pcAfter, registers, disassembly, 1, new TimelineCounters(0, 0))
    {
    }
}

public sealed record RunFrameResult(
    [property: JsonPropertyName("framesRun")] int FramesRun,
    [property: JsonPropertyName("registers")] CpuRegisters Registers,
    [property: JsonPropertyName("hitBreakpoint")] bool HitBreakpoint,
    [property: JsonPropertyName("timeline")] TimelineCounters Timeline)
{
    public RunFrameResult(int framesRun, CpuRegisters registers, bool hitBreakpoint)
        : this(framesRun, registers, hitBreakpoint, new TimelineCounters(0, 0))
    {
    }
}

public enum JoypadButton
{
    Right = 0,
    Left = 1,
    Up = 2,
    Down = 3,
    A = 4,
    B = 5,
    Select = 6,
    Start = 7,
}

public sealed record JoypadStateResult(
    [property: JsonPropertyName("right")] bool Right,
    [property: JsonPropertyName("left")] bool Left,
    [property: JsonPropertyName("up")] bool Up,
    [property: JsonPropertyName("down")] bool Down,
    [property: JsonPropertyName("a")] bool A,
    [property: JsonPropertyName("b")] bool B,
    [property: JsonPropertyName("select")] bool Select,
    [property: JsonPropertyName("start")] bool Start,
    [property: JsonPropertyName("pressed")] IReadOnlyList<string> Pressed);

public sealed record PressButtonsResult(
    [property: JsonPropertyName("framesRun")] int FramesRun,
    [property: JsonPropertyName("released")] JoypadStateResult Released,
    [property: JsonPropertyName("registers")] CpuRegisters Registers);

public sealed record ContinueResult(
    [property: JsonPropertyName("stopped")] bool Stopped,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("pc")] string Pc,
    [property: JsonPropertyName("registers")] CpuRegisters Registers,
    [property: JsonPropertyName("timeline")] TimelineCounters Timeline,
    [property: JsonPropertyName("instructionsRun")] ulong InstructionsRun)
{
    public ContinueResult(bool stopped, string reason, string pc, CpuRegisters registers)
        : this(stopped, reason, pc, registers, new TimelineCounters(0, 0), 0)
    {
    }
}

public sealed record BreakpointSetResult(
    [property: JsonPropertyName("breakpointId")] string BreakpointId,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("enabled")] bool Enabled);

public sealed record ClearBreakpointResult([property: JsonPropertyName("cleared")] bool Cleared);

public sealed record ListBreakpointsResult(
    [property: JsonPropertyName("breakpoints")] IReadOnlyList<BreakpointEntry> Breakpoints);

public sealed record BreakpointEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("condition")] string? Condition);

public sealed record WatchpointSetResult(
    [property: JsonPropertyName("watchpointId")] string WatchpointId,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("length")] int Length)
{
    public WatchpointSetResult(string watchpointId, string address, string mode, bool enabled)
        : this(watchpointId, address, mode, enabled, 1)
    {
    }
}

public sealed record ClearWatchpointResult([property: JsonPropertyName("cleared")] bool Cleared);

public sealed record ListWatchpointsResult(
    [property: JsonPropertyName("watchpoints")] IReadOnlyList<WatchpointEntry> Watchpoints);

public sealed record WatchpointEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("length")] int Length)
{
    public WatchpointEntry(string id, string address, string mode, bool enabled)
        : this(id, address, mode, enabled, 1)
    {
    }
}

public sealed record SessionStateResult(
    [property: JsonPropertyName("romLoaded")] bool RomLoaded,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("halted")] bool Halted,
    [property: JsonPropertyName("pc")] string? Pc,
    [property: JsonPropertyName("timeline")] TimelineCounters Timeline)
{
    public SessionStateResult(bool romLoaded, string? title, string? model, bool halted, string? pc)
        : this(romLoaded, title, model, halted, pc, new TimelineCounters(0, 0))
    {
    }
}

public sealed record CpuRegisters(
    [property: JsonPropertyName("af")] string Af,
    [property: JsonPropertyName("bc")] string Bc,
    [property: JsonPropertyName("de")] string De,
    [property: JsonPropertyName("hl")] string Hl,
    [property: JsonPropertyName("sp")] string Sp,
    [property: JsonPropertyName("pc")] string Pc,
    [property: JsonPropertyName("a")] string A,
    [property: JsonPropertyName("f")] string F,
    [property: JsonPropertyName("b")] string B,
    [property: JsonPropertyName("c")] string C,
    [property: JsonPropertyName("d")] string D,
    [property: JsonPropertyName("e")] string E,
    [property: JsonPropertyName("h")] string H,
    [property: JsonPropertyName("l")] string L,
    [property: JsonPropertyName("ime")] bool Ime,
    [property: JsonPropertyName("halted")] bool Halted);

public sealed record WriteMemoryResult(
    [property: JsonPropertyName("written")] bool Written,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("length")] int Length);

public sealed record DisassembleResult(
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("instructions")] IReadOnlyList<DisassembledInstruction> Instructions);

public sealed record DisassembledInstruction(
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("bytes")] string Bytes,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("symbol")] string? Symbol);

public sealed record OamDumpResult([property: JsonPropertyName("sprites")] IReadOnlyList<OamSprite> Sprites);

public sealed record PpuStateResult(
    [property: JsonPropertyName("lcdc")] string Lcdc,
    [property: JsonPropertyName("stat")] string Stat,
    [property: JsonPropertyName("mode")] int Mode,
    [property: JsonPropertyName("ly")] string Ly,
    [property: JsonPropertyName("lyc")] string Lyc,
    [property: JsonPropertyName("scy")] string Scy,
    [property: JsonPropertyName("scx")] string Scx,
    [property: JsonPropertyName("wy")] string Wy,
    [property: JsonPropertyName("wx")] string Wx,
    [property: JsonPropertyName("bgp")] string Bgp,
    [property: JsonPropertyName("obp0")] string Obp0,
    [property: JsonPropertyName("obp1")] string Obp1,
    [property: JsonPropertyName("vbk")] string Vbk,
    [property: JsonPropertyName("lcdEnabled")] bool LcdEnabled,
    [property: JsonPropertyName("spritesEnabled")] bool SpritesEnabled,
    [property: JsonPropertyName("windowEnabled")] bool WindowEnabled,
    [property: JsonPropertyName("backgroundEnabled")] bool BackgroundEnabled);

public sealed record OamSprite(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("tile")] string Tile,
    [property: JsonPropertyName("attributes")] string Attributes,
    [property: JsonPropertyName("visible")] bool Visible);

public sealed record ScreenCaptureResult(
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("mimeType")] string MimeType,
    [property: JsonPropertyName("data")] byte[] Data);

public sealed record ScreenCaptureMetadata(
    [property: JsonPropertyName("timeline")] TimelineCounters Timeline,
    [property: JsonPropertyName("registers")] CpuRegisters? Registers,
    [property: JsonPropertyName("ppuState")] PpuStateResult? PpuState,
    [property: JsonPropertyName("romTitle")] string? RomTitle,
    [property: JsonPropertyName("model")] string? Model);

public sealed record ScreenCaptureArtifactResult(
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("mimeType")] string MimeType,
    [property: JsonPropertyName("saved")] bool Saved,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("metadata")] ScreenCaptureMetadata? Metadata);

public sealed record LastWriterResult(
    [property: JsonPropertyName("found")] bool Found,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("pc")] string? Pc,
    [property: JsonPropertyName("value")] string? Value,
    [property: JsonPropertyName("writeCount")] ulong WriteCount);

public sealed record TraceUntilWriteResult(
    [property: JsonPropertyName("stopped")] bool Stopped,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("pc")] string? Pc,
    [property: JsonPropertyName("value")] string? Value,
    [property: JsonPropertyName("instructionsRun")] uint InstructionsRun,
    [property: JsonPropertyName("registers")] CpuRegisters Registers,
    [property: JsonPropertyName("timeline")] TimelineCounters Timeline)
{
    public TraceUntilWriteResult(bool stopped, string reason, string address, string? pc, string? value, uint instructionsRun, CpuRegisters registers)
        : this(stopped, reason, address, pc, value, instructionsRun, registers, new TimelineCounters(0, 0))
    {
    }
}

public sealed record LastWritersResult(
    [property: JsonPropertyName("writers")] IReadOnlyList<LastWriterResult> Writers);

public sealed record TraceUntilWriteRangeResult(
    [property: JsonPropertyName("stopped")] bool Stopped,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("length")] int Length,
    [property: JsonPropertyName("hitAddress")] string? HitAddress,
    [property: JsonPropertyName("pc")] string? Pc,
    [property: JsonPropertyName("value")] string? Value,
    [property: JsonPropertyName("instructionsRun")] uint InstructionsRun,
    [property: JsonPropertyName("registers")] CpuRegisters Registers,
    [property: JsonPropertyName("ppuState")] PpuStateResult PpuState,
    [property: JsonPropertyName("disassembly")] DisassembleResult Disassembly,
    [property: JsonPropertyName("timeline")] TimelineCounters Timeline);

public sealed record RunUntilConditionResult(
    [property: JsonPropertyName("stopped")] bool Stopped,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("pc")] string Pc,
    [property: JsonPropertyName("instructionsRun")] uint InstructionsRun,
    [property: JsonPropertyName("framesRun")] ulong FramesRun,
    [property: JsonPropertyName("registers")] CpuRegisters Registers,
    [property: JsonPropertyName("ppuState")] PpuStateResult PpuState,
    [property: JsonPropertyName("timeline")] TimelineCounters Timeline);

public sealed class InputTimelineStep
{
    [JsonPropertyName("frames")]
    public int Frames { get; init; }

    [JsonPropertyName("buttons")]
    public IReadOnlyList<string> Buttons { get; init; } = [];

    [JsonPropertyName("readRegisters")]
    public bool ReadRegisters { get; init; }

    [JsonPropertyName("readPpuState")]
    public bool ReadPpuState { get; init; }

    [JsonPropertyName("dumpOam")]
    public bool DumpOam { get; init; }

    [JsonPropertyName("capture")]
    public bool Capture { get; init; }

    [JsonPropertyName("dumpTilemap")]
    public bool DumpTilemap { get; init; }

    [JsonPropertyName("tilemapAddress")]
    public string? TilemapAddress { get; init; }

    [JsonPropertyName("memoryAddress")]
    public string? MemoryAddress { get; init; }

    [JsonPropertyName("memoryLength")]
    public int? MemoryLength { get; init; }
}

public sealed record InputTimelineStepResult(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("framesRun")] int FramesRun,
    [property: JsonPropertyName("totalFrames")] ulong TotalFrames,
    [property: JsonPropertyName("buttons")] IReadOnlyList<string> Buttons,
    [property: JsonPropertyName("registers")] CpuRegisters? Registers,
    [property: JsonPropertyName("ppuState")] PpuStateResult? PpuState,
    [property: JsonPropertyName("oam")] OamDumpResult? Oam,
    [property: JsonPropertyName("screenCapture")] ScreenCaptureResult? ScreenCapture,
    [property: JsonPropertyName("tilemap")] TilemapDumpResult? Tilemap,
    [property: JsonPropertyName("memory")] MemoryReadResult? Memory,
    [property: JsonPropertyName("timeline")] TimelineCounters? Timeline);

public sealed record InputTimelineResult(
    [property: JsonPropertyName("framesRun")] int FramesRun,
    [property: JsonPropertyName("released")] JoypadStateResult Released,
    [property: JsonPropertyName("steps")] IReadOnlyList<InputTimelineStepResult> Steps,
    [property: JsonPropertyName("timeline")] TimelineCounters Timeline);

public sealed record ScreenRegionResult(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("format")] string Format,
    [property: JsonPropertyName("pixelCount")] int PixelCount,
    [property: JsonPropertyName("values")] IReadOnlyList<int>? Values,
    [property: JsonPropertyName("histogram")] IReadOnlyDictionary<string, int> Histogram,
    [property: JsonPropertyName("rowHashes")] IReadOnlyList<string> RowHashes,
    [property: JsonPropertyName("screenToBgTile")] ScreenToBgTileMapping? ScreenToBgTile);

public sealed record ScreenToBgTileMapping(
    [property: JsonPropertyName("tileX")] int TileX,
    [property: JsonPropertyName("tileY")] int TileY,
    [property: JsonPropertyName("tilemapAddress")] string TilemapAddress);

public sealed record TilemapDumpResult(
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("rows")] IReadOnlyList<string> Rows);

public sealed record TilesetDumpResult(
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("tileCount")] int TileCount,
    [property: JsonPropertyName("tiles")] IReadOnlyList<TileDump> Tiles);

public sealed record TileDump(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("bytesHex")] string BytesHex);

public sealed record LoadSymbolsResult(
    [property: JsonPropertyName("loaded")] bool Loaded,
    [property: JsonPropertyName("symbolCount")] int SymbolCount);

public sealed record ResolveSymbolResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("bank")] int? Bank);

public sealed record ReadSymbolResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("bytes")] byte[] Bytes,
    [property: JsonPropertyName("bytesHex")] string BytesHex);
