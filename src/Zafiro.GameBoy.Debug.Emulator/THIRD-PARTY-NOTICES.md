# Third-Party Notices

Zafiro.GameBoy.Debug.Mcp includes third-party software. The licenses and notices below are
reproduced in compliance with their terms. The authors and copyright holders of
the third-party software are credited here; their ownership is fully retained.

---

## CoreBoy (vendored, distributed)

The `CoreBoy/` subfolder of the `Zafiro.GameBoy.Debug.Emulator` project contains a
trimmed, vendored copy of the **CoreBoy** Game Boy emulator core by **David Whitney**
(https://github.com/davidwhitney/CoreBoy), which is a C# port of **coffee-gb** by
**Tomasz Rękawek** (https://github.com/trekawek/coffee-gb). This code is compiled
into `Zafiro.GameBoy.Debug.Emulator.dll`, which ships inside the `Zafiro.GameBoy.Debug.Mcp` package.

Both CoreBoy and coffee-gb are licensed under the MIT License. The upstream
copyright notice is preserved verbatim at
`src/Zafiro.GameBoy.Debug.Emulator/CoreBoy/LICENSE` and reproduced below:

```
MIT License

Copyright (c) 2017 Tomasz Rękawek

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### Modifications to CoreBoy

The vendored `CoreBoy/` sources were trimmed and lightly adapted for a headless
debugging host. Emulation logic is otherwise unchanged.

- Removed UI/audio frontends and their dependencies (ImageSharp, NAudio,
  Newtonsoft.Json, CommandLineParser): `gui/BitmapDisplay`, `gui/GameboyDisplayFrame`,
  `gui/WinSound`, `gui/Emulator`, `gui/ConsoleWriteSerialEndpoint`, the `debugging/`
  command system, and `memory/cart/battery/FileBattery`.
- `GameboyOptions`: removed `CommandLine` attributes.
- `memory/cart/Cartridge`: always uses `NullBattery` (battery saves are not needed).
- `cpu/Cpu`: added a read-only `InterruptMasterEnabled` accessor (IME is not memory-mapped).
- `memory/Mmu`: added a `WriteObserver` hook to support the find_last_writer /
  trace_until_write debugging tools.

---

## SameBoy (optional backend, NOT distributed)

The repository contains an optional, legacy native backend (`Zafiro.GameBoy.Debug.SameBoy`
and `native/sameboy_mcp_bridge.c`) that links against **SameBoy** by **Lior Halphon**
(https://github.com/LIJI32/SameBoy). SameBoy is **not** vendored in this repository
and is **not** included in the published `Zafiro.GameBoy.Debug.Mcp` package; it is cloned and built
from source only when a developer explicitly opts in. The bridge file
`native/sameboy_mcp_bridge.c` is original code of this project that calls SameBoy's
public C API.

SameBoy is licensed under the Expat (MIT) License:

```
Expat License

Copyright (c) 2015-2026 Lior Halphon

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
