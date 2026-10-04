namespace Zafiro.GameBoy.Debug.Core;

public static class VideoWriteTracing
{
    public const int MaxEvents = 10_000;

    private static readonly IReadOnlyDictionary<ushort, string> PpuRegisterNames =
        new Dictionary<ushort, string>
        {
            [0xFF40] = "LCDC",
            [0xFF41] = "STAT",
            [0xFF42] = "SCY",
            [0xFF43] = "SCX",
            [0xFF45] = "LYC",
            [0xFF46] = "DMA",
            [0xFF47] = "BGP",
            [0xFF48] = "OBP0",
            [0xFF49] = "OBP1",
            [0xFF4A] = "WY",
            [0xFF4B] = "WX",
            [0xFF4F] = "VBK",
            [0xFF51] = "HDMA1",
            [0xFF52] = "HDMA2",
            [0xFF53] = "HDMA3",
            [0xFF54] = "HDMA4",
            [0xFF55] = "HDMA5",
            [0xFF68] = "BGPI",
            [0xFF69] = "BGPD",
            [0xFF6A] = "OBPI",
            [0xFF6B] = "OBPD",
        };

    public static IReadOnlySet<VideoWriteKind> DefaultKinds { get; } =
        new HashSet<VideoWriteKind> { VideoWriteKind.Vram, VideoWriteKind.Oam, VideoWriteKind.PpuRegister };

    public static IReadOnlySet<ushort> DefaultPpuRegisters { get; } =
        PpuRegisterNames.Keys.ToHashSet();

    public static bool TryClassify(ushort address, out VideoWriteKind kind)
    {
        if (address is >= 0x8000 and <= 0x9FFF)
        {
            kind = VideoWriteKind.Vram;
            return true;
        }

        if (address is >= 0xFE00 and <= 0xFE9F)
        {
            kind = VideoWriteKind.Oam;
            return true;
        }

        if (PpuRegisterNames.ContainsKey(address))
        {
            kind = VideoWriteKind.PpuRegister;
            return true;
        }

        kind = default;
        return false;
    }

    public static string? RegisterName(ushort address) =>
        PpuRegisterNames.TryGetValue(address, out var name) ? name : null;

    public static bool TryParsePpuRegister(string value, out ushort address)
    {
        var match = PpuRegisterNames.FirstOrDefault(pair => pair.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (!match.Equals(default(KeyValuePair<ushort, string>)))
        {
            address = match.Key;
            return true;
        }

        var parsed = GameBoyAddress.Parse(value);
        if (parsed.IsSuccess && PpuRegisterNames.ContainsKey(parsed.Value.Address))
        {
            address = parsed.Value.Address;
            return true;
        }

        address = 0;
        return false;
    }
}
