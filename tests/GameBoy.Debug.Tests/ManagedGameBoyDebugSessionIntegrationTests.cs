using System;
using System.IO;
using System.Linq;
using GameBoy.Debug.Core;
using GameBoy.Debug.Emulator;

namespace GameBoy.Debug.Tests;

public sealed class ManagedGameBoyDebugSessionIntegrationTests
{
    [Fact]
    public void State_reports_no_rom_before_loading()
    {
        using var session = new ManagedGameBoyDebugSession();

        var state = session.GetState();

        Assert.True(state.IsSuccess, state.Error?.Message);
        Assert.False(state.Value.RomLoaded);
        Assert.Null(state.Value.Title);
    }

    [Fact]
    public void Reads_before_loading_fail_cleanly()
    {
        using var session = new ManagedGameBoyDebugSession();

        var registers = session.ReadRegisters();

        Assert.False(registers.IsSuccess);
        Assert.Equal("no_rom_loaded", registers.Error?.Code);
    }

    [Fact]
    public void Managed_backend_loads_and_controls_minimal_rom()
    {
        var romPath = CreateTestFilePath("managed-mcp", ".gb");
        var symPath = Path.ChangeExtension(romPath, ".sym");
        CreateMinimalRom(romPath);
        File.WriteAllLines(symPath, ["C000 Player.X"]);

        try
        {
            using var session = new ManagedGameBoyDebugSession();

            var loaded = session.LoadRom(romPath);
            Assert.True(loaded.IsSuccess, loaded.Error?.Message);
            Assert.Equal("MCPTEST", loaded.Value.RomTitle);
            Assert.Equal("DMG", loaded.Value.Model);

            var state = session.GetState();
            Assert.True(state.Value.RomLoaded);
            Assert.Equal("0x0100", state.Value.Pc);

            var registers = session.ReadRegisters();
            Assert.True(registers.IsSuccess, registers.Error?.Message);
            Assert.Equal("0x0100", registers.Value.Pc);

            var memory = session.ReadMemory(0x0100, 3);
            Assert.True(memory.IsSuccess, memory.Error?.Message);
            Assert.Equal("3E 2A EA", memory.Value.BytesHex);

            var disassembly = session.Disassemble(0x0100, 2);
            Assert.True(disassembly.IsSuccess, disassembly.Error?.Message);
            Assert.Contains(disassembly.Value.Instructions, i => i.Text.Contains("LD A", StringComparison.OrdinalIgnoreCase));

            var step = session.StepInstruction(1);
            Assert.True(step.IsSuccess, step.Error?.Message);
            Assert.Equal("0x0100", step.Value.PcBefore);
            Assert.Equal("0x0102", step.Value.PcAfter);

            var breakpoint = session.SetBreakpoint(0x0102, null);
            Assert.True(breakpoint.IsSuccess, breakpoint.Error?.Message);
            var listed = session.ListBreakpoints();
            Assert.Single(listed.Value.Breakpoints);

            var reset = session.Reset();
            Assert.True(reset.IsSuccess, reset.Error?.Message);
            Assert.Single(session.ListBreakpoints().Value.Breakpoints); // breakpoints survive reset

            var continued = session.ContinueUntilBreak(16);
            Assert.True(continued.IsSuccess, continued.Error?.Message);
            Assert.Equal("breakpoint", continued.Value.Reason);
            Assert.Equal("0x0102", continued.Value.Pc);

            var trace = session.TraceUntilWrite(0xC000, 16);
            Assert.True(trace.IsSuccess, trace.Error?.Message);
            Assert.Equal("write", trace.Value.Reason);
            Assert.Equal("0x2A", trace.Value.Value);

            var lastWriter = session.FindLastWriter(0xC000);
            Assert.True(lastWriter.Value.Found);
            Assert.Equal("0x2A", lastWriter.Value.Value);

            var symbols = session.LoadSymbols(symPath);
            Assert.True(symbols.IsSuccess, symbols.Error?.Message);
            var readSymbol = session.ReadSymbol("Player.X", 1);
            Assert.True(readSymbol.IsSuccess, readSymbol.Error?.Message);
            Assert.Equal("2A", readSymbol.Value.BytesHex);

            var written = session.WriteMemory(0xC000, [0x5A]);
            Assert.True(written.IsSuccess, written.Error?.Message);
            Assert.Equal("5A", session.ReadMemory(0xC000, 1).Value.BytesHex);

            var oam = session.ReadOam();
            Assert.Equal(40, oam.Value.Sprites.Count);

            var ppu = session.ReadPpuState();
            Assert.True(ppu.IsSuccess, ppu.Error?.Message);
            Assert.StartsWith("0x", ppu.Value.Lcdc, StringComparison.Ordinal);

            var tilemap = session.DumpTilemap(0x9800);
            Assert.Equal(32, tilemap.Value.Rows.Count);

            var tileset = session.DumpTileset(0x8000, 2);
            Assert.Equal(2, tileset.Value.Tiles.Count);

            var screen = session.CaptureScreen();
            Assert.True(screen.IsSuccess, screen.Error?.Message);
            Assert.Equal(160, screen.Value.Width);
            Assert.Equal(144, screen.Value.Height);
            Assert.Equal("image/png", screen.Value.MimeType);
            Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], screen.Value.Data[..4]);
        }
        finally
        {
            File.Delete(romPath);
            File.Delete(symPath);
        }
    }

    [Fact]
    public void Timeline_counters_progress_with_frames_and_reset_with_rom()
    {
        var romPath = CreateTestFilePath("managed-timeline", ".gb");
        CreateMinimalRom(romPath);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);

            var initial = session.GetState();
            Assert.True(initial.IsSuccess, initial.Error?.Message);
            Assert.Equal(0UL, initial.Value.Timeline.Frames);
            Assert.Equal(0UL, initial.Value.Timeline.Cycles);

            var run = session.RunFrame(2);
            Assert.True(run.IsSuccess, run.Error?.Message);
            Assert.Equal(2, run.Value.FramesRun);
            Assert.Equal(2UL, run.Value.Timeline.Frames);
            Assert.True(run.Value.Timeline.Cycles >= 140448);

            var step = session.StepInstruction(1);
            Assert.True(step.IsSuccess, step.Error?.Message);
            Assert.Equal(1, step.Value.InstructionsRun);
            Assert.True(step.Value.Timeline.Cycles > run.Value.Timeline.Cycles);

            Assert.True(session.Reset().IsSuccess);
            var resetState = session.GetState();
            Assert.Equal(0UL, resetState.Value.Timeline.Frames);
            Assert.Equal(0UL, resetState.Value.Timeline.Cycles);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Run_until_condition_stops_on_memory_predicate_and_reports_ppu_state()
    {
        var romPath = CreateTestFilePath("managed-condition", ".gb");
        CreateMinimalRom(romPath);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);

            var result = session.RunUntilCondition("[0xC000] == 0x2A", maxInstructions: 16, maxFrames: 1);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("condition", result.Value.Reason);
            Assert.Equal("0x0105", result.Value.Pc);
            Assert.True(result.Value.InstructionsRun > 0);
            Assert.StartsWith("0x", result.Value.PpuState.Ly, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Trace_until_write_range_reports_concrete_hit_address_and_last_writers()
    {
        var romPath = CreateTestFilePath("managed-range-trace", ".gb");
        CreateMinimalRom(romPath);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);

            var trace = session.TraceUntilWriteRange(0xBFFF, length: 4, maxInstructions: 16);

            Assert.True(trace.IsSuccess, trace.Error?.Message);
            Assert.Equal("write", trace.Value.Reason);
            Assert.Equal("0xBFFF", trace.Value.Address);
            Assert.Equal("0xC000", trace.Value.HitAddress);
            Assert.Equal("0x2A", trace.Value.Value);
            Assert.NotEmpty(trace.Value.Disassembly.Instructions);

            var lastWriters = session.FindLastWriters(0xBFFF, 4);
            Assert.True(lastWriters.IsSuccess, lastWriters.Error?.Message);
            var writer = Assert.Single(lastWriters.Value.Writers, item => item.Found);
            Assert.Equal("0xC000", writer.Address);
            Assert.Equal("0x2A", writer.Value);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Watchpoint_range_stops_continue_when_write_falls_inside_range()
    {
        var romPath = CreateTestFilePath("managed-range-watchpoint", ".gb");
        CreateMinimalRom(romPath);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);

            var watchpoint = session.SetWatchpointRange(0xBFFF, length: 4, WatchpointMode.Write);
            Assert.True(watchpoint.IsSuccess, watchpoint.Error?.Message);
            Assert.Equal(4, watchpoint.Value.Length);

            var continued = session.ContinueUntilBreak(16);

            Assert.True(continued.IsSuccess, continued.Error?.Message);
            Assert.Equal("watchpoint", continued.Value.Reason);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Read_screen_region_returns_raw_small_region_and_summary_for_large_region()
    {
        var romPath = CreateTestFilePath("managed-screen-region", ".gb");
        CreateMinimalRom(romPath);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);
            Assert.True(session.RunFrame(1).IsSuccess);

            var small = session.ReadScreenRegion(0, 0, 2, 2, "dmg_shades");
            Assert.True(small.IsSuccess, small.Error?.Message);
            Assert.Equal(4, small.Value.Values?.Count);
            Assert.NotEmpty(small.Value.Histogram);

            var large = session.ReadScreenRegion(0, 0, 160, 32, "dmg_shades");
            Assert.True(large.IsSuccess, large.Error?.Message);
            Assert.Null(large.Value.Values);
            Assert.Equal(32, large.Value.RowHashes.Count);
            Assert.Equal(5120, large.Value.PixelCount);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Input_timeline_runs_steps_collects_observations_and_releases_buttons()
    {
        var romPath = CreateTestFilePath("managed-input-timeline", ".gb");
        CreateMinimalRom(romPath);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);

            var result = session.RunInputTimeline(
            [
                new InputTimelineStep { Frames = 1, Buttons = ["right"], ReadRegisters = true },
                new InputTimelineStep { Frames = 1, Buttons = ["right", "a"], ReadPpuState = true, DumpOam = true },
            ]);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(2, result.Value.FramesRun);
            Assert.Empty(result.Value.Released.Pressed);
            Assert.Equal(2, result.Value.Steps.Count);
            Assert.Equal(["right"], result.Value.Steps[0].Buttons);
            Assert.NotNull(result.Value.Steps[0].Registers);
            Assert.NotNull(result.Value.Steps[1].PpuState);
            Assert.NotNull(result.Value.Steps[1].Oam);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Savestate_roundtrip_restores_registers_and_memory()
    {
        var romPath = CreateTestFilePath("managed-savestate", ".gb");
        var statePath = Path.ChangeExtension(romPath, ".s0");
        CreateMinimalRom(romPath);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);
            session.WriteMemory(0xC000, Enumerable.Repeat((byte)0xA5, 16).ToArray());
            Assert.True(session.RunFrame(1).IsSuccess);

            var registersBefore = session.ReadRegisters();
            var memoryBefore = session.ReadMemory(0xC000, 16);
            var timelineBefore = session.GetState().Value.Timeline;

            var saved = session.SaveState(statePath);
            Assert.True(saved.IsSuccess, saved.Error?.Message);
            Assert.True(File.Exists(statePath));

            session.WriteMemory(0xC000, Enumerable.Repeat((byte)0x5A, 16).ToArray());
            session.StepInstruction(1);
            Assert.NotEqual(memoryBefore.Value.BytesHex, session.ReadMemory(0xC000, 16).Value.BytesHex);

            var loadedState = session.LoadState(statePath);
            Assert.True(loadedState.IsSuccess, loadedState.Error?.Message);

            Assert.Equal(registersBefore.Value, session.ReadRegisters().Value);
            Assert.Equal(memoryBefore.Value.BytesHex, session.ReadMemory(0xC000, 16).Value.BytesHex);
            Assert.Equal(timelineBefore, session.GetState().Value.Timeline);
        }
        finally
        {
            File.Delete(romPath);
            File.Delete(statePath);
        }
    }

    [Fact]
    public void Write_watchpoint_stops_continue_until_break()
    {
        var romPath = CreateTestFilePath("managed-watchpoint", ".gb");
        CreateMinimalRom(romPath);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);

            var watchpoint = session.SetWatchpoint(0xC000, WatchpointMode.Write);
            Assert.True(watchpoint.IsSuccess, watchpoint.Error?.Message);

            var continued = session.ContinueUntilBreak(16);

            Assert.True(continued.IsSuccess, continued.Error?.Message);
            Assert.Equal("watchpoint", continued.Value.Reason);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Lists_and_clears_watchpoints()
    {
        using var session = new ManagedGameBoyDebugSession();

        var watchpoint = session.SetWatchpoint(0xC000, WatchpointMode.Access);
        Assert.True(watchpoint.IsSuccess, watchpoint.Error?.Message);

        var listed = session.ListWatchpoints();
        Assert.True(listed.IsSuccess, listed.Error?.Message);
        var entry = Assert.Single(listed.Value.Watchpoints);
        Assert.Equal("wp-1", entry.Id);
        Assert.Equal("0xC000", entry.Address);
        Assert.Equal("access", entry.Mode);
        Assert.True(entry.Enabled);

        var cleared = session.ClearWatchpoint(watchpoint.Value.WatchpointId);
        Assert.True(cleared.IsSuccess, cleared.Error?.Message);
        Assert.True(cleared.Value.Cleared);
        Assert.Empty(session.ListWatchpoints().Value.Watchpoints);
    }

    [Fact]
    public void Step_over_call_stops_at_instruction_after_call()
    {
        var romPath = CreateTestFilePath("managed-step-over", ".gb");
        CreateCallRom(romPath);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);

            var result = session.StepOver(100);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("step_over", result.Value.Reason);
            Assert.Equal("0x0103", result.Value.Pc);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Step_out_from_subroutine_returns_to_caller()
    {
        var romPath = CreateTestFilePath("managed-step-out", ".gb");
        CreateCallRom(romPath);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);
            var stepInto = session.StepInstruction(1);
            Assert.True(stepInto.IsSuccess, stepInto.Error?.Message);
            Assert.Equal("0x0150", stepInto.Value.PcAfter);
            var spInsideSubroutine = stepInto.Value.Registers.Sp;

            var result = session.StepOut(100);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("step_out", result.Value.Reason);
            Assert.Equal("0x0103", result.Value.Pc);
            Assert.True(ParseWord(result.Value.Registers.Sp) > ParseWord(spInsideSubroutine));
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    private static string CreateTestFilePath(string prefix, string extension)
    {
        var directory = Path.Combine(Path.GetTempPath(), "gameboy-mcp-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{prefix}-{Guid.NewGuid():N}{extension}");
    }

    private static void CreateMinimalRom(string path)
    {
        var rom = Enumerable.Repeat((byte)0x00, 0x8000).ToArray();
        rom[0x100] = 0x3E; // LD A, 0x2A
        rom[0x101] = 0x2A;
        rom[0x102] = 0xEA; // LD [0xC000], A
        rom[0x103] = 0x00;
        rom[0x104] = 0xC0;
        rom[0x105] = 0x18; // JR -2
        rom[0x106] = 0xFE;
        var title = "MCPTEST"u8.ToArray();
        Array.Copy(title, 0, rom, 0x134, title.Length);
        rom[0x147] = 0x00;
        rom[0x148] = 0x00;
        rom[0x149] = 0x00;
        File.WriteAllBytes(path, rom);
    }

    private static void CreateCallRom(string path)
    {
        var rom = Enumerable.Repeat((byte)0x00, 0x8000).ToArray();
        rom[0x100] = 0xCD; // CALL 0x0150
        rom[0x101] = 0x50;
        rom[0x102] = 0x01;
        rom[0x103] = 0x18; // JR -2
        rom[0x104] = 0xFE;
        rom[0x150] = 0x00; // NOP
        rom[0x151] = 0x00; // NOP
        rom[0x152] = 0xC9; // RET
        var title = "MCPCALL"u8.ToArray();
        Array.Copy(title, 0, rom, 0x134, title.Length);
        rom[0x147] = 0x00;
        rom[0x148] = 0x00;
        rom[0x149] = 0x00;
        File.WriteAllBytes(path, rom);
    }

    private static ushort ParseWord(string value) =>
        (ushort)Convert.ToInt32(value.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase), 16);
}
