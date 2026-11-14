using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using RdpSwitcher.Config;
using RdpSwitcher.Localization;

namespace RdpSwitcher.Native
{
    public enum HotkeyAction
    {
        Next,
        Prev,
        /// <summary>Jump to session number <see cref="HotkeyEventArgs.Number"/> (1..9).</summary>
        Jump,
        TogglePin,
        LeaveFullscreen
    }

    public sealed class HotkeyEventArgs : EventArgs
    {
        public HotkeyEventArgs(HotkeyAction action, int number)
        {
            Action = action;
            Number = number;
        }

        public HotkeyAction Action { get; }

        /// <summary>1-based session number for Jump, otherwise 0.</summary>
        public int Number { get; }
    }

    /// <summary>
    /// Global hotkeys through a low-level keyboard hook. RegisterHotKey is not enough: while an RDP control has focus
    /// its own low-level hook (KeyboardHookMode = 1) takes the keys first and sends them to the VM. Hooks run
    /// newest-first, so call Reinstall() after RDP controls connect to get back in front of theirs.
    /// A matched key is swallowed (key-down, repeats and key-up) and handled later on the UI thread through a posted
    /// message, so the hook procedure stays fast. Register on entering full screen, Unregister on leaving. UI thread only.
    /// </summary>
    public sealed class HotkeyManager : NativeWindow, IDisposable
    {
        private const int WmFired = 0x8000 + 1; // WM_APP + 1

        private sealed class Entry
        {
            public Hotkey Hotkey;
            public HotkeyEventArgs Args;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly HashSet<uint> _swallowed = new HashSet<uint>();
        private readonly NativeMethods.LowLevelKeyboardProc _proc; // field: the delegate must outlive the hook
        private IntPtr _hook = IntPtr.Zero;
        private uint _mods; // modifiers as seen by the hook itself; the system state is not reliable once RDP eats keys
        private bool _disposed;

        public HotkeyManager()
        {
            _proc = HookProc;
            CreateHandle(new CreateParams());
        }

        public event EventHandler<HotkeyEventArgs> HotkeyPressed;

        public bool IsRegistered => _entries.Count > 0;

        /// <summary>
        /// Replaces the current hotkeys with the ones from the settings. Returns human-readable problems
        /// (unparsable, duplicated, or the hook could not be installed); valid ones stay active.
        /// </summary>
        public IReadOnlyList<string> Register(HotkeySettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (_disposed) throw new ObjectDisposedException(nameof(HotkeyManager));

            Unregister();
            var failures = new List<string>();

            AddText(Loc.T("Hotkey.Next"), settings.Next, new HotkeyEventArgs(HotkeyAction.Next, 0), failures);
            AddText(Loc.T("Hotkey.Prev"), settings.Prev, new HotkeyEventArgs(HotkeyAction.Prev, 0), failures);
            AddText(Loc.T("Hotkey.Pin"), settings.TogglePanelPin, new HotkeyEventArgs(HotkeyAction.TogglePin, 0), failures);
            AddText(Loc.T("Hotkey.Leave"), settings.LeaveFullscreen, new HotkeyEventArgs(HotkeyAction.LeaveFullscreen, 0), failures);

            uint jumpMods;
            string error;
            if (HotkeyParser.TryParseModifiers(settings.JumpModifier, out jumpMods, out error))
            {
                for (var n = 1; n <= 9; n++)
                    AddEntry(Loc.T("Hotkey.JumpTo", n), settings.JumpModifier + "+" + n, HotkeyParser.Jump(jumpMods, n),
                        new HotkeyEventArgs(HotkeyAction.Jump, n), failures);
            }
            else
            {
                failures.Add(Loc.T("Hotkey.JumpByNumber", settings.JumpModifier, error));
            }

            if (_entries.Count > 0 && !Install())
            {
                _entries.Clear();
                failures.Add(Loc.T("Hotkey.HookFailed", Marshal.GetLastWin32Error()));
            }

            return failures;
        }

        /// <summary>Moves the hook to the front of the chain again (after RDP controls installed theirs).</summary>
        public void Reinstall()
        {
            if (_entries.Count == 0 || _disposed) return;
            Uninstall();
            Install(false);
        }

        public void Unregister()
        {
            Uninstall();
            _entries.Clear();
            _swallowed.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            Unregister();
            _disposed = true;
            DestroyHandle();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmFired)
            {
                var index = m.WParam.ToInt32();
                if (index >= 0 && index < _entries.Count) HotkeyPressed?.Invoke(this, _entries[index].Args);
                return;
            }
            base.WndProc(ref m);
        }

        private void AddText(string title, string text, HotkeyEventArgs args, List<string> failures)
        {
            Hotkey hotkey;
            string error;
            if (!HotkeyParser.TryParse(text, out hotkey, out error))
            {
                failures.Add(Loc.T("Hotkey.WithText", title, text, error));
                return;
            }
            AddEntry(title, text, hotkey, args, failures);
        }

        private void AddEntry(string title, string text, Hotkey hotkey, HotkeyEventArgs args, List<string> failures)
        {
            if (_entries.Exists(e => e.Hotkey.Modifiers == hotkey.Modifiers && e.Hotkey.VirtualKey == hotkey.VirtualKey))
            {
                failures.Add(Loc.T("Hotkey.AlreadyUsed", title, text));
                return;
            }
            _entries.Add(new Entry { Hotkey = hotkey, Args = args });
        }

        // seedModifiers = false on Reinstall: keys may be held right now and the system state is not trustworthy.
        private bool Install(bool seedModifiers = true)
        {
            if (_hook != IntPtr.Zero) return true;

            if (seedModifiers) _mods = CurrentSystemModifiers();
            var module = NativeMethods.GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName);
            _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, module, 0);
            return _hook != IntPtr.Zero;
        }

        private void Uninstall()
        {
            if (_hook == IntPtr.Zero) return;
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        private IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                try
                {
                    if (ShouldSwallow(wParam.ToInt32(), lParam)) return (IntPtr)1;
                }
                catch (Exception)
                {
                    // Never let an exception escape into the hook chain.
                }
            }
            return NativeMethods.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        // Returns true when the event is one of ours and must not reach anyone else.
        private bool ShouldSwallow(int message, IntPtr lParam)
        {
            var data = (NativeMethods.KbdLlHookStruct)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KbdLlHookStruct));
            if ((data.flags & NativeMethods.LLKHF_INJECTED) != 0) return false;

            var down = message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN;
            var up = message == NativeMethods.WM_KEYUP || message == NativeMethods.WM_SYSKEYUP;
            if (!down && !up) return false;

            var flag = ModifierFlag(data.vkCode);
            if (flag != 0)
            {
                if (down) _mods |= flag; else _mods &= ~flag;
                return false;
            }

            if (up) return _swallowed.Remove(data.vkCode);

            if (_swallowed.Contains(data.vkCode)) return true; // auto-repeat of a hotkey being held

            for (var i = 0; i < _entries.Count; i++)
            {
                var hotkey = _entries[i].Hotkey;
                if (hotkey.VirtualKey != data.vkCode || hotkey.Modifiers != _mods) continue;

                _swallowed.Add(data.vkCode);
                NativeMethods.PostMessage(Handle, WmFired, (IntPtr)i, IntPtr.Zero);
                return true;
            }
            return false;
        }

        private static uint ModifierFlag(uint vk)
        {
            switch (vk)
            {
                case 0x10: case 0xA0: case 0xA1: return HotkeyParser.ModShift;
                case 0x11: case 0xA2: case 0xA3: return HotkeyParser.ModControl;
                case 0x12: case 0xA4: case 0xA5: return HotkeyParser.ModAlt;
                case 0x5B: case 0x5C: return HotkeyParser.ModWin;
                default: return 0;
            }
        }

        private static uint CurrentSystemModifiers()
        {
            uint mods = 0;
            if (IsDown(0x11)) mods |= HotkeyParser.ModControl;
            if (IsDown(0x12)) mods |= HotkeyParser.ModAlt;
            if (IsDown(0x10)) mods |= HotkeyParser.ModShift;
            if (IsDown(0x5B) || IsDown(0x5C)) mods |= HotkeyParser.ModWin;
            return mods;
        }

        private static bool IsDown(int vk)
        {
            return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
        }
    }
}
