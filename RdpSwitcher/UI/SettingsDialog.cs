using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RdpSwitcher.Config;
using RdpSwitcher.Localization;
using RdpSwitcher.Native;

namespace RdpSwitcher.UI
{
    /// <summary>
    /// Edits panel and hotkey settings. Works on its own copies of the values; nothing is written to the config
    /// until ApplyTo is called after DialogResult.OK. Invalid input keeps the dialog open.
    /// </summary>
    public class SettingsDialog : Form
    {
        private const int MaxDelayMs = 5000;

        private readonly ComboBox _language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly ComboBox _edge =new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly ComboBox _pinMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly NumericUpDown _size = Number(PanelSettings.MinSize, PanelSettings.MaxSize);
        private readonly NumericUpDown _trigger = Number(PanelSettings.MinTriggerZone, PanelSettings.MaxTriggerZone);
        private readonly NumericUpDown _showDelay = Number(0, MaxDelayMs);
        private readonly NumericUpDown _hideDelay = Number(0, MaxDelayMs);
        private readonly CheckBox _watermarkOn = new CheckBox { AutoSize = true };
        private readonly ComboBox _watermarkPosition = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly NumericUpDown _watermarkOpacity = Number(10, 100);
        private readonly TextBox _next = HotkeyBox();
        private readonly TextBox _prev = HotkeyBox();
        private readonly TextBox _jump = HotkeyBox();
        private readonly TextBox _pin = HotkeyBox();
        private readonly TextBox _leave = HotkeyBox();

        public SettingsDialog(AppConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            Text = Loc.T("Settings.Title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);
            Font = new Font("Segoe UI", 9f);

            _language.Items.AddRange(Loc.Supported.Select(Loc.NativeName).Cast<object>().ToArray()); // same order as Loc.Supported
            _edge.Items.AddRange(new object[] { Loc.T("Edge.Top"), Loc.T("Edge.Bottom"), Loc.T("Edge.Left"), Loc.T("Edge.Right") }); // same order as PanelEdge
            _pinMode.Items.AddRange(new object[] { Loc.T("Settings.PinOverlay"), Loc.T("Settings.PinReserve") }); // same order as PinMode
            _watermarkPosition.Items.AddRange(new object[] { Loc.T("Settings.WatermarkTopRight"), Loc.T("Settings.WatermarkBottomRight") }); // same order as WatermarkPosition

            var table = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));

            AddHeader(table, Loc.T("Settings.General"));
            AddRow(table, Loc.T("Settings.Language"), _language);

            AddHeader(table, Loc.T("Settings.Panel"));
            AddRow(table, Loc.T("Settings.Edge"), _edge);
            AddRow(table, Loc.T("Settings.Size", PanelSettings.MinSize, PanelSettings.MaxSize), _size);
            AddRow(table, Loc.T("Settings.TriggerZone", PanelSettings.MinTriggerZone, PanelSettings.MaxTriggerZone), _trigger);
            AddRow(table, Loc.T("Settings.ShowDelay"), _showDelay);
            AddRow(table, Loc.T("Settings.HideDelay"), _hideDelay);
            AddRow(table, Loc.T("Settings.PinMode"), _pinMode);

            AddHeader(table, Loc.T("Settings.Watermark"));
            AddRow(table, Loc.T("Settings.WatermarkShow"), _watermarkOn);
            AddRow(table, Loc.T("Settings.WatermarkPosition"), _watermarkPosition);
            AddRow(table, Loc.T("Settings.WatermarkOpacity"), _watermarkOpacity);

            AddHeader(table, Loc.T("Settings.Hotkeys"));
            AddRow(table, Loc.T("Hotkey.Next"), _next);
            AddRow(table, Loc.T("Hotkey.Prev"), _prev);
            AddRow(table, Loc.T("Settings.HotkeyJumpModifiers"), _jump);
            AddRow(table, Loc.T("Hotkey.Pin"), _pin);
            AddRow(table, Loc.T("Hotkey.Leave"), _leave);

            var ok = new Button { Text = "OK", AutoSize = true, MinimumSize = new Size(80, 26) };
            ok.Click += OnOkClick;
            var cancel = new Button { Text = Loc.T("Common.Cancel"), AutoSize = true, MinimumSize = new Size(80, 26), DialogResult = DialogResult.Cancel };
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 10, 0, 0)
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            var buttonRow = table.RowCount;
            table.RowCount = buttonRow + 1;
            table.Controls.Add(buttons, 0, buttonRow);
            table.SetColumnSpan(buttons, 2);

            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(table);

            LoadValues(config);
        }

        /// <summary>Writes the validated values into the config. Call only after DialogResult.OK.</summary>
        public void ApplyTo(AppConfig config)
        {
            config.Language = Loc.Supported[_language.SelectedIndex];
            config.Panel.Edge = (PanelEdge)_edge.SelectedIndex;
            config.Panel.Size = (int)_size.Value;
            config.Panel.TriggerZone = (int)_trigger.Value;
            config.Panel.ShowDelayMs = (int)_showDelay.Value;
            config.Panel.HideDelayMs = (int)_hideDelay.Value;
            config.Panel.PinMode = (PinMode)_pinMode.SelectedIndex;

            config.Watermark.Enabled = _watermarkOn.Checked;
            config.Watermark.Position = (WatermarkPosition)_watermarkPosition.SelectedIndex;
            config.Watermark.Opacity = (double)_watermarkOpacity.Value / 100.0;

            config.Hotkeys.Next = _next.Text.Trim();
            config.Hotkeys.Prev = _prev.Text.Trim();
            config.Hotkeys.JumpModifier = _jump.Text.Trim();
            config.Hotkeys.TogglePanelPin = _pin.Text.Trim();
            config.Hotkeys.LeaveFullscreen = _leave.Text.Trim();
        }

        private void LoadValues(AppConfig config)
        {
            _language.SelectedIndex = Math.Max(0, Loc.Supported.ToList().IndexOf(config.Language ?? Loc.Language));
            _edge.SelectedIndex = (int)config.Panel.Edge;
            _size.Value = Math.Max(_size.Minimum, Math.Min(_size.Maximum, config.Panel.Size));
            _trigger.Value = Math.Max(_trigger.Minimum, Math.Min(_trigger.Maximum, config.Panel.TriggerZone));
            _showDelay.Value = Math.Max(_showDelay.Minimum, Math.Min(_showDelay.Maximum, config.Panel.ShowDelayMs));
            _hideDelay.Value = Math.Max(_hideDelay.Minimum, Math.Min(_hideDelay.Maximum, config.Panel.HideDelayMs));
            _pinMode.SelectedIndex = (int)config.Panel.PinMode;

            _watermarkOn.Checked = config.Watermark.Enabled;
            _watermarkPosition.SelectedIndex = (int)config.Watermark.Position;
            var percent = (decimal)Math.Round(config.Watermark.Opacity * 100);
            _watermarkOpacity.Value = Math.Max(_watermarkOpacity.Minimum, Math.Min(_watermarkOpacity.Maximum, percent));

            _next.Text = config.Hotkeys.Next;
            _prev.Text = config.Hotkeys.Prev;
            _jump.Text = config.Hotkeys.JumpModifier;
            _pin.Text = config.Hotkeys.TogglePanelPin;
            _leave.Text = config.Hotkeys.LeaveFullscreen;
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            string error;
            Control bad;
            if (!TryValidate(out error, out bad))
            {
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                bad.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
        }

        private bool TryValidate(out string error, out Control bad)
        {
            error = null;
            bad = null;

            var used = new Dictionary<string, string>();
            // Returns a conflict message, or null when the combination is still free (out parameters cannot be used in a lambda).
            Func<Hotkey, string, string> claim = (hotkey, title) =>
            {
                var key = hotkey.Modifiers + ":" + hotkey.VirtualKey;
                string other;
                if (used.TryGetValue(key, out other)) return Loc.T("Settings.HotkeyConflict", title, other);
                used[key] = title;
                return null;
            };

            var singles = new[]
            {
                Tuple.Create(Loc.T("Hotkey.Next"), _next),
                Tuple.Create(Loc.T("Hotkey.Prev"), _prev),
                Tuple.Create(Loc.T("Hotkey.Pin"), _pin),
                Tuple.Create(Loc.T("Hotkey.Leave"), _leave)
            };
            foreach (var item in singles)
            {
                Hotkey hotkey;
                string parseError;
                if (!HotkeyParser.TryParse(item.Item2.Text, out hotkey, out parseError))
                {
                    error = Loc.T("Settings.HotkeyError", item.Item1, parseError);
                    bad = item.Item2;
                    return false;
                }
                var conflict = claim(hotkey, item.Item1);
                if (conflict != null)
                {
                    error = conflict;
                    bad = item.Item2;
                    return false;
                }
            }

            uint jumpMods;
            string jumpError;
            if (!HotkeyParser.TryParseModifiers(_jump.Text, out jumpMods, out jumpError))
            {
                error = Loc.T("Settings.HotkeyError", Loc.T("Settings.HotkeyJumpModifiers"), jumpError);
                bad = _jump;
                return false;
            }
            for (var n = 1; n <= 9; n++)
            {
                var conflict = claim(HotkeyParser.Jump(jumpMods, n), Loc.T("Settings.SessionNumber", n));
                if (conflict != null)
                {
                    error = conflict;
                    bad = _jump;
                    return false;
                }
            }
            return true;
        }

        private static NumericUpDown Number(int min, int max)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Dock = DockStyle.Fill };
        }

        private static TextBox HotkeyBox()
        {
            return new TextBox { Dock = DockStyle.Fill };
        }

        private static void AddHeader(TableLayoutPanel table, string text)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Margin = new Padding(0, table.RowCount == 0 ? 0 : 12, 0, 4)
            };
            var row = table.RowCount;
            table.RowCount = row + 1;
            table.Controls.Add(label, 0, row);
            table.SetColumnSpan(label, 2);
        }

        private static void AddRow(TableLayoutPanel table, string text, Control editor)
        {
            var label = new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 12, 6) };
            var row = table.RowCount;
            table.RowCount = row + 1;
            table.Controls.Add(label, 0, row);
            table.Controls.Add(editor, 1, row);
        }
    }
}
