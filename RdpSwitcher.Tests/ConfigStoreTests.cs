using System;
using System.IO;
using RdpSwitcher.Config;
using Xunit;

namespace RdpSwitcher.Tests
{
    public class ConfigStoreTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _path;

        public ConfigStoreTests()
        {
            _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RdpSwitcherTests_" + Guid.NewGuid().ToString("N"));
            _path = System.IO.Path.Combine(_dir, "config.json");
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Fact]
        public void Load_MissingFile_CreatesDefaults()
        {
            var store = new ConfigStore(_path);

            var config = store.Load();

            Assert.True(File.Exists(_path));
            Assert.Null(store.LoadError);
            Assert.Equal(PanelEdge.Top, config.Panel.Edge);
            Assert.Equal(44, config.Panel.Size);
            Assert.Empty(config.Connections);
        }

        [Fact]
        public void ReadOnly_MissingFile_ThrowsAndCreatesNothing()
        {
            var store = new ConfigStore(_path, readOnly: true);

            Assert.Throws<FileNotFoundException>(() => store.Load());
            Assert.False(File.Exists(_path));
            Assert.False(Directory.Exists(_dir));
        }

        [Fact]
        public void ReadOnly_CorruptFile_ThrowsAndLeavesFileInPlace()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(_path, "{ this is not json");
            var store = new ConfigStore(_path, readOnly: true);

            Assert.Throws<InvalidDataException>(() => store.Load());
            Assert.Equal("{ this is not json", File.ReadAllText(_path));
            Assert.False(File.Exists(_path + ".bad"));
        }

        [Fact]
        public void ReadOnly_ValidFile_LoadsButRefusesSave()
        {
            new ConfigStore(_path).Load();
            var store = new ConfigStore(_path, readOnly: true);

            var config = store.Load();

            Assert.Equal(44, config.Panel.Size);
            Assert.Throws<InvalidOperationException>(() => store.Save(config));
        }

        [Fact]
        public void Load_CorruptFile_MovesToBadAndUsesDefaults()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(_path, "{ this is not json");
            var store = new ConfigStore(_path);

            var config = store.Load();

            Assert.NotNull(store.LoadError);
            Assert.True(File.Exists(_path + ".bad"));
            Assert.Equal("{ this is not json", File.ReadAllText(_path + ".bad"));
            Assert.Equal(44, config.Panel.Size);
            Assert.True(File.Exists(_path));
        }

        [Fact]
        public void Load_EmptyFile_TreatedAsCorrupt()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(_path, "");
            var store = new ConfigStore(_path);

            store.Load();

            Assert.NotNull(store.LoadError);
            Assert.True(File.Exists(_path + ".bad"));
        }

        [Fact]
        public void SaveLoad_RoundTrip()
        {
            var store = new ConfigStore(_path);
            var config = new AppConfig();
            config.Panel.Edge = PanelEdge.Left;
            config.Panel.Size = 60;
            config.Connections.Add(new ConnectionInfo
            {
                Id = "b4f1c0de-0000-0000-0000-000000000001",
                Name = "Client A",
                Host = "192.168.56.10",
                Port = 3390,
                User = "DOMAIN\\admin",
                Group = "Client A",
                Color = "#4C9AFF",
                AutoConnect = true
            });

            store.Save(config);
            var loaded = new ConfigStore(_path).Load();

            Assert.Equal(PanelEdge.Left, loaded.Panel.Edge);
            Assert.Equal(60, loaded.Panel.Size);
            var c = Assert.Single(loaded.Connections);
            Assert.Equal("b4f1c0de-0000-0000-0000-000000000001", c.Id);
            Assert.Equal(3390, c.Port);
            Assert.Equal("DOMAIN\\admin", c.User);
            Assert.True(c.AutoConnect);
        }

        [Fact]
        public void Save_WritesCamelCaseAndLowercaseEdge()
        {
            var store = new ConfigStore(_path);
            var config = new AppConfig();
            config.Panel.Edge = PanelEdge.Bottom;

            store.Save(config);
            var text = File.ReadAllText(_path);

            Assert.Contains("\"edge\": \"bottom\"", text);
            Assert.Contains("\"showDelayMs\"", text);
            Assert.DoesNotContain("tmp", text);
            Assert.False(File.Exists(_path + ".tmp"));
        }

        [Fact]
        public void Save_OverwritesExistingFile()
        {
            var store = new ConfigStore(_path);
            store.Save(new AppConfig());
            var second = new AppConfig();
            second.Panel.Size = 80;

            store.Save(second);

            Assert.Equal(80, new ConfigStore(_path).Load().Panel.Size);
            Assert.False(File.Exists(_path + ".tmp"));
        }

        [Fact]
        public void Config_HasNoPasswordField()
        {
            Assert.Null(typeof(ConnectionInfo).GetProperty("Password"));
        }

        [Fact]
        public void Normalize_ClampsPanelValues()
        {
            var config = new AppConfig();
            config.Panel.Size = 5;
            config.Panel.TriggerZone = 99;
            config.Panel.ShowDelayMs = -1;
            config.Panel.Opacity = 7;

            config.Normalize();

            Assert.Equal(32, config.Panel.Size);
            Assert.Equal(10, config.Panel.TriggerZone);
            Assert.Equal(0, config.Panel.ShowDelayMs);
            Assert.Equal(1.0, config.Panel.Opacity);

            config.Panel.Size = 500;
            config.Panel.TriggerZone = 0;
            config.Normalize();
            Assert.Equal(120, config.Panel.Size);
            Assert.Equal(1, config.Panel.TriggerZone);
        }

        [Fact]
        public void Normalize_FixesDuplicateAndEmptyIds()
        {
            var config = new AppConfig();
            config.Connections.Add(new ConnectionInfo { Id = "same", Host = "a" });
            config.Connections.Add(new ConnectionInfo { Id = "same", Host = "b" });
            config.Connections.Add(new ConnectionInfo { Id = "", Host = "c" });

            config.Normalize();

            Assert.Equal("same", config.Connections[0].Id);
            Assert.Equal(3, new System.Collections.Generic.HashSet<string>(
                System.Linq.Enumerable.Select(config.Connections, c => c.Id)).Count);
            Assert.All(config.Connections, c => Assert.False(string.IsNullOrWhiteSpace(c.Id)));
        }

        [Fact]
        public void Normalize_FixesPortNameAndMissingSections()
        {
            var config = new AppConfig { Panel = null, Hotkeys = null, Connections = null };
            config.Normalize();
            Assert.NotNull(config.Panel);
            Assert.NotNull(config.Hotkeys);
            Assert.NotNull(config.Connections);

            config.Connections.Add(new ConnectionInfo { Host = "vm1", Port = 0, Name = " " });
            config.Connections.Add(new ConnectionInfo { Host = "vm2", Port = 70000, Name = "Two" });
            config.Normalize();

            Assert.Equal(3389, config.Connections[0].Port);
            Assert.Equal("vm1", config.Connections[0].Name);
            Assert.Equal(3389, config.Connections[1].Port);
            Assert.Equal("Two", config.Connections[1].Name);
        }

        [Fact]
        public void Load_PartialJson_FillsDefaults()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(_path, "{ \"version\": 1, \"panel\": { \"edge\": \"right\" } }");

            var config = new ConfigStore(_path).Load();

            Assert.Equal(PanelEdge.Right, config.Panel.Edge);
            Assert.Equal(44, config.Panel.Size);
            Assert.Equal("Ctrl+Alt+Right", config.Hotkeys.Next);
        }
    }
}
