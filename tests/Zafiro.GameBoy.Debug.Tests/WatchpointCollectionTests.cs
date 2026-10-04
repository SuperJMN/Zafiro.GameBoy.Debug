using Zafiro.GameBoy.Debug.Core;

namespace Zafiro.GameBoy.Debug.Tests;

public sealed class WatchpointCollectionTests
{
    [Fact]
    public void Matches_enabled_watchpoints_by_address_and_mode()
    {
        var watchpoints = new WatchpointCollection();
        var read = watchpoints.Set(0xC000, WatchpointMode.Read);
        var write = watchpoints.Set(0xC001, WatchpointMode.Write);
        var access = watchpoints.Set(0xC002, WatchpointMode.Access);

        Assert.True(watchpoints.TryMatch(0xC000, isWrite: false, out var readMatch));
        Assert.Equal(read, readMatch);
        Assert.False(watchpoints.TryMatch(0xC000, isWrite: true, out _));

        Assert.True(watchpoints.TryMatch(0xC001, isWrite: true, out var writeMatch));
        Assert.Equal(write, writeMatch);
        Assert.False(watchpoints.TryMatch(0xC001, isWrite: false, out _));

        Assert.True(watchpoints.TryMatch(0xC002, isWrite: false, out var accessReadMatch));
        Assert.Equal(access, accessReadMatch);
        Assert.True(watchpoints.TryMatch(0xC002, isWrite: true, out var accessWriteMatch));
        Assert.Equal(access, accessWriteMatch);
    }

    [Fact]
    public void Clears_watchpoints_by_id()
    {
        var watchpoints = new WatchpointCollection();
        var first = watchpoints.Set(0xC000, WatchpointMode.Read);
        var second = watchpoints.Set(0xC001, WatchpointMode.Write);

        var cleared = watchpoints.Clear(first.Id);

        Assert.True(cleared);
        Assert.False(watchpoints.TryMatch(0xC000, isWrite: false, out _));
        Assert.True(watchpoints.TryMatch(0xC001, isWrite: true, out var remaining));
        Assert.Equal(second, remaining);
        Assert.Equal("wp-1", first.Id);
        Assert.Equal("wp-2", second.Id);
        Assert.Equal("0xC001", second.Address);
    }

    [Fact]
    public void Clear_all_removes_every_watchpoint()
    {
        var watchpoints = new WatchpointCollection();
        watchpoints.Set(0xC000, WatchpointMode.Read);
        watchpoints.Set(0xC001, WatchpointMode.Write);

        watchpoints.ClearAll();

        Assert.Empty(watchpoints.All);
        Assert.False(watchpoints.TryMatch(0xC000, isWrite: false, out _));
    }
}
