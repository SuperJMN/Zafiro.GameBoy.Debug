using System.Security.Cryptography;

namespace GameBoy.Debug.Core;

public static class TilemapReader
{
    private const int VideoRamBase = 0x8000;
    private const int TilemapLength = 32 * 32;

    private static readonly ushort[] TilemapAddresses = [0x9800, 0x9C00];

    public static TilemapSetDumpResult Build(
        ReadOnlySpan<byte> tileBank,
        ReadOnlySpan<byte> attributeBank,
        string model,
        bool includeDetails,
        TimelineCounters timeline)
    {
        var hasAttributes = model.Equals("CGB", StringComparison.OrdinalIgnoreCase) && attributeBank.Length >= 0x2000;
        var maps = new TilemapSnapshot[TilemapAddresses.Length];
        for (var index = 0; index < TilemapAddresses.Length; index++)
        {
            var address = TilemapAddresses[index];
            var offset = address - VideoRamBase;
            var tiles = tileBank.Slice(offset, TilemapLength);
            var attributes = hasAttributes ? attributeBank.Slice(offset, TilemapLength) : [];
            maps[index] = new TilemapSnapshot(
                Hex.FormatWord(address),
                Hash(tiles),
                hasAttributes ? Hash(attributes) : null,
                includeDetails ? Rows(tiles) : null,
                includeDetails && hasAttributes ? Rows(attributes) : null);
        }

        return new TilemapSetDumpResult(model, includeDetails, maps, timeline);
    }

    private static string[] Rows(ReadOnlySpan<byte> bytes)
    {
        var rows = new string[32];
        for (var row = 0; row < rows.Length; row++)
        {
            rows[row] = Hex.FormatBytes(bytes.Slice(row * 32, 32).ToArray());
        }

        return rows;
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";
}
