using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RdpSwitcher.Localization
{
    /// <summary>
    /// UI strings by key. English is the fallback for a missing translation; a missing key shows the key itself.
    /// Not thread-safe by design: language is set at start and from the UI thread.
    /// </summary>
    public static class Loc
    {
        public const string Fallback = "en";

        /// <summary>Language codes in the order they are offered in the settings dialog.</summary>
        public static readonly IReadOnlyList<string> Supported = new[] { "en", "ru", "uk" };

        private static Dictionary<string, string> _current = Strings.En;

        public static string Language { get; private set; } = Fallback;

        public static bool IsSupported(string code)
        {
            return code != null && Supported.Contains(code);
        }

        /// <summary>Language of the given UI culture if supported, otherwise English.</summary>
        public static string Detect(CultureInfo culture)
        {
            var code = culture?.TwoLetterISOLanguageName;
            return IsSupported(code) ? code : Fallback;
        }

        /// <summary>Unsupported or null code falls back to the system language.</summary>
        public static void SetLanguage(string code)
        {
            Language = IsSupported(code) ? code : Detect(CultureInfo.CurrentUICulture);
            _current = Strings.For(Language);
        }

        /// <summary>Name of a language in that language itself.</summary>
        public static string NativeName(string code)
        {
            switch (code)
            {
                case "ru": return "Русский";
                case "uk": return "Українська";
                default: return "English";
            }
        }

        public static string T(string key)
        {
            string value;
            if (_current.TryGetValue(key, out value)) return value;
            return Strings.En.TryGetValue(key, out value) ? value : key;
        }

        public static string T(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, T(key), args);
        }
    }
}
