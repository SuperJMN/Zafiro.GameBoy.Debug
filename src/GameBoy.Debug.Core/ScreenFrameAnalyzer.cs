using System.Numerics;
using System.Security.Cryptography;

namespace GameBoy.Debug.Core;

public static class ScreenFrameAnalyzer
{
    public const int Width = 160;
    public const int Height = 144;
    public const int PixelCount = Width * Height;

    private const int TileSize = 8;

    public static DebugResult<int> Capture(IRgbFrameSource source, Memory<uint> destination)
    {
        if (destination.Length < PixelCount)
        {
            return DebugResult<int>.Failure("invalid_screen_frame_buffer", $"Frame buffer must contain at least {PixelCount} pixels.");
        }

        var capture = source.CopyRgbFrame(destination);
        if (!capture.IsSuccess)
        {
            return DebugResult<int>.Failure(capture.Error!.Code, capture.Error.Message);
        }

        return capture.Value == PixelCount
            ? capture
            : DebugResult<int>.Failure("invalid_screen_frame", $"Expected {PixelCount} RGB pixels from the active backend.");
    }

    public static ScreenFrameObservation Compare(
        ReadOnlySpan<uint> previous,
        ReadOnlySpan<uint> current,
        int frameOffset,
        ulong totalFrame)
    {
        var changedPixels = 0;
        var minX = Width;
        var minY = Height;
        var maxX = -1;
        var maxY = -1;
        var tileRows = new uint[Height / TileSize];

        for (var index = 0; index < current.Length; index++)
        {
            if ((previous[index] & 0xFFFFFF) == (current[index] & 0xFFFFFF))
            {
                continue;
            }

            changedPixels++;
            var x = index % Width;
            var y = index / Width;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
            tileRows[y / TileSize] |= 1u << (x / TileSize);
        }

        var changedTileRows = tileRows
            .Select((mask, row) => (mask, row))
            .Where(item => item.mask != 0)
            .Select(item => new ScreenChangedTileRow(item.row, $"0x{item.mask:X8}"))
            .ToArray();
        var changedTiles = tileRows.Sum(BitOperations.PopCount);
        var bounds = changedPixels == 0
            ? null
            : new ScreenChangeBounds(minX, minY, maxX - minX + 1, maxY - minY + 1);

        return new ScreenFrameObservation(
            frameOffset,
            totalFrame,
            Hash(current),
            changedPixels,
            changedTiles,
            bounds,
            changedTileRows);
    }

    public static string Hash(ReadOnlySpan<uint> pixels)
    {
        var rgb = new byte[pixels.Length * 3];
        for (var index = 0; index < pixels.Length; index++)
        {
            var pixel = pixels[index];
            var offset = index * 3;
            rgb[offset] = (byte)(pixel >> 16);
            rgb[offset + 1] = (byte)(pixel >> 8);
            rgb[offset + 2] = (byte)pixel;
        }

        return $"sha256:{Convert.ToHexString(SHA256.HashData(rgb)).ToLowerInvariant()}";
    }
}
