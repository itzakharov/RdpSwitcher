using System.Drawing;
using RdpSwitcher.Config;
using RdpSwitcher.UI;
using Xunit;

namespace RdpSwitcher.Tests
{
    public class WatermarkLogicTests
    {
        private static readonly Rectangle Area = new Rectangle(100, 50, 1920, 1080);

        [Fact]
        public void ComputeBounds_TopRight()
        {
            var r = WatermarkLogic.ComputeBounds(Area, new Size(200, 40), WatermarkPosition.TopRight, 16);

            Assert.Equal(new Rectangle(100 + 1920 - 16 - 200, 50 + 16, 200, 40), r);
        }

        [Fact]
        public void ComputeBounds_BottomRight()
        {
            var r = WatermarkLogic.ComputeBounds(Area, new Size(200, 40), WatermarkPosition.BottomRight, 16);

            Assert.Equal(new Rectangle(100 + 1920 - 16 - 200, 50 + 1080 - 16 - 40, 200, 40), r);
        }

        [Theory]
        [InlineData(PanelEdge.Top, WatermarkPosition.TopRight, true)]
        [InlineData(PanelEdge.Top, WatermarkPosition.BottomRight, false)]
        [InlineData(PanelEdge.Bottom, WatermarkPosition.BottomRight, true)]
        [InlineData(PanelEdge.Bottom, WatermarkPosition.TopRight, false)]
        [InlineData(PanelEdge.Right, WatermarkPosition.TopRight, true)]
        [InlineData(PanelEdge.Right, WatermarkPosition.BottomRight, true)]
        [InlineData(PanelEdge.Left, WatermarkPosition.TopRight, false)]
        [InlineData(PanelEdge.Left, WatermarkPosition.BottomRight, false)]
        public void OverlapsPanel_ByEdgeAndPosition(PanelEdge edge, WatermarkPosition position, bool expected)
        {
            Assert.Equal(expected, WatermarkLogic.OverlapsPanel(edge, position));
        }

        [Fact]
        public void Normalize_ClampsWatermarkOpacity()
        {
            var config = new AppConfig();
            config.Watermark.Opacity = 5;
            config.Normalize();
            Assert.Equal(WatermarkSettings.MaxOpacity, config.Watermark.Opacity);

            config.Watermark.Opacity = 0;
            config.Normalize();
            Assert.Equal(WatermarkSettings.MinOpacity, config.Watermark.Opacity);
        }
    }
}
