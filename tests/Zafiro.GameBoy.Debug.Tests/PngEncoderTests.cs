using System.Buffers.Binary;
using System.IO.Compression;
using Zafiro.GameBoy.Debug.Core;

namespace Zafiro.GameBoy.Debug.Tests;

public sealed class PngEncoderTests
{
    [Fact]
    public void Encoded_framebuffer_is_a_decodable_160_by_144_png()
    {
        var pixels = Enumerable.Repeat(0x00112233u, 160 * 144).ToArray();
        pixels[0] = 0x00FF0000;
        pixels[1] = 0x0000FF00;
        pixels[2] = 0x000000FF;

        var png = PngEncoder.EncodeRgb24(pixels, 160, 144);
        var firstScanline = ReadFirstScanline(png);

        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);
        Assert.Equal((160, 144), ReadIhdrSize(png));
        Assert.Equal([0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF], firstScanline[..9]);
    }

    private static (int Width, int Height) ReadIhdrSize(byte[] png)
    {
        Assert.Equal("IHDR", ReadChunkType(png, 8));
        return (
            BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }

    private static byte[] ReadFirstScanline(byte[] png)
    {
        var compressed = new MemoryStream();
        var offset = 8;
        while (offset < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            var type = ReadChunkType(png, offset);
            if (type == "IDAT")
            {
                compressed.Write(png, offset + 8, length);
            }

            if (type == "IEND")
            {
                break;
            }

            offset += 12 + length;
        }

        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        var raw = new byte[(160 * 3 + 1) * 144];
        Assert.Equal(raw.Length, zlib.ReadAtLeast(raw, raw.Length, throwOnEndOfStream: false));
        Assert.Equal(0, raw[0]);
        return raw[1..(1 + 160 * 3)];
    }

    private static string ReadChunkType(byte[] png, int offset) =>
        System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
}
