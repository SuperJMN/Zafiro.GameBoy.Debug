namespace Zafiro.GameBoy.Debug.Core;

public static class ExecutionObserver
{
    public const int MaxFrames = 600;
    public const int MaxMemoryProbes = 16;
    public const int MaxMemoryProbeLength = 64;
    public const int MaxMemoryBytesPerFrame = 256;
    public const int MaxVideoEvents = 2_000;

    private static readonly (int Start, int End)[] SafeProbeRanges =
    [
        (0x8000, 0x9FFF), // VRAM
        (0xA000, 0xBFFF), // cartridge RAM
        (0xC000, 0xDFFF), // work RAM
        (0xE000, 0xFDFF), // work RAM echo
        (0xFE00, 0xFE9F), // OAM
        (0xFF80, 0xFFFE), // high RAM
    ];

    public static ExecutionObservationAppliedLimits AppliedLimits { get; } = new(
        MaxFrames,
        MaxMemoryProbes,
        MaxMemoryProbeLength,
        MaxMemoryBytesPerFrame,
        MaxVideoEvents);

    public static bool IsSafeProbe(MemoryProbe probe)
    {
        if (probe.Length < 1 || probe.Address + probe.Length > 0x10000)
        {
            return false;
        }

        var end = probe.Address + probe.Length - 1;
        return SafeProbeRanges.Any(range => probe.Address >= range.Start && end <= range.End);
    }
}
