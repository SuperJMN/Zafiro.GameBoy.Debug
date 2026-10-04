using Zafiro.GameBoy.Debug.Core;
using Zafiro.GameBoy.Debug.Symbols;

namespace Zafiro.GameBoy.Debug.Tests;

public sealed class SymbolServiceTests
{
    [Fact]
    public void Loads_rgbds_style_symbols()
    {
        var path = CreateTestFilePath("symbols", ".sym");
        File.WriteAllLines(path,
        [
            "; comment",
            "00:0150 Start",
            "02:4000 Banked.Function",
            "C120 Player.X",
        ]);

        try
        {
            var service = new SymbolService();

            var loaded = service.Load(path);
            var start = service.Resolve("Start");
            var playerX = service.Resolve("Player.X");

            Assert.True(loaded.IsSuccess);
            Assert.Equal(3, loaded.Value);
            Assert.True(start.IsSuccess);
            Assert.Equal(0, start.Value.Bank);
            Assert.Equal(0x0150, start.Value.Address);
            Assert.True(playerX.IsSuccess);
            Assert.Null(playerX.Value.Bank);
            Assert.Equal(0xC120, playerX.Value.Address);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Reports_missing_symbols()
    {
        var service = new SymbolService();

        var result = service.Resolve("Missing.Symbol");

        Assert.False(result.IsSuccess);
        Assert.Equal("symbol_not_found", result.Error?.Code);
    }

    private static string CreateTestFilePath(string prefix, string extension)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Zafiro.GameBoy.Debug.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? Directory.GetCurrentDirectory();
        var testDirectory = Path.Combine(root, "artifacts", "tests");
        Directory.CreateDirectory(testDirectory);
        return Path.Combine(testDirectory, $"{prefix}-{Guid.NewGuid():N}{extension}");
    }
}
