using System.Globalization;

namespace Zafiro.GameBoy.Debug.Core;

public sealed record GameBoyAddress(ushort Address, int? Bank = null, string? AddressSpace = null)
{
    public static DebugResult<GameBoyAddress> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Invalid(text);
        }

        var trimmed = text.Trim();
        var parts = trimmed.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length > 2)
        {
            return Invalid(text);
        }

        int? bank = null;
        var addressText = trimmed;
        if (parts.Length == 2)
        {
            if (!TryParseHex(parts[0], 0xFF, out var parsedBank))
            {
                return Invalid(text);
            }

            bank = parsedBank;
            addressText = parts[1];
        }

        if (!TryParseHex(addressText, 0xFFFF, out var address))
        {
            return Invalid(text);
        }

        return DebugResult<GameBoyAddress>.Success(new GameBoyAddress((ushort)address, bank));
    }

    public override string ToString()
    {
        return Bank is int bank
            ? $"{bank:X2}:{Address:X4}"
            : Hex.FormatWord(Address);
    }

    private static bool TryParseHex(string text, int maxValue, out int value)
    {
        value = 0;
        var normalized = text.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }
        else if (normalized.StartsWith('$'))
        {
            normalized = normalized[1..];
        }

        return normalized.Length > 0
            && int.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
            && value <= maxValue;
    }

    private static DebugResult<GameBoyAddress> Invalid(string? text)
    {
        return DebugResult<GameBoyAddress>.Failure("invalid_address", $"'{text}' is not a valid Game Boy address.");
    }
}
