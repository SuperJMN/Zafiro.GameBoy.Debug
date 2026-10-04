namespace Zafiro.GameBoy.Debug.Core;

public static class MemoryFormatter
{
    public static MemoryReadResult Format(ushort address, IReadOnlyCollection<byte> bytes)
    {
        var array = bytes.ToArray();
        return new MemoryReadResult(
            Hex.FormatWord(address),
            Hex.FormatBytes(array),
            array,
            new string(array.Select(ToAscii).ToArray()));
    }

    private static char ToAscii(byte value) => value is >= 0x20 and <= 0x7E ? (char)value : '.';
}
