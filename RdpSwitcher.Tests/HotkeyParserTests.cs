using RdpSwitcher.Native;
using Xunit;

namespace RdpSwitcher.Tests
{
    public class HotkeyParserTests
    {
        [Fact]
        public void Parse_CtrlAltRight()
        {
            Hotkey hk;
            string error;
            Assert.True(HotkeyParser.TryParse("Ctrl+Alt+Right", out hk, out error));
            Assert.Equal(HotkeyParser.ModControl | HotkeyParser.ModAlt, hk.Modifiers);
            Assert.Equal(0x27u, hk.VirtualKey);
        }

        [Theory]
        [InlineData("ctrl+alt+home", 0x24u)]
        [InlineData(" Ctrl + Alt + End ", 0x23u)]
        [InlineData("Ctrl+Alt+a", (uint)'A')]
        [InlineData("Ctrl+Alt+7", (uint)'7')]
        [InlineData("Ctrl+Alt+F1", 0x70u)]
        [InlineData("Ctrl+Alt+f12", 0x7Bu)]
        [InlineData("Ctrl+Alt+PageDown", 0x22u)]
        public void Parse_Keys(string text, uint vk)
        {
            Hotkey hk;
            string error;
            Assert.True(HotkeyParser.TryParse(text, out hk, out error), error);
            Assert.Equal(vk, hk.VirtualKey);
        }

        [Fact]
        public void Parse_AllModifiers()
        {
            Hotkey hk;
            string error;
            Assert.True(HotkeyParser.TryParse("Control+Shift+Alt+Win+Z", out hk, out error));
            Assert.Equal(HotkeyParser.ModControl | HotkeyParser.ModShift | HotkeyParser.ModAlt | HotkeyParser.ModWin, hk.Modifiers);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("Right")]
        [InlineData("Ctrl+Alt")]
        [InlineData("Ctrl+Ctrl+Right")]
        [InlineData("Ctrl++Right")]
        [InlineData("Foo+Right")]
        [InlineData("Ctrl+Alt+Banana")]
        [InlineData("Ctrl+Alt+F25")]
        [InlineData("Ctrl+Alt+F0")]
        public void Parse_Invalid(string text)
        {
            Hotkey hk;
            string error;
            Assert.False(HotkeyParser.TryParse(text, out hk, out error));
            Assert.Null(hk);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void ParseModifiers_CtrlAlt()
        {
            uint mods;
            string error;
            Assert.True(HotkeyParser.TryParseModifiers("Ctrl+Alt", out mods, out error));
            Assert.Equal(HotkeyParser.ModControl | HotkeyParser.ModAlt, mods);
        }

        [Theory]
        [InlineData("")]
        [InlineData("Ctrl+Right")]
        [InlineData("Ctrl+")]
        public void ParseModifiers_Invalid(string text)
        {
            uint mods;
            string error;
            Assert.False(HotkeyParser.TryParseModifiers(text, out mods, out error));
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void Jump_UsesDigitKeys()
        {
            var hk = HotkeyParser.Jump(HotkeyParser.ModControl | HotkeyParser.ModAlt, 1);
            Assert.Equal((uint)'1', hk.VirtualKey);
            Assert.Equal((uint)'9', HotkeyParser.Jump(HotkeyParser.ModControl, 9).VirtualKey);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(10)]
        public void Jump_OutOfRange_Throws(int n)
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => HotkeyParser.Jump(HotkeyParser.ModControl, n));
        }
    }
}
