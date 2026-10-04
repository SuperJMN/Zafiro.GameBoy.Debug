namespace Zafiro.GameBoy.Debug.Core;

/// <summary>
/// Internal debugger primitive for copying the exact rendered 160x144 RGB frame
/// without encoding an image or reducing CGB colors to DMG shades.
/// </summary>
public interface IRgbFrameSource
{
    DebugResult<int> CopyRgbFrame(Memory<uint> destination);
}
