using Zafiro.GameBoy.Debug.Core;

namespace Zafiro.GameBoy.Debug.Tests;

public sealed class AddressParsingTests
{
    [Theory]
    [InlineData("0x0150", 0x0150)]
    [InlineData("$C000", 0xC000)]
    [InlineData("FF80", 0xFF80)]
    public void Parses_16_bit_cpu_addresses(string text, int expected)
    {
        var parsed = GameBoyAddress.Parse(text);

        Assert.True(parsed.IsSuccess);
        Assert.Equal((ushort)expected, parsed.Value.Address);
        Assert.Null(parsed.Value.Bank);
    }

    [Fact]
    public void Parses_banked_addresses()
    {
        var parsed = GameBoyAddress.Parse("02:4000");

        Assert.True(parsed.IsSuccess);
        Assert.Equal(2, parsed.Value.Bank);
        Assert.Equal(0x4000, parsed.Value.Address);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0x10000")]
    [InlineData("zzzz")]
    [InlineData("123:4000")]
    public void Rejects_invalid_addresses(string text)
    {
        var parsed = GameBoyAddress.Parse(text);

        Assert.False(parsed.IsSuccess);
        Assert.NotNull(parsed.Error);
        Assert.Equal("invalid_address", parsed.Error.Code);
    }
}
