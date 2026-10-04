using Zafiro.GameBoy.Debug.Core;

namespace Zafiro.GameBoy.Debug.Symbols;

public sealed record SymbolInfo(string Name, ushort Address, int? Bank)
{
    public GameBoyAddress ToAddress() => new(Address, Bank);
}
