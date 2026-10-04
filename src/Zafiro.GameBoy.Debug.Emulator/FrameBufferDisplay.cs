using System;
using System.Threading;
using CoreBoy.gpu;

namespace Zafiro.GameBoy.Debug.Emulator
{
    /// <summary>
    /// Headless <see cref="IDisplay"/> that accumulates emitted pixels into a 160x144
    /// framebuffer of 0xRRGGBB integers. No UI, no external dependencies. The buffer can be
    /// read at any time to capture the current screen.
    /// </summary>
    internal sealed class FrameBufferDisplay : IDisplay
    {
        public const int Width = 160;
        public const int Height = 144;

        // DMG shades, matching CoreBoy's reference palette (lightest -> darkest).
        private static readonly int[] DmgColors = { 0xe6f8da, 0x99c886, 0x437969, 0x051f2a };

        private readonly int[] _rgb = new int[Width * Height];
        private readonly int[] _frame = new int[Width * Height];
        private readonly byte[] _shade = new byte[Width * Height];
        private readonly byte[] _shadeFrame = new byte[Width * Height];
        private readonly object _sync = new object();
        private int _index;

        public bool Enabled { get; set; }

        public event FrameProducedEventHandler OnFrameProduced;

        public void PutDmgPixel(int color)
        {
            var shade = color & 0x03;
            _rgb[_index] = DmgColors[shade];
            _shade[_index] = (byte)shade;
            _index = (_index + 1) % _rgb.Length;
        }

        public void PutColorPixel(int gbcRgb)
        {
            var rgb = TranslateGbcRgb(gbcRgb);
            _rgb[_index] = rgb;
            _shade[_index] = ApproximateDmgShade(rgb);
            _index = (_index + 1) % _rgb.Length;
        }

        public void RequestRefresh()
        {
            lock (_sync)
            {
                Array.Copy(_rgb, _frame, _rgb.Length);
                Array.Copy(_shade, _shadeFrame, _shade.Length);
                _index = 0;
            }

            OnFrameProduced?.Invoke(this, Array.Empty<byte>());
        }

        public void WaitForRefresh()
        {
        }

        public void Run(CancellationToken token)
        {
        }

        /// <summary>Returns a snapshot of the most recently completed frame as 0xRRGGBB pixels.</summary>
        public uint[] Snapshot()
        {
            var copy = new uint[_frame.Length];
            lock (_sync)
            {
                for (var i = 0; i < _frame.Length; i++)
                {
                    copy[i] = (uint)(_frame[i] & 0xFFFFFF);
                }
            }

            return copy;
        }

        public byte[] SnapshotDmgShades()
        {
            var copy = new byte[_shadeFrame.Length];
            lock (_sync)
            {
                Array.Copy(_shadeFrame, copy, _shadeFrame.Length);
            }

            return copy;
        }

        private static int TranslateGbcRgb(int gbcRgb)
        {
            var r = (gbcRgb >> 0) & 0x1f;
            var g = (gbcRgb >> 5) & 0x1f;
            var b = (gbcRgb >> 10) & 0x1f;
            return ((r * 8) << 16) | ((g * 8) << 8) | (b * 8);
        }

        private static byte ApproximateDmgShade(int rgb)
        {
            var r = (rgb >> 16) & 0xFF;
            var g = (rgb >> 8) & 0xFF;
            var b = rgb & 0xFF;
            var luma = (r * 299 + g * 587 + b * 114) / 1000;
            return luma switch
            {
                >= 192 => 0,
                >= 128 => 1,
                >= 64 => 2,
                _ => 3,
            };
        }
    }
}
