using GameBoy.Debug.Core;
using GameBoy.Debug.SameBoy;

namespace GameBoy.Debug.Tests;

public sealed class SameBoyDebugSessionIntegrationTests
{
    private const string RealRomPath = "/home/jmn/.copilot/session-state/6dea9c9b-9db5-4e33-bbf9-2ab327d68574/files/rom/Super Mario Land 2 - 6 Golden Coins (USA, Europe).gb";

    [Fact]
    public void Set_breakpoint_rejects_invalid_condition_before_native_execution()
    {
        using var session = new SameBoyDebugSession();

        var result = session.SetBreakpoint(0x0150, "A = 1");

        Assert.False(result.IsSuccess);
        Assert.Equal("invalid_breakpoint_condition", result.Error?.Code);
    }

    [Fact]
    public void SameBoy_backend_loads_and_controls_minimal_rom()
    {
        if (!NativeBridgeExists())
        {
            return;
        }

        var romPath = CreateTestFilePath("gameboy-debug-mcp", ".gb");
        var symPath = Path.ChangeExtension(romPath, ".sym");
        CreateMinimalRom(romPath);
        File.WriteAllLines(symPath, ["C000 Player.X"]);

        try
        {
            using var session = new SameBoyDebugSession();

            var loaded = session.LoadRom(romPath);
            Assert.True(loaded.IsSuccess, loaded.Error?.Message);
            Assert.True(loaded.Value.Loaded);
            Assert.Equal("DMG", loaded.Value.Model);

            var loadedState = session.GetState();
            Assert.True(loadedState.IsSuccess, loadedState.Error?.Message);
            Assert.True(loadedState.Value.RomLoaded);
            Assert.Equal("MCPTEST", loadedState.Value.Title);
            Assert.Equal("DMG", loadedState.Value.Model);
            Assert.Equal("0x0100", loadedState.Value.Pc);
            Assert.False(loadedState.Value.Halted);

            var reset = session.Reset();
            Assert.True(reset.IsSuccess, reset.Error?.Message);
            Assert.True(reset.Value.Reset);

            var registers = session.ReadRegisters();
            Assert.True(registers.IsSuccess, registers.Error?.Message);
            Assert.Equal("0x0100", registers.Value.Pc);

            var memory = session.ReadMemory(0x0100, 3);
            Assert.True(memory.IsSuccess, memory.Error?.Message);
            Assert.Equal("3E 2A EA", memory.Value.BytesHex);

            var disassembly = session.Disassemble(0x0100, 2);
            Assert.True(disassembly.IsSuccess, disassembly.Error?.Message);
            Assert.Contains(disassembly.Value.Instructions, instruction => instruction.Text.Contains("LD A", StringComparison.OrdinalIgnoreCase));

            var step = session.StepInstruction(1);
            Assert.True(step.IsSuccess, step.Error?.Message);
            Assert.Equal("0x0100", step.Value.PcBefore);
            Assert.Equal("0x0102", step.Value.PcAfter);

            var breakpoint = session.SetBreakpoint(0x0102, null);
            Assert.True(breakpoint.IsSuccess, breakpoint.Error?.Message);
            var listedBreakpoints = session.ListBreakpoints();
            Assert.True(listedBreakpoints.IsSuccess, listedBreakpoints.Error?.Message);
            var listedBreakpoint = Assert.Single(listedBreakpoints.Value.Breakpoints);
            Assert.Equal(breakpoint.Value.BreakpointId, listedBreakpoint.Id);
            Assert.Equal("0x0102", listedBreakpoint.Address);
            Assert.True(listedBreakpoint.Enabled);
            Assert.Null(listedBreakpoint.Condition);
            var continued = session.ContinueUntilBreak(16);
            Assert.True(continued.IsSuccess, continued.Error?.Message);
            Assert.Equal("breakpoint", continued.Value.Reason);

            var trace = session.TraceUntilWrite(0xC000, 16);
            Assert.True(trace.IsSuccess, trace.Error?.Message);
            Assert.Equal("write", trace.Value.Reason);
            Assert.Equal("0x2A", trace.Value.Value);

            var lastWriter = session.FindLastWriter(0xC000);
            Assert.True(lastWriter.IsSuccess, lastWriter.Error?.Message);
            Assert.True(lastWriter.Value.Found);
            Assert.Equal("0x2A", lastWriter.Value.Value);

            var written = session.WriteMemory(0xC000, [0x2A]);
            Assert.True(written.IsSuccess, written.Error?.Message);
            var symbols = session.LoadSymbols(symPath);
            Assert.True(symbols.IsSuccess, symbols.Error?.Message);
            var readSymbol = session.ReadSymbol("Player.X", 1);
            Assert.True(readSymbol.IsSuccess, readSymbol.Error?.Message);
            Assert.Equal("2A", readSymbol.Value.BytesHex);

            var oam = session.ReadOam();
            Assert.True(oam.IsSuccess, oam.Error?.Message);
            Assert.Equal(40, oam.Value.Sprites.Count);

            var ppu = session.ReadPpuState();
            Assert.True(ppu.IsSuccess, ppu.Error?.Message);
            Assert.StartsWith("0x", ppu.Value.Lcdc, StringComparison.Ordinal);

            var tilemap = session.DumpTilemap(0x9800);
            Assert.True(tilemap.IsSuccess, tilemap.Error?.Message);
            Assert.Equal(32, tilemap.Value.Rows.Count);

            var tileset = session.DumpTileset(0x8000, 2);
            Assert.True(tileset.IsSuccess, tileset.Error?.Message);
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
    public void State_reports_no_rom_without_native_dependency()
    {
        using var session = new SameBoyDebugSession();

        var state = session.GetState();

        Assert.True(state.IsSuccess, state.Error?.Message);
        Assert.False(state.Value.RomLoaded);
        Assert.Null(state.Value.Title);
        Assert.Null(state.Value.Model);
        Assert.False(state.Value.Halted);
        Assert.Null(state.Value.Pc);
    }

    [Fact]
    public void Post_boot_lcd_state_allows_ly_wait_loops_to_advance()
    {
        if (!NativeBridgeExists())
        {
            return;
        }

        var romPath = CreateTestFilePath("gameboy-debug-mcp-ly-wait", ".gb");
        CreateLyWaitRom(romPath);

        try
        {
            using var session = new SameBoyDebugSession();

            var loaded = session.LoadRom(romPath);
            Assert.True(loaded.IsSuccess, loaded.Error?.Message);

            var ppu = session.ReadPpuState();
            Assert.True(ppu.IsSuccess, ppu.Error?.Message);
            Assert.True(ppu.Value.LcdEnabled);

            var trace = session.TraceUntilWrite(0xC000, 200_000);
            Assert.True(trace.IsSuccess, trace.Error?.Message);
            Assert.Equal("write", trace.Value.Reason);
            Assert.Equal("0x2A", trace.Value.Value);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Observe_screen_rejects_breakpoints_that_the_native_frame_api_cannot_honor()
    {
        if (!NativeBridgeExists())
        {
            return;
        }

        var romPath = CreateTestFilePath("sameboy-observe-breakpoint", ".gb");
        CreateMinimalRom(romPath);

        try
        {
            using var session = new SameBoyDebugSession();
            Assert.True(session.LoadRom(romPath).IsSuccess);
            Assert.True(session.SetBreakpoint(0x0100, null).IsSuccess);

            var observation = session.ObserveScreen(1);

            Assert.False(observation.IsSuccess);
            Assert.Equal("screen_observation_breakpoints_not_supported", observation.Error?.Code);
            Assert.Equal("0x0100", session.ReadRegisters().Value.Pc);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Set_joypad_drives_sameboy_joyp_reads()
    {
        if (!NativeBridgeExists())
        {
            return;
        }

        var romPath = CreateTestFilePath("gameboy-debug-mcp-joypad", ".gb");
        CreateJoypadReadRom(romPath);

        try
        {
            using var session = new SameBoyDebugSession();

            var loaded = session.LoadRom(romPath);
            Assert.True(loaded.IsSuccess, loaded.Error?.Message);

            var joypad = session.SetJoypad([JoypadButton.A, JoypadButton.Right]);
            Assert.True(joypad.IsSuccess, joypad.Error?.Message);
            Assert.Equal(["right", "a"], joypad.Value.Pressed);

            var frame = session.RunFrame(1);
            Assert.True(frame.IsSuccess, frame.Error?.Message);

            var memory = session.ReadMemory(0xC000, 2);
            Assert.True(memory.IsSuccess, memory.Error?.Message);
            Assert.True((memory.Value.Bytes[0] & 0x0F) == 0x0E, memory.Value.BytesHex);
            Assert.True((memory.Value.Bytes[1] & 0x0F) == 0x0E, memory.Value.BytesHex);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    [Fact]
    public void Savestate_roundtrip_restores_registers_and_memory_from_real_rom()
    {
        if (!NativeBridgeExists() || !File.Exists(RealRomPath))
        {
            return;
        }

        var statePath = CreateTestFilePath("sameboy-savestate", ".s0");

        try
        {
            using var session = new SameBoyDebugSession();

            var loaded = session.LoadRom(RealRomPath);
            Assert.True(loaded.IsSuccess, loaded.Error?.Message);

            var frames = session.RunFrame(3);
            Assert.True(frames.IsSuccess, frames.Error?.Message);

            var registersBefore = session.ReadRegisters();
            Assert.True(registersBefore.IsSuccess, registersBefore.Error?.Message);
            var memoryBefore = session.ReadMemory(0xC000, 16);
            Assert.True(memoryBefore.IsSuccess, memoryBefore.Error?.Message);

            var saved = session.SaveState(statePath);
            Assert.True(saved.IsSuccess, saved.Error?.Message);
            Assert.True(saved.Value.Saved);
            Assert.Equal(statePath, saved.Value.Path);
            Assert.True(File.Exists(statePath));

            var replacement = memoryBefore.Value.Bytes[0] == 0xA5 ? (byte)0x5A : (byte)0xA5;
            var written = session.WriteMemory(0xC000, Enumerable.Repeat(replacement, 16).ToArray());
            Assert.True(written.IsSuccess, written.Error?.Message);
            var memoryAfterMutation = session.ReadMemory(0xC000, 16);
            Assert.True(memoryAfterMutation.IsSuccess, memoryAfterMutation.Error?.Message);
            Assert.NotEqual(memoryBefore.Value.BytesHex, memoryAfterMutation.Value.BytesHex);

            var loadedState = session.LoadState(statePath);
            Assert.True(loadedState.IsSuccess, loadedState.Error?.Message);
            Assert.True(loadedState.Value.Loaded);
            Assert.Equal(statePath, loadedState.Value.Path);

            var registersAfter = session.ReadRegisters();
            Assert.True(registersAfter.IsSuccess, registersAfter.Error?.Message);
            var memoryAfter = session.ReadMemory(0xC000, 16);
            Assert.True(memoryAfter.IsSuccess, memoryAfter.Error?.Message);

            Assert.Equal(registersBefore.Value, registersAfter.Value);
            Assert.Equal(memoryBefore.Value.BytesHex, memoryAfter.Value.BytesHex);
        }
        finally
        {
            File.Delete(statePath);
        }
    }

    private static bool NativeBridgeExists()
    {
        var nativeDir = Environment.GetEnvironmentVariable("GAMEBOY_DEBUG_MCP_NATIVE_DIR");
        if (!string.IsNullOrWhiteSpace(nativeDir) && File.Exists(Path.Combine(nativeDir, "libgameboy_debug_sameboy.so")))
        {
            return true;
        }

        var root = FindRepoRoot();
        return root is not null && File.Exists(Path.Combine(root, "native", "out", "linux-x64", "libgameboy_debug_sameboy.so"));
    }

    private static string? FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "gameboy-debug-mcp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string CreateTestFilePath(string prefix, string extension)
    {
        var root = FindRepoRoot() ?? Directory.GetCurrentDirectory();
        var directory = Path.Combine(root, "artifacts", "tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{prefix}-{Guid.NewGuid():N}{extension}");
    }

    private static void CreateMinimalRom(string path)
    {
        var rom = Enumerable.Repeat((byte)0x00, 0x8000).ToArray();
        rom[0x100] = 0x3E;
        rom[0x101] = 0x2A;
        rom[0x102] = 0xEA;
        rom[0x103] = 0x00;
        rom[0x104] = 0xC0;
        rom[0x105] = 0x18;
        rom[0x106] = 0xFE;
        var title = "MCPTEST"u8.ToArray();
        Array.Copy(title, 0, rom, 0x134, title.Length);
        rom[0x147] = 0x00;
        rom[0x148] = 0x00;
        rom[0x149] = 0x00;
        File.WriteAllBytes(path, rom);
    }

    private static void CreateLyWaitRom(string path)
    {
        var rom = Enumerable.Repeat((byte)0x00, 0x8000).ToArray();
        rom[0x100] = 0xF0; // LDH A, [$44]
        rom[0x101] = 0x44;
        rom[0x102] = 0xFE; // CP $90
        rom[0x103] = 0x90;
        rom[0x104] = 0x38; // JR C, $0100
        rom[0x105] = 0xFA;
        rom[0x106] = 0x3E; // LD A, $2A
        rom[0x107] = 0x2A;
        rom[0x108] = 0xEA; // LD [$C000], A
        rom[0x109] = 0x00;
        rom[0x10A] = 0xC0;
        rom[0x10B] = 0x18; // JR $010B
        rom[0x10C] = 0xFE;
        var title = "LYWAIT"u8.ToArray();
        Array.Copy(title, 0, rom, 0x134, title.Length);
        rom[0x147] = 0x00;
        rom[0x148] = 0x00;
        rom[0x149] = 0x00;
        File.WriteAllBytes(path, rom);
    }

    private static void CreateJoypadReadRom(string path)
    {
        var rom = Enumerable.Repeat((byte)0x00, 0x8000).ToArray();
        rom[0x100] = 0xC3; // JP $0150
        rom[0x101] = 0x50;
        rom[0x102] = 0x01;
        var offset = 0x150;
        byte[] program =
        [
            0x3E, 0x10,       // LD A, $10 - select action buttons
            0xE0, 0x00,       // LDH [$00], A
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0xF0, 0x00,       // LDH A, [$00]
            0xF0, 0x00,       // LDH A, [$00]
            0xF0, 0x00,       // LDH A, [$00]
            0xF0, 0x00,       // LDH A, [$00]
            0xEA, 0x00, 0xC0, // LD [$C000], A
            0x3E, 0x20,       // LD A, $20 - select d-pad
            0xE0, 0x00,       // LDH [$00], A
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0xF0, 0x00,       // LDH A, [$00]
            0xF0, 0x00,       // LDH A, [$00]
            0xF0, 0x00,       // LDH A, [$00]
            0xF0, 0x00,       // LDH A, [$00]
            0xEA, 0x01, 0xC0, // LD [$C001], A
            0x18, 0xFE,       // JR $
        ];

        Array.Copy(program, 0, rom, offset, program.Length);
        var title = "JOYREAD"u8.ToArray();
        Array.Copy(title, 0, rom, 0x134, title.Length);
        rom[0x147] = 0x00;
        rom[0x148] = 0x00;
        rom[0x149] = 0x00;
        File.WriteAllBytes(path, rom);
    }
}
