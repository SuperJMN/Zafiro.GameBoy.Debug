using Zafiro.GameBoy.Debug.Core;

namespace Zafiro.GameBoy.Debug.Tests;

public sealed class HexFormattingTests
{
    [Fact]
    public void Formats_bytes_and_words_as_uppercase_hex()
    {
        Assert.Equal("0x2A", Hex.FormatByte(0x2A));
        Assert.Equal("0x0150", Hex.FormatWord(0x0150));
    }

    [Fact]
    public void Formats_byte_blocks_with_spaces()
    {
        byte[] bytes = [0x00, 0x01, 0x2A, 0xFF];

        Assert.Equal("00 01 2A FF", Hex.FormatBytes(bytes));
    }
}
