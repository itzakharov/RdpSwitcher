using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RdpSwitcher.Config;
using RdpSwitcher.Localization;

namespace RdpSwitcher.UI
{
    /// <summary>
    /// Edits the list of connections. Works on copies; nothing is written to the config until ApplyTo is called
    /// after DialogResult.OK. No password is asked here: credentials are entered in the CredSSP window.
    /// </summary>
    public class ConnectionsDialog : Form
    {
        private readonly List<ConnectionInfo> _items;
        private readonly ListView _list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            ShowGroups = true,
            Dock = DockStyle.Fill
        };
        private readonly Button _edit = new Button { Text = Loc.T("Connections.Edit"), AutoSize = true, MinimumSize = new Size(110, 26) };
        private readonly Button _duplicate = new Button { Text = Loc.T("Connections.Duplicate"), AutoSize = true, MinimumSize = new Size(110, 26) };
        private readonly Button _delete = new Button { Text = Loc.T("Connections.Delete"), AutoSize = true, MinimumSize = new Size(110, 26) };

        public ConnectionsDialog(AppConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            _items = config.Connections.Select(Clone).ToList();

            Text = Loc.T("Connections.Title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(760, 380);
            Font = new Font("Segoe UI", 9f);

            _list.Columns.Add(Loc.T("Connections.ColName"), 200);
            _list.Columns.Add(Loc.T("Connections.ColHost"), 160);
            _list.Columns.Add(Loc.T("Connections.ColPort"), 55);
            _list.Columns.Add(Loc.T("Connections.ColUser"), 140);
            _list.Columns.Add(Loc.T("Connections.ColAuto"), 50);
            _list.SelectedIndexChanged += (s, e) => UpdateButtons();
            _list.DoubleClick += (s, e) => EditSelected();
            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) DeleteSelected();
            };

            var add = new Button { Text = Loc.T("Connections.Add"), AutoSize = true, MinimumSize = new Size(110, 26) };
            add.Click += (s, e) => AddNew();
            _edit.Click += (s, e) => EditSelected();
            _duplicate.Click += (s, e) => DuplicateSelected();
            _delete.Click += (s, e) => DeleteSelected();

            var side = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Dock = DockStyle.Right,
                Padding = new Padding(8, 0, 0, 0)
            };
            side.Controls.AddRange(new Control[] { add, _edit, _duplicate, _delete });

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(80, 26) };
            var cancel = new Button { Text = Loc.T("Common.Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(80, 26) };
            var bottom = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Bottom,
                Padding = new Padding(0, 8, 0, 0)
            };
            bottom.Controls.Add(cancel);
            bottom.Controls.Add(ok);

            AcceptButton = ok;
            CancelButton = cancel;

            // Dock order: the last added control is laid out first.
            Padding = new Padding(12);
            Controls.Add(_list);
            Controls.Add(side);
            Controls.Add(bottom);
            _list.BringToFront();

            Refill(null);
        }

        /// <summary>
        /// Writes the edited list into the config. Existing entries keep their object (and id), so open sessions
        /// and the panel see the new name/color; removed entries are dropped, new ones are appended in list order.
        /// </summary>
        public void ApplyTo(AppConfig config)
        {
            var existing = config.Connections.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
            var result = new List<ConnectionInfo>();
            foreach (var item in _items)
            {
                ConnectionInfo target;
                if (existing.TryGetValue(item.Id, out target)) CopyFields(item, target);
                else target = Clone(item);
                result.Add(target);
            }
            config.Connections = result;
        }

        private void AddNew()
        {
            var info = new ConnectionInfo();
            if (!Edit(info)) return;
            _items.Add(info);
            Refill(info.Id);
        }

        private void EditSelected()
        {
            var info = Selected();
            if (info == null) return;
            var copy = Clone(info);
            if (!Edit(copy)) return;
            CopyFields(copy, info);
            Refill(info.Id);
        }

        private void DuplicateSelected()
        {
            var info = Selected();
            if (info == null) return;
            var copy = Clone(info);
            copy.Id = Guid.NewGuid().ToString();
            copy.Name = Loc.T("Connections.CopySuffix", info.Name ?? "");
            copy.AutoConnect = false;
            _items.Insert(_items.IndexOf(info) + 1, copy);
            Refill(copy.Id);
        }

        private void DeleteSelected()
        {
            var info = Selected();
            if (info == null) return;
            var answer = MessageBox.Show(this, Loc.T("Connections.ConfirmDelete", info.Name), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            _items.Remove(info);
            Refill(null);
        }

        private bool Edit(ConnectionInfo info)
        {
            var groups = _items.Select(c => c.Group).Where(g => !string.IsNullOrWhiteSpace(g)).Distinct().ToList();
            using (var form = new ConnectionEditForm(info, groups))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return false;
                form.ApplyTo(info);
                return true;
            }
        }

        private ConnectionInfo Selected()
        {
            return _list.SelectedItems.Count == 0 ? null : (ConnectionInfo)_list.SelectedItems[0].Tag;
        }

        private void Refill(string selectId)
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Groups.Clear();

            var groups = new Dictionary<string, ListViewGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in _items)
            {
                var title = string.IsNullOrWhiteSpace(info.Group) ? Loc.T("Connections.NoGroup") : info.Group.Trim();
                ListViewGroup group;
                if (!groups.TryGetValue(title, out group))
                {
                    group = new ListViewGroup(title);
                    groups[title] = group;
                    _list.Groups.Add(group);
                }
                var row = new ListViewItem(info.Name, group) { Tag = info };
                row.SubItems.Add(info.Host);
                row.SubItems.Add(info.Port.ToString());
                row.SubItems.Add(info.User ?? "");
                row.SubItems.Add(info.AutoConnect ? Loc.T("Common.Yes") : "");
                _list.Items.Add(row);
                if (info.Id == selectId)
                {
                    row.Selected = true;
                    row.EnsureVisible();
                }
            }
            _list.EndUpdate();
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var has = _list.SelectedItems.Count > 0;
            _edit.Enabled = has;
            _duplicate.Enabled = has;
            _delete.Enabled = has;
        }

        private static ConnectionInfo Clone(ConnectionInfo source)
        {
            var copy = new ConnectionInfo { Id = source.Id };
            CopyFields(source, copy);
            return copy;
        }

        private static void CopyFields(ConnectionInfo from, ConnectionInfo to)
        {
            to.Name = from.Name;
            to.Host = from.Host;
            to.Port = from.Port;
            to.User = from.User;
            to.Group = from.Group;
            to.Color = from.Color;
            to.AutoConnect = from.AutoConnect;
        }
    }

    /// <summary>Edits one connection. Invalid input keeps the dialog open.</summary>
    internal class ConnectionEditForm : Form
    {
        private readonly TextBox _name = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox _host = new TextBox { Dock = DockStyle.Fill };
        private readonly NumericUpDown _port = new NumericUpDown { Minimum = 1, Maximum = 65535, Dock = DockStyle.Fill };
        private readonly TextBox _user = new TextBox { Dock = DockStyle.Fill };
        private readonly ComboBox _group = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
        private readonly TextBox _color = new TextBox { Dock = DockStyle.Fill };
        private readonly Panel _swatch = new Panel { Width = 24, Height = 22, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(4, 0, 4, 0) };
        private readonly CheckBox _auto = new CheckBox { Text = Loc.T("ConnectionEdit.Auto"), AutoSize = true };

        public ConnectionEditForm(ConnectionInfo info, IEnumerable<string> groups)
        {
            Text = Loc.T("ConnectionEdit.Title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);
            Font = new Font("Segoe UI", 9f);

            _group.Items.AddRange(groups.Cast<object>().ToArray());

            var table = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));

            AddRow(table, Loc.T("ConnectionEdit.Name"), _name);
            AddRow(table, Loc.T("ConnectionEdit.Host"), _host);
            AddRow(table, Loc.T("ConnectionEdit.Port"), _port);
            AddRow(table, Loc.T("ConnectionEdit.User"), _user);
            AddRow(table, Loc.T("ConnectionEdit.Group"), _group);

            // Anchor (not Dock) centers each cell's control vertically; Dock/margins left the swatch and button lower than the text box.
            _color.Dock = DockStyle.None;
            _color.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _color.Margin = new Padding(0);
            _swatch.Anchor = AnchorStyles.None;
            _swatch.Margin = new Padding(4, 0, 4, 0);
            var pick = new Button { Text = "…", Width = 28, Height = 23, Anchor = AnchorStyles.None, Margin = new Padding(0) };
            pick.Click += OnPickColor;
            var colorRow = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(3) };
            colorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            colorRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            colorRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            colorRow.Controls.Add(_color, 0, 0);
            colorRow.Controls.Add(_swatch, 1, 0);
            colorRow.Controls.Add(pick, 2, 0);
            AddRow(table, Loc.T("ConnectionEdit.Color"), colorRow);
            _color.TextChanged += (s, e) => UpdateSwatch();

            var autoRow = table.RowCount;
            table.RowCount = autoRow + 1;
            table.Controls.Add(_auto, 1, autoRow);

            var note = new Label
            {
                Text = Loc.T("ConnectionEdit.PasswordNote"),
                AutoSize = true,
                MaximumSize = new Size(380, 0),
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 8, 0, 0)
            };
            var noteRow = table.RowCount;
            table.RowCount = noteRow + 1;
            table.Controls.Add(note, 0, noteRow);
            table.SetColumnSpan(note, 2);

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

            _name.Text = info.Name;
            _host.Text = info.Host;
            _port.Value = Math.Max(_port.Minimum, Math.Min(_port.Maximum, info.Port));
            _user.Text = info.User ?? "";
            _group.Text = info.Group ?? "";
            _color.Text = info.Color ?? "";
            _auto.Checked = info.AutoConnect;
            UpdateSwatch();
        }

        public void ApplyTo(ConnectionInfo info)
        {
            var host = _host.Text.Trim();
            var name = _name.Text.Trim();
            info.Name = name.Length == 0 ? host : name;
            info.Host = host;
            info.Port = (int)_port.Value;
            info.User = NullIfEmpty(_user.Text);
            info.Group = NullIfEmpty(_group.Text);
            info.Color = NullIfEmpty(_color.Text);
            info.AutoConnect = _auto.Checked;
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
            var host = _host.Text.Trim();
            if (host.Length == 0)
            {
                error = Loc.T("ConnectionEdit.HostRequired");
                bad = _host;
                return false;
            }
            if (host.Any(char.IsWhiteSpace))
            {
                error = Loc.T("ConnectionEdit.HostSpaces");
                bad = _host;
                return false;
            }
            Color unused;
            if (_color.Text.Trim().Length > 0 && !TryParseColor(_color.Text, out unused))
            {
                error = Loc.T("ConnectionEdit.BadColor");
                bad = _color;
                return false;
            }
            return true;
        }

        private void OnPickColor(object sender, EventArgs e)
        {
            using (var dialog = new ColorDialog { FullOpen = true })
            {
                Color current;
                if (TryParseColor(_color.Text, out current)) dialog.Color = current;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _color.Text = string.Format("#{0:X2}{1:X2}{2:X2}", dialog.Color.R, dialog.Color.G, dialog.Color.B);
            }
        }

        private void UpdateSwatch()
        {
            Color color;
            _swatch.BackColor = TryParseColor(_color.Text, out color) ? color : SystemColors.Control;
        }

        private static bool TryParseColor(string text, out Color color)
        {
            color = Color.Empty;
            if (string.IsNullOrWhiteSpace(text)) return false;
            try
            {
                color = ColorTranslator.FromHtml(text.Trim());
                return !color.IsEmpty;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string NullIfEmpty(string text)
        {
            var trimmed = (text ?? "").Trim();
            return trimmed.Length == 0 ? null : trimmed;
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
