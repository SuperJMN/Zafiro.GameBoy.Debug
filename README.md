# GameBoy.Mcp

`GameBoy.Mcp` is a cross-platform .NET MCP server for inspecting and controlling a Game Boy or Game Boy Color ROM. It includes bounded workflows for correlating transient screen corruption with RAM, tilemap, PPU, VRAM, OAM, and LCD-register state. It is distributed as a .NET tool (command: `gameboymcp`).

The emulator core is **pure managed C#** (a trimmed, vendored copy of the MIT-licensed [CoreBoy](https://github.com/davidwhitney/CoreBoy), itself a port of [coffee-gb](https://github.com/trekawek/coffee-gb)). There are **no native dependencies**, so a single package runs anywhere .NET 10 runs — Windows, macOS and Linux, on x64 and arm64.

## Install

Run it on demand with .NET 10's `dnx` (no install required):

```bash
dnx GameBoy.Mcp
```

Or install it globally:

```bash
dotnet tool install -g GameBoy.Mcp
gameboymcp
```

## Build

Requirements:

- .NET 10 SDK

Build the solution:

```bash
dotnet build gameboy-debug-mcp.slnx
```

That's it — no native toolchain or build step is required.

## Run

From the repo root:

```bash
dotnet run --project src/GameBoy.Debug.Mcp/GameBoy.Debug.Mcp.csproj
```

Screen captures are returned inline as PNG images over MCP by default. Pass a safe relative `.png` path to `capture_screen` when you want a file artifact written under the current working directory.

## Connect An MCP Client

Use stdio transport. With the tool installed (or via `dnx`), the client entry is path-independent:

```json
{
  "mcpServers": {
    "gameboy": {
      "command": "dnx",
      "args": ["GameBoy.Mcp", "--yes"]
    }
  }
}
```

If you installed the tool globally (`dotnet tool install -g GameBoy.Mcp`), use the command directly:

```json
{
  "mcpServers": {
    "gameboy": {
      "command": "gameboymcp"
    }
  }
}
```

For development against a local checkout you can still run it from source:

```json
{
  "mcpServers": {
    "gameboy": {
      "command": "dotnet",
      "args": ["run", "--project", "src/GameBoy.Debug.Mcp/GameBoy.Debug.Mcp.csproj"]
    }
  }
}
```

### Codex Tool Discovery Troubleshooting

The current source exposes 42 MCP tools through the server's real `tools/list` output, including advanced debugging tools such as `observe_execution`, `trace_video_writes`, `observe_screen`, `dump_tilemaps`, `run_input_timeline`, and `trace_until_write`.

Some MCP clients, including Codex, may initially show only the subset of tools that has been surfaced to the current model session. Do not treat that initial visible subset as the complete server capability list. If tools such as `capture_screen` or `run_input_timeline` appear to be missing:

1. Verify that the active client entry runs the current package, for example `dnx GameBoy.Mcp --yes` or `gameboymcp`.
2. In Codex, inspect the configured MCP server with `/mcp verbose` and confirm that it points at the expected command.
3. Ask Codex to search for the exact advanced tool names, for example `gameboy_debug load_rom run_input_timeline capture_screen dump_oam read_screen_region`.
4. When in doubt, use an MCP inspector or direct MCP client probe against the same command and check the actual `tools/list` response. A local build of the current source should report 42 tools.

If the direct `tools/list` response includes a tool but Codex has not surfaced it yet, the package/server is not stale. It is a deferred tool-discovery issue in the client session.

## Tools

Implemented tools:

- `load_rom`
- `save_state`
- `load_state`
- `reset`
- `step_instruction`
- `step_over`
- `step_out`
- `run_until_condition`
- `run_frame`
- `run_input_timeline`
- `set_joypad`
- `press_buttons`
- `continue_until_break`
- `set_breakpoint`
- `clear_breakpoint`
- `list_breakpoints`
- `set_watchpoint`
- `clear_watchpoint`
- `list_watchpoints`
- `set_watchpoint_range`
- `get_state`
- `read_registers`
- `read_memory`
- `write_memory`
- `disassemble`
- `load_symbols`
- `resolve_symbol`
- `read_symbol`
- `dump_oam`
- `read_ppu_state`
- `capture_screen`
- `read_screen_region`
- `find_last_writer`
- `find_last_writers`
- `trace_until_write`
- `trace_until_write_range`
- `trace_video_writes`
- `observe_screen`
- `observe_execution`
- `dump_tilemaps`
- `dump_tilemap`
- `dump_tileset`

See [docs/mcp-tools.md](docs/mcp-tools.md) for schemas and examples.

## Current Limitations

- The managed core skips the external boot ROM and applies a standard post-boot register state. This is deterministic and practical for debugging, but it is not a boot-ROM-accurate startup trace.
- Conditional breakpoints and `run_until_condition` are evaluated by the C# session loop and support register, PPU/IO alias, and memory comparisons such as `A == 0x10`, `LY >= 0x90`, `SCX == 4`, `HL >= 0xC000`, and `[HL] < 4`.
- `.sym` parsing is intentionally simple: `BANK:ADDR Name` and `ADDR Name` lines with `;` or `#` comments.
- `capture_screen` returns inline PNG image content unless a safe relative `.png` artifact path is provided.
- Exact continuous video-write correlation and atomic CGB tilemap-bank snapshots require the managed CoreBoy backend used by the MCP host. The legacy SameBoy project still supports `observe_screen`, but reports explicit `not_supported` results for those managed-only workflows.
- Savestates capture CPU registers and all CPU-visible RAM/IO; MBC bank selection and sub-frame PPU/APU timing are not captured, so save/restore is intended at frame boundaries.

## Emulator Core

The emulator is a pure-managed C# core: a trimmed, vendored copy of [CoreBoy](https://github.com/davidwhitney/CoreBoy) by David Whitney (MIT), itself a C# port of [coffee-gb](https://github.com/trekawek/coffee-gb) by Tomasz Rękawek (MIT). Only the emulation core is kept (CPU, MMU, PPU, timers, interrupts, sound, serial, cartridge mappers); UI/audio frontends and their dependencies are removed. See [`src/GameBoy.Debug.Emulator/THIRD-PARTY-NOTICES.md`](src/GameBoy.Debug.Emulator/THIRD-PARTY-NOTICES.md) for full attribution, license texts, and the list of modifications.

A legacy, optional native backend (`GameBoy.Debug.SameBoy`) links against [SameBoy](https://github.com/LIJI32/SameBoy) by Lior Halphon (Expat/MIT). SameBoy is not vendored and is not part of the published tool; it is cloned and built from source only if a developer opts in.

## License

This project is licensed under the [MIT License](LICENSE) © José Manuel Nieto (@SuperJMN).

It includes third-party software under its own license. The vendored CoreBoy core (MIT) is distributed inside the `GameBoy.Mcp` package; its copyright notice and the notices for all third-party components are reproduced in [`THIRD-PARTY-NOTICES.md`](src/GameBoy.Debug.Emulator/THIRD-PARTY-NOTICES.md), which is also bundled in the published package. All third-party authors retain full ownership of their work.

## Test

```bash
dotnet test gameboy-debug-mcp.slnx
```

The managed-core integration tests run on every platform with no native dependency. If the optional SameBoy native library (`native/out/linux-x64/libgameboy_debug_sameboy.so`) exists, the legacy SameBoy integration tests also run.

## Deployment

Distribution is handled by [DotnetDeployer](https://github.com/SuperJMN/DotnetDeployer), configured in [`deployer.yaml`](deployer.yaml). It packs the `GameBoy.Mcp` tool and pushes it to NuGet. Because the emulator core is pure managed C#, the package is a single architecture-agnostic artifact that runs on every platform — no per-RID native builds.

Publish locally (requires the `NUGET_API_KEY` environment variable):

```bash
dnx dotnetdeployer.tool            # add --dry-run to pack without publishing
```

CI/CD is provided by [DotnetDeployer.Fleet](https://github.com/SuperJMN/DotnetDeployer.Fleet): a coordinator/worker that clones the repo and runs DotnetDeployer on every commit. Any worker with the .NET 10 SDK can build and publish the package.
