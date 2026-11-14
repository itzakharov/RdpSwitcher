using System.Drawing;
using RdpSwitcher.Config;

namespace RdpSwitcher.UI
{
    /// <summary>Pure geometry for the session-name watermark (no WinForms controls).</summary>
    public static class WatermarkLogic
    {
        /// <summary>Label rectangle in the right-hand corner of <paramref name="area"/>, <paramref name="margin"/> px from the edges.</summary>
        public static Rectangle ComputeBounds(Rectangle area, Size size, WatermarkPosition position, int margin)
        {
            var x = area.Right - margin - size.Width;
            var y = position == WatermarkPosition.TopRight
                ? area.Top + margin
                : area.Bottom - margin - size.Height;
            return new Rectangle(new Point(x, y), size);
        }

        /// <summary>
        /// True when a visible edge panel would sit under the label (the label is a separate window above the panel).
        /// A panel on the right edge spans the whole height, so it always overlaps.
        /// </summary>
        public static bool OverlapsPanel(PanelEdge edge, WatermarkPosition position)
        {
            switch (edge)
            {
                case PanelEdge.Top: return position == WatermarkPosition.TopRight;
                case PanelEdge.Bottom: return position == WatermarkPosition.BottomRight;
                case PanelEdge.Right: return true;
                default: return false;
            }
        }
    }
}
