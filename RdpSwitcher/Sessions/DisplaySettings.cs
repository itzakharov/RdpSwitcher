using System;
using System.Drawing;

namespace RdpSwitcher.Sessions
{
    /// <summary>
    /// Remote desktop geometry for one monitor, computed from its size in physical pixels and its DPI.
    /// Pure logic (no WinForms/COM) so it can be unit-tested.
    /// </summary>
    public sealed class DisplaySettings : IEquatable<DisplaySettings>
    {
        public const int MinDimension = 200;
        public const int MaxDimension = 8192;
        public const int MinScale = 100;
        public const int MaxScale = 500;

        private DisplaySettings() { }

        /// <summary>Remote desktop size in physical pixels (width is even).</summary>
        public Size Size { get; private set; }

        /// <summary>DesktopScaleFactor, percent (100..500): how large the remote UI is drawn.</summary>
        public int DesktopScaleFactor { get; private set; }

        /// <summary>DeviceScaleFactor: one of 100, 140, 180 (the only values the protocol accepts).</summary>
        public int DeviceScaleFactor { get; private set; }

        /// <summary>Monitor size in millimetres, derived from the pixel size and DPI.</summary>
        public Size PhysicalSizeMm { get; private set; }

        /// <param name="monitorPixels">Monitor (or form) size in physical pixels.</param>
        /// <param name="dpi">Effective DPI of that monitor; 96 = 100%. Non-positive means 96.</param>
        public static DisplaySettings ForMonitor(Size monitorPixels, int dpi)
        {
            if (dpi <= 0) dpi = 96;

            var scale = (int)Math.Round(dpi * 100.0 / 96.0, MidpointRounding.AwayFromZero);
            scale = Math.Max(MinScale, Math.Min(MaxScale, scale));

            var width = Clamp(monitorPixels.Width & ~1);
            var height = Clamp(monitorPixels.Height);

            return new DisplaySettings
            {
                Size = new Size(width, height),
                DesktopScaleFactor = scale,
                DeviceScaleFactor = scale >= 180 ? 180 : scale >= 140 ? 140 : 100,
                PhysicalSizeMm = new Size(ToMm(width, dpi), ToMm(height, dpi))
            };
        }

        public bool Equals(DisplaySettings other)
        {
            return other != null
                && Size == other.Size
                && DesktopScaleFactor == other.DesktopScaleFactor
                && DeviceScaleFactor == other.DeviceScaleFactor;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as DisplaySettings);
        }

        public override int GetHashCode()
        {
            return Size.GetHashCode() ^ (DesktopScaleFactor << 16) ^ DeviceScaleFactor;
        }

        private static int Clamp(int value)
        {
            return Math.Max(MinDimension, Math.Min(MaxDimension, value));
        }

        private static int ToMm(int pixels, int dpi)
        {
            return (int)Math.Round(pixels * 25.4 / dpi, MidpointRounding.AwayFromZero);
        }
    }
}
