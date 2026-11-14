using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace RdpSwitcher.Config
{
    /// <summary>Loads and saves config.json. Atomic writes; a corrupt file is moved to *.bad.</summary>
    public class ConfigStore
    {
        private static readonly JsonSerializerSettings JsonSettings = CreateSettings();

        public string Path { get; }

        /// <summary>Set by Load() when the file was corrupt and defaults were used; null otherwise.</summary>
        public string LoadError { get; private set; }

        /// <summary>
        /// True for a secondary window process: Load() never creates or moves files (a missing or corrupt
        /// file throws instead) and Save() is refused. Repairing the config is the primary process's job.
        /// </summary>
        public bool ReadOnly { get; }

        public ConfigStore(string path = null, bool readOnly = false)
        {
            Path = path ?? DefaultPath();
            ReadOnly = readOnly;
        }

        public static string DefaultPath()
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RdpSwitcher", "config.json");
        }

        public AppConfig Load()
        {
            LoadError = null;

            if (!File.Exists(Path))
            {
                if (ReadOnly) throw new FileNotFoundException("Config file not found: " + Path, Path);

                var fresh = new AppConfig();
                fresh.Normalize();
                Save(fresh);
                return fresh;
            }

            try
            {
                var json = File.ReadAllText(Path, Encoding.UTF8);
                var config = JsonConvert.DeserializeObject<AppConfig>(json, JsonSettings);
                if (config == null) throw new JsonException("Config file is empty.");
                config.Normalize();
                return config;
            }
            catch (JsonException ex)
            {
                if (ReadOnly) throw new InvalidDataException("Config is corrupt (" + ex.Message + ").", ex);
                return RecoverFromCorrupt(ex.Message);
            }
        }

        public void Save(AppConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (ReadOnly) throw new InvalidOperationException("The config store is read-only.");

            var dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(config, JsonSettings), new UTF8Encoding(false));

            if (File.Exists(Path)) File.Replace(tmp, Path, null);
            else File.Move(tmp, Path);
        }

        private AppConfig RecoverFromCorrupt(string reason)
        {
            var bad = Path + ".bad";
            try
            {
                if (File.Exists(bad)) File.Delete(bad);
                File.Move(Path, bad);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            LoadError = "Config is corrupt (" + reason + "). The file was saved as " + bad + "; defaults are used.";

            var config = new AppConfig();
            config.Normalize();
            Save(config);
            return config;
        }

        private static JsonSerializerSettings CreateSettings()
        {
            return new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) }
            };
        }
    }
}
