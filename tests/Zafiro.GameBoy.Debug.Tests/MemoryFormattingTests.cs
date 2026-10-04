using Zafiro.GameBoy.Debug.Core;

namespace Zafiro.GameBoy.Debug.Tests;

public sealed class MemoryFormattingTests
{
    [Fact]
    public void Formats_memory_as_hex_bytes_and_ascii()
    {
        byte[] bytes = [0x41, 0x42, 0x00, 0x7E, 0xFF];

        var block = MemoryFormatter.Format(0xC000, bytes);

        Assert.Equal("0xC000", block.Address);
        Assert.Equal("41 42 00 7E FF", block.BytesHex);
        Assert.Equal([0x41, 0x42, 0x00, 0x7E, 0xFF], block.Bytes);
        Assert.Equal("AB.~.", block.Ascii);
    }
}
