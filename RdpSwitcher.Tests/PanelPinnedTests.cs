using System;
using System.IO;
using RdpSwitcher.Config;
using Xunit;

namespace RdpSwitcher.Tests
{
    public class PanelPinnedTests : IDisposable
    {
        private readonly string _dir =
            Path.Combine(Path.GetTempPath(), "RdpSwitcherTests_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Fact]
        public void Pinned_DefaultsToFalse_SoOldConfigFilesStillLoad()
        {
            Assert.False(new PanelSettings().Pinned);

            Directory.CreateDirectory(_dir);
            var path = Path.Combine(_dir, "config.json");
            File.WriteAllText(path, "{ \"panel\": { \"edge\": \"left\", \"size\": 50 } }");

            var config = new ConfigStore(path).Load();

            Assert.Equal(PanelEdge.Left, config.Panel.Edge);
            Assert.False(config.Panel.Pinned);
        }

        [Fact]
        public void Pinned_RoundTrips()
        {
            var path = Path.Combine(_dir, "config.json");
            var store = new ConfigStore(path);
            var config = store.Load();
            config.Panel.Pinned = true;
            store.Save(config);

            Assert.True(new ConfigStore(path).Load().Panel.Pinned);
        }
    }
}
