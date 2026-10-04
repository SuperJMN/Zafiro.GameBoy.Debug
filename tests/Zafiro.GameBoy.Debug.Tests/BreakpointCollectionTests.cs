using Zafiro.GameBoy.Debug.Core;
using Zafiro.GameBoy.Debug.SameBoy;

namespace Zafiro.GameBoy.Debug.Tests;

public sealed class BreakpointCollectionTests
{
    [Fact]
    public void Adds_and_clears_breakpoints_by_id()
    {
        var breakpoints = new BreakpointCollection();

        var first = breakpoints.Set(0x0150, null);
        var second = breakpoints.Set(0xC000, "a == 1");

        Assert.Equal("bp-1", first.Id);
        Assert.Equal("0x0150", first.Address);
        Assert.True(first.Enabled);
        Assert.True(breakpoints.Contains(0x0150));
        Assert.True(breakpoints.Contains(0xC000));

        var cleared = breakpoints.Clear(first.Id);

        Assert.True(cleared);
        Assert.False(breakpoints.Contains(0x0150));
        Assert.True(breakpoints.Contains(0xC000));
        Assert.Equal("bp-2", second.Id);
        Assert.NotNull(second.ParsedCondition);
    }

    [Fact]
    public void Finds_all_enabled_breakpoints_at_an_address()
    {
        var breakpoints = new BreakpointCollection();

        var first = breakpoints.Set(0x0150, "A == 1");
        var second = breakpoints.Set(0x0150, "A == 2");
        _ = breakpoints.Set(0xC000, null);

        var matches = breakpoints.FindAll(0x0150);

        Assert.Equal([first, second], matches);
    }

    [Fact]
    public void SameBoy_session_lists_current_breakpoints_without_native_session()
    {
        using var session = new SameBoyDebugSession();

        session.SetBreakpoint(0x0150, null);
        session.SetBreakpoint(0xC000, "a == 1");

        var result = session.ListBreakpoints();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Collection(
            result.Value.Breakpoints,
            first =>
            {
                Assert.Equal("bp-1", first.Id);
                Assert.Equal("0x0150", first.Address);
                Assert.True(first.Enabled);
                Assert.Null(first.Condition);
            },
            second =>
            {
                Assert.Equal("bp-2", second.Id);
                Assert.Equal("0xC000", second.Address);
                Assert.True(second.Enabled);
                Assert.Equal("a == 1", second.Condition);
            });
    }
}
