namespace Zafiro.GameBoy.Debug.Core;

public static class PpuStateBuilder
{
    public static PpuStateResult Build(PpuRegistersSnapshot raw)
    {
        var mode = raw.Stat & 0x03;
        var lcdEnabled = (raw.Lcdc & 0x80) != 0;
        var backgroundWindowEnabled = raw.IsColor || (raw.Lcdc & 0x01) != 0;
        return new PpuStateResult(
            Hex.FormatByte(raw.Lcdc),
            Hex.FormatByte(raw.Stat),
            mode,
            Hex.FormatByte(raw.Ly),
            Hex.FormatByte(raw.Lyc),
            Hex.FormatByte(raw.Scy),
            Hex.FormatByte(raw.Scx),
            Hex.FormatByte(raw.Wy),
            Hex.FormatByte(raw.Wx),
            Hex.FormatByte(raw.Bgp),
            Hex.FormatByte(raw.Obp0),
            Hex.FormatByte(raw.Obp1),
            Hex.FormatByte(raw.Vbk),
            lcdEnabled,
            (raw.Lcdc & 0x02) != 0,
            (raw.Lcdc & 0x20) != 0,
            backgroundWindowEnabled)
        {
            Scanline = raw.Ly,
            Dot = raw.Dot,
            TimingAuthoritative = raw.TimingAuthoritative,
            VBlank = mode == 1,
            RenderingActive = lcdEnabled && mode == 3,
            Control = new PpuControlState(
                backgroundWindowEnabled,
                raw.IsColor ? (raw.Lcdc & 0x01) != 0 : null,
                (raw.Lcdc & 0x02) != 0,
                (raw.Lcdc & 0x04) != 0 ? 16 : 8,
                (raw.Lcdc & 0x08) != 0 ? "0x9C00" : "0x9800",
                (raw.Lcdc & 0x10) != 0 ? "0x8000" : "0x9000",
                (raw.Lcdc & 0x10) == 0,
                (raw.Lcdc & 0x20) != 0,
                (raw.Lcdc & 0x40) != 0 ? "0x9C00" : "0x9800",
                lcdEnabled),
            Status = new PpuStatusState(
                mode,
                mode switch
                {
                    0 => "hblank",
                    1 => "vblank",
                    2 => "oam_scan",
                    _ => "pixel_transfer",
                },
                (raw.Stat & 0x04) != 0,
                (raw.Stat & 0x08) != 0,
                (raw.Stat & 0x10) != 0,
                (raw.Stat & 0x20) != 0,
                (raw.Stat & 0x40) != 0),
            Timeline = raw.Timeline,
        };
    }
}
