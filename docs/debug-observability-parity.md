# NesMcp Debug-Observability Parity

The NesMcp `deterministic PPU observability` work introduced four capabilities needed to diagnose transient corruption rather than inspect only the final state. Game Boy needs the same evidence, but not always the same bus primitive.

| NesMcp capability | GameBoyMcp capability | Game Boy adaptation |
| --- | --- | --- |
| `observe_screen` | `observe_screen` | Hashes exact RGB24 output so distinct CGB colors are never collapsed into the same DMG shade. |
| `observe_execution` | `observe_execution` | Correlates framebuffer changes, safe RAM/VRAM/OAM probes, PPU state, both tilemaps, input, breakpoints, and video writes. |
| `trace_ppu_register_writes` | `trace_video_writes` | Includes direct VRAM writes, CPU and DMA OAM writes, and LCD/PPU registers because GB has no NES-style `$2007` data port. |
| `dump_nametables` | `dump_tilemaps` | Snapshots `$9800/$9C00`; CGB attributes come from VRAM bank 1 and the selected `VBK` is preserved. |

The supporting primitives were also aligned:

- `read_ppu_state` now includes scanline/dot, decoded DMG/CGB LCDC/STAT state, VBlank/rendering state, the selected VRAM bank, the cumulative timeline, and an explicit `timingAuthoritative` flag.
- `read_screen_region` accepts `dmg_shades_raw`, `rgb24`, and `rgb24_raw`; raw formats can return a complete 160x144 frame.
- the managed CPU reports actual retired instructions during both stepping and frame execution, so video events can be ordered against a meaningful cumulative instruction counter.

## Investigation workflow

1. Save state immediately before the suspect sequence.
2. Use `observe_execution` when corruption must be correlated with memory or video writes; use `observe_screen` when visible change alone is enough.
3. Record the suspicious `frameOffset`, framebuffer hash, changed bounds, probe bytes, tilemap hashes, and nearby video events.
4. Reload state, replay to the focal frame, and use `trace_video_writes` with narrower kinds/registers.
5. Reload again and stop at the relevant PC or memory condition for instruction-level inspection with `read_ppu_state`, `read_screen_region` raw formats, `dump_tilemaps`, `dump_oam`, and `dump_tileset`.

The MCP host uses the managed CoreBoy backend, which implements the complete workflow and supplies authoritative dot timing. The legacy SameBoy project supports exact `observe_screen` capture only when no managed breakpoints are set; its native frame API cannot honor sub-frame breakpoint stops. SameBoy also does not expose the managed core hooks required for continuous write correlation or atomic VRAM-bank snapshots, and its PPU state therefore returns `dot: null` with `timingAuthoritative: false`.
