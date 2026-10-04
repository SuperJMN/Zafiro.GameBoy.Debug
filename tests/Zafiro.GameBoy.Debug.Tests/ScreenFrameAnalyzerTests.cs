using Zafiro.GameBoy.Debug.Core;

namespace Zafiro.GameBoy.Debug.Tests;

public sealed class ScreenFrameAnalyzerTests
{
    [Fact]
    public void Compare_detects_exact_rgb_changes_and_reports_compact_tile_rows()
    {
        var previous = new uint[ScreenFrameAnalyzer.PixelCount];
        var current = new uint[ScreenFrameAnalyzer.PixelCount];
        current[0] = 0x102030;
        current[8 * ScreenFrameAnalyzer.Width + 159] = 0x405060;

        var observation = ScreenFrameAnalyzer.Compare(previous, current, frameOffset: 3, totalFrame: 42);

        Assert.Equal(3, observation.FrameOffset);
        Assert.Equal((ulong)42, observation.TotalFrame);
        Assert.StartsWith("sha256:", observation.Hash, StringComparison.Ordinal);
        Assert.Equal(2, observation.ChangedPixels);
        Assert.Equal(2, observation.ChangedTiles);
        Assert.Equal(new ScreenChangeBounds(0, 0, 160, 9), observation.ChangedBounds);
        Assert.Collection(
            observation.ChangedTileRows,
            row => Assert.Equal(new ScreenChangedTileRow(0, "0x00000001"), row),
            row => Assert.Equal(new ScreenChangedTileRow(1, "0x00080000"), row));
    }

    [Fact]
    public void Hash_distinguishes_cgb_colors_that_collapse_to_the_same_dmg_shade()
    {
        var first = new uint[ScreenFrameAnalyzer.PixelCount];
        var second = new uint[ScreenFrameAnalyzer.PixelCount];
        first[0] = 0xC00000;
        second[0] = 0x00A000;

        Assert.NotEqual(ScreenFrameAnalyzer.Hash(first), ScreenFrameAnalyzer.Hash(second));
    }
}
