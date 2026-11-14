using System;
using System.Drawing;
using System.IO;
using RdpSwitcher.Config;
using RdpSwitcher.UI;
using Xunit;

namespace RdpSwitcher.Tests
{
    public class PinModeTests : IDisposable
    {
        private static readonly Rectangle Area = new Rectangle(100, 50, 1920, 1080);

        private readonly string _dir =
            Path.Combine(Path.GetTempPath(), "RdpSwitcherTests_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Theory]
        [InlineData(PanelEdge.Top, 100, 94, 1920, 1036)]
        [InlineData(PanelEdge.Bottom, 100, 50, 1920, 1036)]
        [InlineData(PanelEdge.Left, 144, 50, 1876, 1080)]
        [InlineData(PanelEdge.Right, 100, 50, 1876, 1080)]
        public void ComputeRemaining_CutsTheStripOffTheEdge(PanelEdge edge, int x, int y, int w, int h)
        {
            Assert.Equal(new Rectangle(x, y, w, h), EdgePanelLogic.ComputeRemaining(Area, edge, 44));
        }

        [Theory]
        [InlineData(PanelEdge.Top)]
        [InlineData(PanelEdge.Bottom)]
        [InlineData(PanelEdge.Left)]
        [InlineData(PanelEdge.Right)]
        public void ComputeRemaining_StripAndRemainderCoverTheArea(PanelEdge edge)
        {
            var strip = EdgePanelLogic.ComputeBounds(Area, edge, 44);
            var rest = EdgePanelLogic.ComputeRemaining(Area, edge, 44);

            Assert.Equal(Area.Width * Area.Height, strip.Width * strip.Height + rest.Width * rest.Height);
            Assert.False(strip.IntersectsWith(rest));
            Assert.True(Area.Contains(rest));
        }

        [Fact]
        public void ComputeRemaining_ThickerThanArea_NeverGoesNegative()
        {
            var rest = EdgePanelLogic.ComputeRemaining(new Rectangle(0, 0, 30, 20), PanelEdge.Top, 120);
            Assert.Equal(0, rest.Height);
            Assert.Equal(30, rest.Width);
        }

        [Fact]
        public void PinMode_DefaultsToOverlay_AndOldConfigsLoad()
        {
            Assert.Equal(PinMode.Overlay, new PanelSettings().PinMode);

            Directory.CreateDirectory(_dir);
            var path = Path.Combine(_dir, "config.json");
            File.WriteAllText(path, "{ \"panel\": { \"edge\": \"top\" } }");

            Assert.Equal(PinMode.Overlay, new ConfigStore(path).Load().Panel.PinMode);
        }

        [Fact]
        public void PinMode_RoundTripsAsCamelCaseString()
        {
            var path = Path.Combine(_dir, "config.json");
            var store = new ConfigStore(path);
            var config = store.Load();
            config.Panel.PinMode = PinMode.Reserve;
            store.Save(config);

            Assert.Contains("\"pinMode\": \"reserve\"", File.ReadAllText(path));
            Assert.Equal(PinMode.Reserve, new ConfigStore(path).Load().Panel.PinMode);
        }

        [Fact]
        public void Normalize_ResetsOutOfRangeNumber()
        {
            var config = new AppConfig();
            config.Panel.PinMode = (PinMode)42;

            config.Normalize();

            Assert.Equal(PinMode.Overlay, config.Panel.PinMode);
        }
    }
}
