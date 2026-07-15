# MCP Tools

All addresses are hexadecimal strings. Inputs are bounded; trace-like tools require explicit limits. Execution and state results include a `timeline` object with cumulative `frames`, `cycles`, and `instructions` since the last ROM load or reset.

## load_rom

Input:

```json
{ "path": "path/to/game.gb" }
```

Output:

```json
{ "loaded": true, "romTitle": "GAME", "model": "DMG" }
```

## save_state

Input:

```json
{ "path": "path/to/state.s0" }
```

Output:

```json
{ "saved": true, "path": "path/to/state.s0" }
```

## load_state

Input:

```json
{ "path": "path/to/state.s0" }
```

Output:

```json
{ "loaded": true, "path": "path/to/state.s0" }
```

## reset

Input:

```json
{}
```

Output:

```json
{ "reset": true }
```

## step_instruction

Input:

```json
{ "count": 1 }
```

Output:

```json
{
  "pcBefore": "0x0100",
  "pcAfter": "0x0101",
  "registers": {},
  "disassembly": "0x0100: NOP",
  "instructionsRun": 1,
  "timeline": { "frames": 0, "cycles": 4, "instructions": 1 }
}
```

## run_frame

Input:

```json
{ "count": 1 }
```

Output:

```json
{ "framesRun": 1, "registers": {}, "hitBreakpoint": false, "timeline": { "frames": 1, "cycles": 70224, "instructions": 17556 } }
```

`run_frame` may stop before the requested frame count when a breakpoint or watchpoint is hit, including a breakpoint at the initial PC.

## observe_screen

Atomically runs up to 600 complete frames. Every sample contains a SHA-256 identity of the exact rendered RGB24 frame, the number and bounds of changed pixels, and compact 8x8 tile-row masks. RGB identity is used rather than reduced DMG shades so CGB palette corruption cannot disappear during observation.

```json
{ "frameCount": 120 }
```

Save state before a suspicious sequence, use the returned `frameOffset` to find the transient frame, reload, then replay to the focal frame for exact region, tilemap, OAM, or video-write evidence. Observation stops without sampling an incomplete frame when execution reaches a breakpoint.

## observe_execution

Runs a bounded input sequence atomically and correlates, for every completed frame:

- exact RGB framebuffer hash and compact visible changes;
- up to 16 side-effect-free RAM/VRAM/OAM probes (64 bytes each, 256 bytes total per frame);
- optional authoritative PPU state;
- a bounded continuous video-write stream; and
- initial/final hashes for both tilemaps, including CGB attribute bank 1.

```json
{
  "frameCount": 120,
  "buttons": ["right"],
  "memoryProbes": [
    { "address": "0xC000", "length": 16 },
    { "address": "0x9800", "length": 32 }
  ],
  "includePpuState": true,
  "traceVideoWrites": true,
  "maxVideoEvents": 1000,
  "videoKinds": ["vram", "oam", "ppu_register"],
  "ppuRegisters": ["LCDC", "SCX", "SCY", "DMA", "WY", "WX", "VBK"]
}
```

Probe ranges are deliberately restricted to VRAM `$8000-$9FFF`, cartridge RAM `$A000-$BFFF`, WRAM/echo `$C000-$FDFF`, OAM `$FE00-$FE9F`, and HRAM `$FF80-$FFFE`. I/O probes are rejected because reading I/O is not a generally side-effect-free observation.

The event payload truncates at `maxVideoEvents` while execution continues. Compare `videoEventCount` with `videoEventsObserved` and inspect `videoTraceTruncated`. Held input is released on every exit path.

## trace_video_writes

Continuously records selected direct VRAM writes, OAM writes, and LCD/PPU-register writes for up to 600 frames. This is the Game Boy equivalent of NES PPU-register tracing: Game Boy writes tile/sprite data directly to memory instead of routing it through one `PPUDATA` register.

```json
{
  "frameCount": 2,
  "maxEvents": 1000,
  "kinds": ["vram", "oam", "ppu_register"],
  "ppuRegisters": ["LCDC", "SCX", "SCY", "DMA", "WY", "WX", "VBK"],
  "buttons": ["right"]
}
```

Each event includes bus order, value, writing PC, zero-based `frameOffset`, absolute frame/cycle/instruction counters, VRAM bank when relevant, and immediate PPU snapshots before and after the write. Supported register filters include DMG LCD registers plus CGB `VBK`, HDMA, and color-palette index/data registers. At most 10,000 events are returned; observation continues after the cap and reports `eventsObserved` plus `truncated`.

## dump_tilemaps

Snapshots both 32x32 maps at `$9800` and `$9C00` without changing `VBK`. Compact mode returns SHA-256 identities; `includeDetails: true` also returns all tile rows and, for CGB ROMs, the corresponding attribute rows from VRAM bank 1.

```json
{ "includeDetails": false }
```

## step_over

Input:

```json
{ "maxInstructions": 100000 }
```

Output:

```json
{ "stopped": true, "reason": "step_over", "pc": "0x0103", "registers": {}, "timeline": {}, "instructionsRun": 4 }
```

If the current instruction is a `CALL` or `RST`, this runs until execution returns to the instruction after it. Otherwise it steps a single instruction and returns reason `step`. Other reasons: `breakpoint`, `watchpoint`, `halt`, `maxInstructions`.

## step_out

Input:

```json
{ "maxInstructions": 100000 }
```

Output:

```json
{ "stopped": true, "reason": "step_out", "pc": "0x0103", "registers": {}, "timeline": {}, "instructionsRun": 4 }
```

Runs until the current stack frame returns. Other reasons: `breakpoint`, `watchpoint`, `halt`, `maxInstructions`.

## set_joypad

Input:

```json
{ "buttons": ["right", "a"] }
```

Output:

```json
{
  "right": true,
  "left": false,
  "up": false,
  "down": false,
  "a": true,
  "b": false,
  "select": false,
  "start": false,
  "pressed": ["right", "a"]
}
```

Valid button names are `right`, `left`, `up`, `down`, `a`, `b`, `select`, and `start`. Pass an empty array to release every button. SameBoy's physical button-bounce emulation is disabled by the bridge so MCP-driven input is deterministic.

## press_buttons

Input:

```json
{ "buttons": ["a"], "frameCount": 6 }
```

Output:

```json
{ "framesRun": 6, "released": { "pressed": [] }, "registers": {} }
```

This holds the requested buttons for `frameCount` frames, then releases every button before returning. `frameCount` must be between 1 and 600.

## run_input_timeline

Input:

```json
{
  "steps": [
    { "frames": 60, "buttons": ["right"] },
    { "frames": 4, "buttons": ["right", "a"], "capture": true },
    { "frames": 40, "buttons": ["right"], "readPpuState": true, "dumpOam": true }
  ]
}
```

Output:

```json
{
  "framesRun": 104,
  "released": { "pressed": [] },
  "steps": [
    { "index": 0, "framesRun": 60, "totalFrames": 60, "buttons": ["right"], "timeline": {} },
    { "index": 1, "framesRun": 4, "totalFrames": 64, "buttons": ["right", "a"], "screenCapture": { "mimeType": "image/png" }, "timeline": {} },
    { "index": 2, "framesRun": 40, "totalFrames": 104, "buttons": ["right"], "ppuState": {}, "oam": {}, "timeline": {} }
  ],
  "timeline": {}
}
```

Each step defines the complete held-button set for that interval. The scenario is executed atomically under the session lock and releases all buttons before returning, including failure paths. Step count, per-step frames, and total frames are bounded.

## continue_until_break

Input:

```json
{ "maxInstructions": 1000000 }
```

Output:

```json
{ "stopped": true, "reason": "breakpoint", "pc": "0x0150", "registers": {} }
```

Reasons: `breakpoint`, `watchpoint`, `maxInstructions`, `halt`, `error`.

## run_until_condition

Input:

```json
{ "condition": "LY >= 0x90", "maxInstructions": 1000000, "maxFrames": 120 }
```

Output:

```json
{
  "stopped": true,
  "reason": "condition",
  "pc": "0x0150",
  "instructionsRun": 9216,
  "framesRun": 1,
  "registers": {},
  "ppuState": { "ly": "0x90", "stat": "0x85" },
  "timeline": {}
}
```

Reasons: `condition`, `breakpoint`, `watchpoint`, `halt`, `maxInstructions`, and `maxFrames`. The condition grammar is the same comparison grammar used by conditional breakpoints.

## set_breakpoint

Input:

```json
{ "address": "0x1234", "condition": "A == 0x10" }
```

Output:

```json
{ "breakpointId": "bp-1", "address": "0x1234", "enabled": true }
```

`condition` is optional. A null or empty condition is an unconditional breakpoint. Conditional breakpoints are evaluated in the C# session loop when `PC` reaches the breakpoint address.

Supported condition grammar is a single comparison:

```text
<left> <operator> <constant>
```

- Left operands: 8-bit registers `A B C D E F H L`, 16-bit registers `AF BC DE HL SP PC`, PPU/IO aliases, or 8-bit memory reads `[addr]` / `[reg]`.
- PPU/IO aliases: `LCDC`, `STAT`, `LY`, `LYC`, `SCX`, `SCY`, `WX`, `WY`, `BGP`, `OBP0`, `OBP1`, and `VBK`.
- Memory addresses can be decimal or `0x` hexadecimal constants; memory registers must be 16-bit registers.
- Operators: `== != < <= > >=`.
- Constants: decimal or `0x` hexadecimal values from `0` to `0xFFFF`.

Examples: `A == 0x10`, `B != 5`, `HL >= 0xC000`, `LY >= 0x90`, `STAT == 0x85`, `SCX == 4`, `[0xFF80] == 1`, `[HL] < 4`. Invalid conditions are rejected by `set_breakpoint` and `run_until_condition`.

## clear_breakpoint

Input:

```json
{ "breakpointId": "bp-1" }
```

Output:

```json
{ "cleared": true }
```

## list_breakpoints

Input:

```json
{}
```

Output:

```json
{
  "breakpoints": [
    { "id": "bp-1", "address": "0x0150", "enabled": true, "condition": null },
    { "id": "bp-2", "address": "0xC000", "enabled": true, "condition": "a == 1" }
  ]
}
```

## set_watchpoint

Input:

```json
{ "address": "0xC000", "mode": "write" }
```

Output:

```json
{ "watchpointId": "wp-1", "address": "0xC000", "mode": "write", "enabled": true, "length": 1 }
```

Modes are `read`, `write`, or `access`. Read watchpoints also trigger on instruction fetches when the watched address is executed.

## set_watchpoint_range

Input:

```json
{ "address": "0x9800", "length": 32, "mode": "write" }
```

Output:

```json
{ "watchpointId": "wp-2", "address": "0x9800", "mode": "write", "enabled": true, "length": 32 }
```

Ranges must fit within `0x0000..0xFFFF` and are bounded to avoid unbounded observation. `set_watchpoint` is equivalent to a range length of 1.

## clear_watchpoint

Input:

```json
{ "watchpointId": "wp-1" }
```

Output:

```json
{ "cleared": true }
```

## list_watchpoints

Input:

```json
{}
```

Output:

```json
{
  "watchpoints": [
    { "id": "wp-1", "address": "0xC000", "mode": "write", "enabled": true, "length": 1 },
    { "id": "wp-2", "address": "0x9800", "mode": "write", "enabled": true, "length": 32 }
  ]
}
```

## get_state

Input:

```json
{}
```

Output with a ROM loaded:

```json
{ "romLoaded": true, "title": "GAME", "model": "DMG", "halted": false, "pc": "0x0100", "timeline": { "frames": 0, "cycles": 0, "instructions": 0 } }
```

Output before loading a ROM:

```json
{ "romLoaded": false, "title": null, "model": null, "halted": false, "pc": null, "timeline": { "frames": 0, "cycles": 0, "instructions": 0 } }
```

## read_registers

Input:

```json
{}
```

Output:

```json
{
  "af": "0x01B0",
  "bc": "0x0013",
  "de": "0x00D8",
  "hl": "0x014D",
  "sp": "0xFFFE",
  "pc": "0x0100",
  "a": "0x01",
  "f": "0xB0",
  "b": "0x00",
  "c": "0x13",
  "d": "0x00",
  "e": "0xD8",
  "h": "0x01",
  "l": "0x4D",
  "ime": false,
  "halted": false
}
```

## read_memory

Input:

```json
{ "address": "0xC000", "length": 32 }
```

Output:

```json
{ "address": "0xC000", "bytesHex": "00 01 02", "bytes": [0, 1, 2], "ascii": "..." }
```

## write_memory

Input:

```json
{ "address": "0xC000", "bytes": [1, 2, 3] }
```

Output:

```json
{ "written": true, "address": "0xC000", "length": 3 }
```

## disassemble

Input:

```json
{ "address": "0x0150", "instructionCount": 16 }
```

Output:

```json
{
  "address": "0x0150",
  "instructions": [
    { "address": "0x0150", "bytes": "3E 01", "text": "LD A, $01", "symbol": null }
  ]
}
```

## load_symbols

Input:

```json
{ "path": "path/to/game.sym" }
```

Output:

```json
{ "loaded": true, "symbolCount": 1234 }
```

## resolve_symbol

Input:

```json
{ "name": "Player.X" }
```

Output:

```json
{ "name": "Player.X", "address": "0xC120", "bank": null }
```

## read_symbol

Input:

```json
{ "name": "Player.X", "length": 1 }
```

Output:

```json
{ "name": "Player.X", "address": "0xC120", "bytes": [42], "bytesHex": "2A" }
```

## dump_oam

Input:

```json
{}
```

Output:

```json
{
  "sprites": [
    { "index": 0, "y": 16, "x": 32, "tile": "0x04", "attributes": "0x00", "visible": true }
  ]
}
```

## read_ppu_state

Input:

```json
{}
```

Output:

```json
{
  "lcdc": "0x91",
  "stat": "0x85",
  "mode": 1,
  "ly": "0x90",
  "lyc": "0x00",
  "scy": "0x00",
  "scx": "0x00",
  "wy": "0x00",
  "wx": "0x00",
  "bgp": "0xFC",
  "obp0": "0xFF",
  "obp1": "0xFF",
  "vbk": "0xFF",
  "lcdEnabled": true,
  "spritesEnabled": true,
  "windowEnabled": false,
  "backgroundEnabled": true,
  "scanline": 144,
  "dot": 12,
  "timingAuthoritative": true,
  "vBlank": true,
  "renderingActive": false,
  "control": {
    "backgroundWindowEnabled": true,
    "backgroundWindowPriorityEnabled": true,
    "backgroundTilemapAddress": "0x9800",
    "tileDataAddress": "0x8000",
    "windowTilemapAddress": "0x9800",
    "spriteHeight": 8
  },
  "status": { "mode": 1, "modeName": "vblank", "lycEqualsLy": false },
  "timeline": { "frames": 1, "cycles": 70224, "instructions": 17556 }
}
```

The raw LCD registers are accompanied by decoded LCDC/STAT fields, rendering state, selected CGB VRAM bank, and cumulative execution counters. `timingAuthoritative` says whether `dot` comes from the backend's live PPU clock. The managed backend reports an authoritative dot; the legacy SameBoy adapter reports `dot: null` and `timingAuthoritative: false` instead of fabricating sub-frame timing. On CGB, LCDC bit 0 is decoded as `backgroundWindowPriorityEnabled`; background/window rendering remains enabled as required by CGB semantics.

## capture_screen

Input:

```json
{}
```

Output content:

```json
{
  "type": "image",
  "data": "<base64 PNG bytes>",
  "mimeType": "image/png"
}
```

Saved artifact input:

```json
{ "path": "artifacts/runner-frame-120.png", "includeMetadata": true }
```

Saved artifact output:

```json
{
  "width": 160,
  "height": 144,
  "mimeType": "image/png",
  "saved": true,
  "path": "artifacts/runner-frame-120.png",
  "metadata": {
    "timeline": { "frames": 120, "cycles": 8426880, "instructions": 12345 },
    "registers": {},
    "ppuState": {},
    "romTitle": "GAME",
    "model": "DMG"
  }
}
```

Without `path`, `capture_screen` keeps returning inline image content and writes no files. Saved paths must be relative `.png` paths that stay within the current working directory; absolute paths and `..` escapes are rejected.

## find_last_writer

Input:

```json
{ "address": "0xC000" }
```

Output:

```json
{ "found": true, "address": "0xC000", "pc": "0x0105", "value": "0x2A", "writeCount": 1 }
```

This reports writes observed after the session started. It is not a time-travel query for writes that happened before the backend was running.

## find_last_writers

Input:

```json
{ "address": "0x9800", "length": 32 }
```

Output:

```json
{
  "writers": [
    { "found": true, "address": "0x9800", "pc": "0x1234", "value": "0x2A", "writeCount": 1 },
    { "found": false, "address": "0x9801", "pc": null, "value": null, "writeCount": 0 }
  ]
}
```

The range is bounded and must fit within `0x0000..0xFFFF`.

## trace_until_write

Input:

```json
{ "address": "0xC000", "maxInstructions": 1000000 }
```

Output:

```json
{
  "stopped": true,
  "reason": "write",
  "address": "0xC000",
  "pc": "0x0105",
  "value": "0x2A",
  "instructionsRun": 12,
  "registers": {},
  "timeline": {}
}
```

Reasons: `write`, `maxInstructions`.

## trace_until_write_range

Input:

```json
{ "address": "0x9800", "length": 32, "maxInstructions": 1000000 }
```

Output:

```json
{
  "stopped": true,
  "reason": "write",
  "address": "0x9800",
  "length": 32,
  "hitAddress": "0x9812",
  "pc": "0x1234",
  "value": "0x2A",
  "instructionsRun": 4096,
  "registers": {},
  "ppuState": {},
  "disassembly": { "instructions": [] },
  "timeline": {}
}
```

Reasons: `write`, `maxInstructions`. The result reports the concrete address hit inside the requested range.

## read_screen_region

Input:

```json
{ "x": 0, "y": 0, "width": 160, "height": 32, "format": "dmg_shades" }
```

Output:

```json
{
  "x": 0,
  "y": 0,
  "width": 160,
  "height": 32,
  "format": "dmg_shades",
  "pixelCount": 5120,
  "values": null,
  "histogram": { "0": 4700, "1": 300, "2": 120, "3": 0 },
  "rowHashes": ["0x0D58A2C1"],
  "screenToBgTile": { "tileX": 0, "tileY": 0, "tilemapAddress": "0x9800" }
}
```

Formats are `dmg_shades`, `dmg_shades_raw`, `rgb24`, and `rgb24_raw`. Summary formats include raw `values` automatically up to 1,024 pixels, then return a histogram plus row hashes. Raw formats explicitly return every value, including a complete 160x144 frame. RGB values are integers from `0x000000` through `0xFFFFFF`. Bounds must fit within the screen.

## dump_tilemap

Input:

```json
{ "address": "0x9800" }
```

Output:

```json
{ "address": "0x9800", "width": 32, "height": 32, "rows": ["00 01 ..."] }
```

The address must be `0x9800` or `0x9C00`.

## dump_tileset

Input:

```json
{ "address": "0x8000", "tileCount": 16 }
```

Output:

```json
{
  "address": "0x8000",
  "tileCount": 16,
  "tiles": [
    { "index": 0, "address": "0x8000", "bytesHex": "00 00 ..." }
  ]
}
```

## Errors

Expected failures are returned as structured objects:

```json
{
  "error": {
    "code": "invalid_address",
    "message": "'zzzz' is not a valid Game Boy address."
  }
}
```
