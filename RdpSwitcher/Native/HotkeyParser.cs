using System;
using System.Collections.Generic;
using System.Globalization;
using RdpSwitcher.Localization;

namespace RdpSwitcher.Native
{
    /// <summary>A parsed hotkey: RegisterHotKey modifier flags plus a virtual-key code.</summary>
    public sealed class Hotkey
    {
        public Hotkey(uint modifiers, uint virtualKey)
        {
            Modifiers = modifiers;
            VirtualKey = virtualKey;
        }

        /// <summary>HotkeyParser.ModAlt / ModControl / ModShift / ModWin flags.</summary>
        public uint Modifiers { get; }

        public uint VirtualKey { get; }
    }

    /// <summary>Pure text parsing of hotkeys such as "Ctrl+Alt+Right"; no WinForms or Win32 calls.</summary>
    public static class HotkeyParser
    {
        public const uint ModAlt = 0x1;
        public const uint ModControl = 0x2;
        public const uint ModShift = 0x4;
        public const uint ModWin = 0x8;

        private static readonly Dictionary<string, uint> NamedKeys =
            new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                { "Left", 0x25 }, { "Up", 0x26 }, { "Right", 0x27 }, { "Down", 0x28 },
                { "Home", 0x24 }, { "End", 0x23 },
                { "PageUp", 0x21 }, { "Prior", 0x21 }, { "PageDown", 0x22 }, { "Next", 0x22 },
                { "Insert", 0x2D }, { "Delete", 0x2E }, { "Tab", 0x09 }, { "Space", 0x20 },
                { "Enter", 0x0D }, { "Return", 0x0D }, { "Escape", 0x1B }, { "Esc", 0x1B },
                { "Backspace", 0x08 }
            };

        /// <summary>Parses "Mod+Mod+Key". At least one modifier is required (a bare global key would steal typing).</summary>
        public static bool TryParse(string text, out Hotkey hotkey, out string error)
        {
            hotkey = null;
            string[] parts;
            if (!Split(text, out parts, out error)) return false;

            if (parts.Length < 2)
            {
                error = Loc.T("Hotkey.NeedModifier");
                return false;
            }

            uint mods = 0;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (!AddModifier(parts[i], ref mods, out error)) return false;
            }

            uint vk;
            if (!TryParseKey(parts[parts.Length - 1], out vk))
            {
                error = Loc.T("Hotkey.UnknownKey", parts[parts.Length - 1]);
                return false;
            }

            hotkey = new Hotkey(mods, vk);
            return true;
        }

        /// <summary>Parses a modifiers-only string such as "Ctrl+Alt" (used for jumpModifier).</summary>
        public static bool TryParseModifiers(string text, out uint modifiers, out string error)
        {
            modifiers = 0;
            string[] parts;
            if (!Split(text, out parts, out error)) return false;

            foreach (var part in parts)
            {
                if (!AddModifier(part, ref modifiers, out error)) return false;
            }
            return true;
        }

        /// <summary>Hotkey for jumping to session number 1..9 with the given modifiers.</summary>
        public static Hotkey Jump(uint modifiers, int number)
        {
            if (number < 1 || number > 9) throw new ArgumentOutOfRangeException(nameof(number));
            return new Hotkey(modifiers, (uint)('0' + number));
        }

        private static bool Split(string text, out string[] parts, out string error)
        {
            parts = null;
            error = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = Loc.T("Hotkey.Empty");
                return false;
            }

            parts = text.Split('+');
            for (var i = 0; i < parts.Length; i++)
            {
                parts[i] = parts[i].Trim();
                if (parts[i].Length == 0)
                {
                    error = Loc.T("Hotkey.EmptyPart", text);
                    return false;
                }
            }
            return true;
        }

        private static bool AddModifier(string token, ref uint mods, out string error)
        {
            error = null;
            uint flag;
            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control": flag = ModControl; break;
                case "alt": flag = ModAlt; break;
                case "shift": flag = ModShift; break;
                case "win": flag = ModWin; break;
                default:
                    error = Loc.T("Hotkey.UnknownModifier", token);
                    return false;
            }

            if ((mods & flag) != 0)
            {
                error = Loc.T("Hotkey.DuplicateModifier", token);
                return false;
            }
            mods |= flag;
            return true;
        }

        private static bool TryParseKey(string token, out uint vk)
        {
            vk = 0;
            if (token.Length == 1)
            {
                var c = char.ToUpperInvariant(token[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                {
                    vk = c;
                    return true;
                }
                return false;
            }

            if ((token[0] == 'F' || token[0] == 'f') && token.Length <= 3)
            {
                int n;
                if (int.TryParse(token.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out n)
                    && n >= 1 && n <= 24)
                {
                    vk = (uint)(0x70 + n - 1);
                    return true;
                }
            }

            return NamedKeys.TryGetValue(token, out vk);
        }
    }
}
