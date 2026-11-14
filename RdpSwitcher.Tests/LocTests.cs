using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using RdpSwitcher.Config;
using RdpSwitcher.Localization;
using Xunit;

namespace RdpSwitcher.Tests
{
    public class LocTests
    {
        [Theory]
        [InlineData("ru")]
        [InlineData("uk")]
        public void Translation_HasExactlyTheEnglishKeys(string language)
        {
            var en = Strings.En.Keys.OrderBy(k => k).ToList();
            var other = Strings.For(language).Keys.OrderBy(k => k).ToList();
            Assert.Equal(en, other);
        }

        [Theory]
        [InlineData("ru")]
        [InlineData("uk")]
        public void Translation_UsesTheSamePlaceholdersAsEnglish(string language)
        {
            foreach (var pair in Strings.En)
            {
                Assert.Equal(Placeholders(pair.Value), Placeholders(Strings.For(language)[pair.Key]));
            }
        }

        [Theory]
        [InlineData("ru", "ru")]
        [InlineData("uk", "uk")]
        [InlineData("en", "en")]
        [InlineData("de", "en")]
        [InlineData("fr", "en")]
        public void Detect_PicksSupportedOrEnglish(string culture, string expected)
        {
            Assert.Equal(expected, Loc.Detect(new CultureInfo(culture + "-" + (culture == "en" ? "US" : culture.ToUpperInvariant()))));
        }

        [Fact]
        public void SetLanguage_SwitchesStringsAndFallsBackToKey()
        {
            try
            {
                Loc.SetLanguage("ru");
                Assert.Equal("Отмена", Loc.T("Common.Cancel"));
                Loc.SetLanguage("uk");
                Assert.Equal("Скасувати", Loc.T("Common.Cancel"));
                Loc.SetLanguage("en");
                Assert.Equal("Cancel", Loc.T("Common.Cancel"));
                Assert.Equal("No.Such.Key", Loc.T("No.Such.Key"));
            }
            finally
            {
                Loc.SetLanguage("en");
            }
        }

        [Fact]
        public void Normalize_DropsUnknownLanguage()
        {
            var config = new AppConfig { Language = "xx" };
            config.Normalize();
            Assert.Null(config.Language);

            config.Language = "uk";
            config.Normalize();
            Assert.Equal("uk", config.Language);
        }

        private static string Placeholders(string text)
        {
            return string.Join(",", Regex.Matches(text, @"\{\d+\}").Cast<Match>().Select(m => m.Value).Distinct().OrderBy(v => v));
        }
    }
}
