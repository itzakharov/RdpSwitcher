using System;
using System.Collections.Generic;

namespace RdpSwitcher.Config
{
    public enum PanelEdge
    {
        Top,
        Bottom,
        Left,
        Right
    }

    /// <summary>What a pinned panel does to the remote desktop.</summary>
    public enum PinMode
    {
        /// <summary>Covers part of the remote desktop; its size does not change.</summary>
        Overlay,

        /// <summary>The pinned strip is taken away from the remote desktop (control and resolution shrink).</summary>
        Reserve
    }

    public class PanelSettings
    {
        public const int MinSize = 32;
        public const int MaxSize = 120;
        public const int MinTriggerZone = 1;
        public const int MaxTriggerZone = 10;

        public PanelEdge Edge { get; set; } = PanelEdge.Top;
        public int Size { get; set; } = 44;
        public int TriggerZone { get; set; } = 4;
        public int ShowDelayMs { get; set; } = 150;
        public int HideDelayMs { get; set; } = 600;
        public double Opacity { get; set; } = 0.95;

        /// <summary>The user pinned the panel (it never hides). Restored at start.</summary>
        public bool Pinned { get; set; }

        public PinMode PinMode { get; set; } = PinMode.Overlay;
    }

    public enum WatermarkPosition
    {
        TopRight,
        BottomRight
    }

    /// <summary>Session-name label shown over the remote desktop in full screen.</summary>
    public class WatermarkSettings
    {
        public const double MinOpacity = 0.1;
        public const double MaxOpacity = 1.0;

        public bool Enabled { get; set; } = true;
        public WatermarkPosition Position { get; set; } = WatermarkPosition.BottomRight;
        public double Opacity { get; set; } = 0.5;
    }

    public class HotkeySettings
    {
        public string Next { get; set; } = "Ctrl+Alt+Right";
        public string Prev { get; set; } = "Ctrl+Alt+Left";
        public string JumpModifier { get; set; } = "Ctrl+Alt";
        public string TogglePanelPin { get; set; } = "Ctrl+Alt+Home";
        public string LeaveFullscreen { get; set; } = "Ctrl+Alt+F11";
    }

    /// <summary>One RDP target. Never holds a password: credentials are entered in the CredSSP window.</summary>
    public class ConnectionInfo
    {
        public const int DefaultPort = 3389;

        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Host { get; set; } = "";
        public int Port { get; set; } = DefaultPort;
        public string User { get; set; }
        public string Group { get; set; }
        public string Color { get; set; }
        public bool AutoConnect { get; set; }
    }

    public class AppConfig
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;

        /// <summary>UI language code (en, ru, uk). Null until the first start picks it from the system.</summary>
        public string Language { get; set; }

        public PanelSettings Panel { get; set; } = new PanelSettings();
        public WatermarkSettings Watermark { get; set; } = new WatermarkSettings();
        public HotkeySettings Hotkeys { get; set; } = new HotkeySettings();
        public List<ConnectionInfo> Connections { get; set; } = new List<ConnectionInfo>();

        /// <summary>Fixes missing sections, out-of-range values and duplicate/empty ids in place.</summary>
        public void Normalize()
        {
            if (Version <= 0) Version = CurrentVersion;
            if (!Localization.Loc.IsSupported(Language)) Language = null;
            if (Panel == null) Panel = new PanelSettings();
            if (Hotkeys == null) Hotkeys = new HotkeySettings();
            if (Watermark == null) Watermark = new WatermarkSettings();
            if (double.IsNaN(Watermark.Opacity)) Watermark.Opacity = new WatermarkSettings().Opacity;
            Watermark.Opacity = Math.Max(WatermarkSettings.MinOpacity, Math.Min(WatermarkSettings.MaxOpacity, Watermark.Opacity));
            if (!Enum.IsDefined(typeof(WatermarkPosition), Watermark.Position)) Watermark.Position = WatermarkPosition.BottomRight;
            if (Connections == null) Connections = new List<ConnectionInfo>();

            Panel.Size = Clamp(Panel.Size, PanelSettings.MinSize, PanelSettings.MaxSize);
            Panel.TriggerZone = Clamp(Panel.TriggerZone, PanelSettings.MinTriggerZone, PanelSettings.MaxTriggerZone);
            if (Panel.ShowDelayMs < 0) Panel.ShowDelayMs = 0;
            if (Panel.HideDelayMs < 0) Panel.HideDelayMs = 0;
            if (double.IsNaN(Panel.Opacity)) Panel.Opacity = 0.95;
            Panel.Opacity = Math.Max(0.2, Math.Min(1.0, Panel.Opacity));
            if (!Enum.IsDefined(typeof(PanelEdge), Panel.Edge)) Panel.Edge = PanelEdge.Top;
            if (!Enum.IsDefined(typeof(PinMode), Panel.PinMode)) Panel.PinMode = PinMode.Overlay;

            var defaults = new HotkeySettings();
            if (string.IsNullOrWhiteSpace(Hotkeys.Next)) Hotkeys.Next = defaults.Next;
            if (string.IsNullOrWhiteSpace(Hotkeys.Prev)) Hotkeys.Prev = defaults.Prev;
            if (string.IsNullOrWhiteSpace(Hotkeys.JumpModifier)) Hotkeys.JumpModifier = defaults.JumpModifier;
            if (string.IsNullOrWhiteSpace(Hotkeys.TogglePanelPin)) Hotkeys.TogglePanelPin = defaults.TogglePanelPin;
            if (string.IsNullOrWhiteSpace(Hotkeys.LeaveFullscreen)) Hotkeys.LeaveFullscreen = defaults.LeaveFullscreen;

            Connections.RemoveAll(c => c == null);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in Connections)
            {
                if (string.IsNullOrWhiteSpace(c.Id) || !seen.Add(c.Id))
                {
                    c.Id = Guid.NewGuid().ToString();
                    seen.Add(c.Id);
                }
                if (c.Port < 1 || c.Port > 65535) c.Port = ConnectionInfo.DefaultPort;
                if (c.Host == null) c.Host = "";
                if (string.IsNullOrWhiteSpace(c.Name)) c.Name = c.Host;
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
