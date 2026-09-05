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

    [Fact]
    public void Observe_screen_returns_one_exact_rgb_sample_per_completed_frame()
    {
        var romPath = CreateTestFilePath("managed-observe-screen", ".gb");
        CreateMinimalRom(romPath);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);

            var observation = session.ObserveScreen(2);

            Assert.True(observation.IsSuccess, observation.Error?.Message);
            Assert.Equal(2, observation.Value.FramesRun);
            Assert.Equal(2, observation.Value.Samples.Count);
            Assert.All(observation.Value.Samples, sample =>
                Assert.StartsWith("sha256:", sample.Hash, StringComparison.Ordinal));
            Assert.Equal(observation.Value.Timeline.Frames, observation.Value.Samples[^1].TotalFrame);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Observe_screen_stops_before_running_when_the_initial_pc_has_a_breakpoint()
    {
        var romPath = CreateTestFilePath("managed-screen-breakpoint", ".gb");
        CreateMinimalRom(romPath);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);
            Assert.True(session.SetBreakpoint(0x0100, null).IsSuccess);

            var observation = session.ObserveScreen(2);

            Assert.True(observation.IsSuccess, observation.Error?.Message);
            Assert.Equal(0, observation.Value.FramesRun);
            Assert.Empty(observation.Value.Samples);
            Assert.True(observation.Value.HitBreakpoint);
            Assert.Equal(0UL, observation.Value.Timeline.Frames);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Read_screen_region_raw_formats_return_the_complete_exact_frame()
    {
        var romPath = CreateTestFilePath("managed-screen-raw", ".gb");
        CreateMinimalRom(romPath);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);
            Assert.True(session.RunFrame(1).IsSuccess);

            var shades = session.ReadScreenRegion(0, 0, 160, 144, "dmg_shades_raw");
            var rgb = session.ReadScreenRegion(0, 0, 160, 144, "rgb24_raw");

            Assert.True(shades.IsSuccess, shades.Error?.Message);
            Assert.Equal(160 * 144, shades.Value.Values?.Count);
            Assert.Equal("dmg_shades_raw", shades.Value.Format);
            Assert.True(rgb.IsSuccess, rgb.Error?.Message);
            Assert.Equal(160 * 144, rgb.Value.Values?.Count);
            Assert.Equal("rgb24_raw", rgb.Value.Format);
            Assert.All(rgb.Value.Values!, value => Assert.InRange(value, 0, 0xFFFFFF));
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Ppu_state_reports_selected_cgb_vram_bank_and_authoritative_timing()
    {
        var romPath = CreateTestFilePath("managed-ppu-state", ".gbc");
        CreateMinimalRom(romPath, cgb: true);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);
            Assert.True(session.WriteMemory(0xFF4F, [0x01]).IsSuccess);
            Assert.True(session.WriteMemory(0xFF40, [0x90]).IsSuccess);
            Assert.True(session.StepInstruction(1).IsSuccess);

            var ppu = session.ReadPpuState();

            Assert.True(ppu.IsSuccess, ppu.Error?.Message);
            Assert.Equal("0xFF", ppu.Value.Vbk);
            Assert.True(ppu.Value.Dot.HasValue);
            Assert.InRange(ppu.Value.Dot.Value, 0, 455);
            Assert.Equal(Convert.ToByte(ppu.Value.Ly[2..], 16), ppu.Value.Scanline);
            Assert.Equal(ppu.Value.Mode == 1, ppu.Value.VBlank);
            Assert.Equal(ppu.Value.Timeline, session.GetState().Value.Timeline);
            Assert.Equal("0x9800", ppu.Value.Control.BackgroundTilemapAddress);
            Assert.True(ppu.Value.Control.BackgroundWindowEnabled);
            Assert.False(ppu.Value.Control.BackgroundWindowPriorityEnabled);
            Assert.Equal(ppu.Value.Mode, ppu.Value.Status.Mode);
            Assert.True(ppu.Value.TimingAuthoritative);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Dump_tilemaps_snapshots_both_maps_and_cgb_attribute_bank_without_changing_vbk()
    {
        var romPath = CreateTestFilePath("managed-tilemaps", ".gbc");
        CreateMinimalRom(romPath, cgb: true);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);
            Assert.True(session.WriteMemory(0xFF4F, [0x00]).IsSuccess);
            Assert.True(session.WriteMemory(0x9800, [0x11]).IsSuccess);
            Assert.True(session.WriteMemory(0x9C00, [0x22]).IsSuccess);
            Assert.True(session.WriteMemory(0xFF4F, [0x01]).IsSuccess);
            Assert.True(session.WriteMemory(0x9800, [0xA1]).IsSuccess);
            Assert.True(session.WriteMemory(0x9C00, [0xA2]).IsSuccess);

            var dump = session.DumpTilemaps(includeDetails: true);

            Assert.True(dump.IsSuccess, dump.Error?.Message);
            Assert.Equal("CGB", dump.Value.Model);
            Assert.True(dump.Value.DetailsIncluded);
            Assert.Collection(
                dump.Value.Tilemaps,
                map =>
                {
                    Assert.Equal("0x9800", map.Address);
                    Assert.StartsWith("11 ", map.TileRows![0], StringComparison.Ordinal);
                    Assert.StartsWith("A1 ", map.AttributeRows![0], StringComparison.Ordinal);
                },
                map =>
                {
                    Assert.Equal("0x9C00", map.Address);
                    Assert.StartsWith("22 ", map.TileRows![0], StringComparison.Ordinal);
                    Assert.StartsWith("A2 ", map.AttributeRows![0], StringComparison.Ordinal);
                });
            Assert.All(dump.Value.Tilemaps, map => Assert.StartsWith("sha256:", map.TileHash, StringComparison.Ordinal));
            Assert.Equal("0xFF", session.ReadPpuState().Value.Vbk);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Trace_video_writes_correlates_vram_oam_and_ppu_register_writes_without_stopping_at_the_cap()
    {
        var romPath = CreateTestFilePath("managed-video-trace", ".gb");
        CreateVideoWriteRom(romPath);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);

            var trace = session.TraceVideoWrites(new VideoWriteTraceRequest(
                FrameCount: 1,
                MaxEvents: 3,
                Kinds: new HashSet<VideoWriteKind>
                {
                    VideoWriteKind.Vram,
                    VideoWriteKind.Oam,
                    VideoWriteKind.PpuRegister,
                },
                PpuRegisters: VideoWriteTracing.DefaultPpuRegisters,
                Buttons: []));

            Assert.True(trace.IsSuccess, trace.Error?.Message);
            Assert.Equal(1, trace.Value.FramesRun);
            Assert.Equal(3, trace.Value.EventCount);
            Assert.True(trace.Value.EventsObserved > trace.Value.EventCount);
            Assert.True(trace.Value.Truncated);
            Assert.Equal(
                [VideoWriteKind.Vram, VideoWriteKind.Oam, VideoWriteKind.PpuRegister],
                trace.Value.Events.Select(evt => evt.Kind));
            Assert.Equal(["0x8000", "0xFE00", "0xFF40"], trace.Value.Events.Select(evt => evt.Address));
            Assert.Equal(["0x0102", "0x0107", "0x0111"], trace.Value.Events.Select(evt => evt.Pc));
            Assert.All(trace.Value.Events, evt =>
            {
                Assert.StartsWith("0x", evt.Pc, StringComparison.Ordinal);
                Assert.NotNull(evt.Before);
                Assert.NotNull(evt.After);
            });
            Assert.True(trace.Value.Events[1].InstructionCounter > trace.Value.Events[0].InstructionCounter);
            Assert.Empty(trace.Value.Released.Pressed);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Trace_video_writes_stops_at_a_mid_frame_breakpoint_before_later_corruption()
    {
        var romPath = CreateTestFilePath("managed-video-breakpoint", ".gb");
        CreateVideoWriteRom(romPath);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);
            Assert.True(session.SetBreakpoint(0x010F, null).IsSuccess);

            var trace = session.TraceVideoWrites(new VideoWriteTraceRequest(
                1,
                100,
                VideoWriteTracing.DefaultKinds,
                VideoWriteTracing.DefaultPpuRegisters,
                []));

            Assert.True(trace.IsSuccess, trace.Error?.Message);
            Assert.Equal(0, trace.Value.FramesRun);
            Assert.True(trace.Value.HitBreakpoint);
            Assert.Equal("breakpoint", trace.Value.StopReason);
            Assert.Equal(["0x8000", "0xFE00"], trace.Value.Events.Select(evt => evt.Address));
            Assert.DoesNotContain(trace.Value.Events, evt => evt.Address == "0xFF40");
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Observe_execution_correlates_exact_frames_memory_ppu_tilemaps_and_bounded_video_writes()
    {
        var romPath = CreateTestFilePath("managed-execution-observation", ".gb");
        CreateVideoWriteRom(romPath);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);

            var observation = session.ObserveExecution(new ExecutionObservationRequest(
                FrameCount: 2,
                Buttons: [JoypadButton.Right],
                MemoryProbes: [new MemoryProbe(0x8000, 1), new MemoryProbe(0xC000, 1)],
                IncludePpuState: true,
                TraceVideoWrites: true,
                MaxVideoEvents: 1,
                VideoKinds: VideoWriteTracing.DefaultKinds,
                PpuRegisters: VideoWriteTracing.DefaultPpuRegisters));

            Assert.True(observation.IsSuccess, observation.Error?.Message);
            Assert.Equal(2, observation.Value.FramesRun);
            Assert.Equal(["right"], observation.Value.HeldButtons);
            Assert.Equal(2, observation.Value.Frames.Count);
            Assert.All(observation.Value.Frames, frame =>
            {
                Assert.StartsWith("sha256:", frame.Screen.Hash, StringComparison.Ordinal);
                Assert.Equal(2, frame.Memory.Count);
                Assert.NotNull(frame.PpuState);
                Assert.Equal("12", frame.Memory[0].BytesHex);
                Assert.Equal("56", frame.Memory[1].BytesHex);
            });
            Assert.Single(observation.Value.VideoEvents);
            Assert.True(observation.Value.VideoEventsObserved > observation.Value.VideoEventCount);
            Assert.True(observation.Value.VideoTraceTruncated);
            Assert.Equal(2, observation.Value.InitialTilemaps.Tilemaps.Count);
            Assert.Equal(2, observation.Value.FinalTilemaps.Tilemaps.Count);
            Assert.Empty(observation.Value.Released.Pressed);
            Assert.Equal(ExecutionObserver.AppliedLimits, observation.Value.Limits);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Observe_execution_reports_a_mid_frame_breakpoint_before_sampling_an_incomplete_frame()
    {
        var romPath = CreateTestFilePath("managed-execution-breakpoint", ".gb");
        CreateVideoWriteRom(romPath);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);
            Assert.True(session.SetBreakpoint(0x010F, null).IsSuccess);

            var observation = session.ObserveExecution(new ExecutionObservationRequest(
                1,
                [],
                [],
                IncludePpuState: false,
                TraceVideoWrites: true,
                MaxVideoEvents: 100,
                VideoKinds: VideoWriteTracing.DefaultKinds,
                PpuRegisters: VideoWriteTracing.DefaultPpuRegisters));

            Assert.True(observation.IsSuccess, observation.Error?.Message);
            Assert.Equal(0, observation.Value.FramesRun);
            Assert.Empty(observation.Value.Frames);
            Assert.True(observation.Value.HitBreakpoint);
            Assert.Equal("breakpoint", observation.Value.StopReason);
            Assert.Equal(["0x8000", "0xFE00"], observation.Value.VideoEvents.Select(evt => evt.Address));
            Assert.Empty(observation.Value.Released.Pressed);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Run_frame_stops_at_an_interrupt_vector_before_the_handler_executes()
    {
        var romPath = CreateTestFilePath("managed-interrupt-breakpoint", ".gb");
        CreateInterruptRom(romPath);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);
            Assert.True(session.SetBreakpoint(0x0040, null).IsSuccess);

            var run = session.RunFrame(2);

            Assert.True(run.IsSuccess, run.Error?.Message);
            Assert.Equal(0, run.Value.FramesRun);
            Assert.True(run.Value.HitBreakpoint);
            Assert.Equal("0x0040", run.Value.Registers.Pc);
            Assert.Equal("00", session.ReadMemory(0xC000, 1).Value.BytesHex);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Trace_video_writes_observes_every_oam_dma_destination_write()
    {
        var romPath = CreateTestFilePath("managed-oam-dma-trace", ".gb");
        CreateOamDmaRom(romPath);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);
            Assert.True(session.WriteMemory(0xC000, Enumerable.Range(0, 0xA0).Select(value => (byte)value).ToArray()).IsSuccess);

            var trace = session.TraceVideoWrites(new VideoWriteTraceRequest(
                1,
                200,
                new HashSet<VideoWriteKind> { VideoWriteKind.Oam },
                VideoWriteTracing.DefaultPpuRegisters,
                []));

            Assert.True(trace.IsSuccess, trace.Error?.Message);
            Assert.Equal(0xA0, trace.Value.EventCount);
            Assert.Equal(0xA0, trace.Value.EventsObserved);
            Assert.False(trace.Value.Truncated);
            Assert.Equal("0xFE00", trace.Value.Events[0].Address);
            Assert.Equal("0x00", trace.Value.Events[0].Value);
            Assert.Equal("0xFE9F", trace.Value.Events[^1].Address);
            Assert.Equal("0x9F", trace.Value.Events[^1].Value);
            Assert.Equal(
                Enumerable.Range(0, 0xA0).Select(value => (byte)value),
                session.ReadMemory(0xFE00, 0xA0).Value.Bytes);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Run_frame_without_breakpoints_completes_100_frames()
    {
        var romPath = CreateTestFilePath("managed-frame-fast-path", ".gb");
        CreateMinimalRom(romPath);

        try
        {
            using var session = new ManagedGameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);

            var run = session.RunFrame(100);

            Assert.True(run.IsSuccess, run.Error?.Message);
            Assert.Equal(100, run.Value.FramesRun);
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

    private static void CreateMinimalRom(string path, bool cgb = false)
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
        rom[0x143] = cgb ? (byte)0x80 : (byte)0x00;
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

    private static void CreateVideoWriteRom(string path)
    {
        var rom = Enumerable.Repeat((byte)0x00, 0x8000).ToArray();
        byte[] program =
        [
            0x3E, 0x12,       // LD A, $12
            0xEA, 0x00, 0x80, // LD [$8000], A
            0x3E, 0x34,       // LD A, $34
            0xEA, 0x00, 0xFE, // LD [$FE00], A
            0x3E, 0x56,       // LD A, $56
            0xEA, 0x00, 0xC0, // LD [$C000], A
            0x3E, 0x91,       // LD A, $91
            0xE0, 0x40,       // LDH [$FF40], A
            0x18, 0xEB,       // JR $0100
        ];
        Array.Copy(program, 0, rom, 0x100, program.Length);
        var title = "MCPVIDEO"u8.ToArray();
        Array.Copy(title, 0, rom, 0x134, title.Length);
        rom[0x147] = 0x00;
        rom[0x148] = 0x00;
        rom[0x149] = 0x00;
        File.WriteAllBytes(path, rom);
    }

    private static void CreateInterruptRom(string path)
    {
        var rom = Enumerable.Repeat((byte)0x00, 0x8000).ToArray();
        byte[] program =
        [
            0x3E, 0x01,       // LD A, $01
            0xEA, 0xFF, 0xFF, // LD [$FFFF], A - enable VBlank interrupt
            0xFB,             // EI
            0x00,             // NOP - allow EI to take effect
            0x18, 0xFD,       // JR $0106
        ];
        byte[] handler =
        [
            0x3E, 0x99,       // LD A, $99
            0xEA, 0x00, 0xC0, // LD [$C000], A
            0xD9,             // RETI
        ];
        Array.Copy(program, 0, rom, 0x100, program.Length);
        Array.Copy(handler, 0, rom, 0x0040, handler.Length);
        var title = "MCPIRQ"u8.ToArray();
        Array.Copy(title, 0, rom, 0x134, title.Length);
        File.WriteAllBytes(path, rom);
    }

    private static void CreateOamDmaRom(string path)
    {
        var rom = Enumerable.Repeat((byte)0x00, 0x8000).ToArray();
        byte[] program =
        [
            0x3E, 0xC0, // LD A, $C0
            0xE0, 0x46, // LDH [$FF46], A
            0x18, 0xFE, // JR $0104
        ];
        Array.Copy(program, 0, rom, 0x100, program.Length);
        var title = "MCPDMA"u8.ToArray();
        Array.Copy(title, 0, rom, 0x134, title.Length);
        File.WriteAllBytes(path, rom);
    }

    private static ushort ParseWord(string value) =>
        (ushort)Convert.ToInt32(value.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase), 16);
}
