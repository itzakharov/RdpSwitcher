using System.Drawing;
using RdpSwitcher.Sessions;
using Xunit;

namespace RdpSwitcher.Tests
{
    public class DisplaySettingsTests
    {
        [Theory]
        [InlineData(96, 100, 100)]
        [InlineData(120, 125, 100)]
        [InlineData(144, 150, 140)]
        [InlineData(168, 175, 140)]
        [InlineData(192, 200, 180)]
        [InlineData(384, 400, 180)]
        public void ForMonitor_MapsDpiToScaleFactors(int dpi, int desktopScale, int deviceScale)
        {
            var settings = DisplaySettings.ForMonitor(new Size(2560, 1440), dpi);

            Assert.Equal(desktopScale, settings.DesktopScaleFactor);
            Assert.Equal(deviceScale, settings.DeviceScaleFactor);
        }

        [Theory]
        [InlineData(96)]
        [InlineData(120)]
        [InlineData(144)]
        public void ForMonitor_KeepsPhysicalPixelSize(int dpi)
        {
            var settings = DisplaySettings.ForMonitor(new Size(1920, 1080), dpi);

            Assert.Equal(new Size(1920, 1080), settings.Size);
        }

        [Fact]
        public void ForMonitor_NonPositiveDpiMeansHundredPercent()
        {
            var settings = DisplaySettings.ForMonitor(new Size(1920, 1080), 0);

            Assert.Equal(100, settings.DesktopScaleFactor);
            Assert.Equal(100, settings.DeviceScaleFactor);
        }

        [Fact]
        public void ForMonitor_ScaleIsClamped()
        {
            Assert.Equal(100, DisplaySettings.ForMonitor(new Size(1920, 1080), 48).DesktopScaleFactor);
            Assert.Equal(500, DisplaySettings.ForMonitor(new Size(1920, 1080), 960).DesktopScaleFactor);
        }

        [Fact]
        public void ForMonitor_WidthIsMadeEven()
        {
            Assert.Equal(1920, DisplaySettings.ForMonitor(new Size(1921, 1080), 96).Size.Width);
        }

        [Fact]
        public void ForMonitor_SizeIsClampedToProtocolLimits()
        {
            Assert.Equal(new Size(200, 200), DisplaySettings.ForMonitor(new Size(100, 50), 96).Size);
            Assert.Equal(new Size(8192, 8192), DisplaySettings.ForMonitor(new Size(10000, 9000), 96).Size);
        }

        [Fact]
        public void ForMonitor_ComputesPhysicalSizeInMillimetres()
        {
            var settings = DisplaySettings.ForMonitor(new Size(1920, 1080), 96);

            Assert.Equal(new Size(508, 286), settings.PhysicalSizeMm);
        }

        [Fact]
        public void Equals_ComparesSizeAndScales()
        {
            var a = DisplaySettings.ForMonitor(new Size(1920, 1080), 120);
            var b = DisplaySettings.ForMonitor(new Size(1920, 1080), 120);
            var c = DisplaySettings.ForMonitor(new Size(1920, 1080), 144);

            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
        }
    }
}
