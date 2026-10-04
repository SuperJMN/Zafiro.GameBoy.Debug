namespace Zafiro.GameBoy.Debug.Core;

public sealed record WatchpointInfo(string Id, string Address, ushort AddressValue, int Length, WatchpointMode Mode, bool Enabled)
{
    public int EndAddressValue => AddressValue + Length - 1;
}
