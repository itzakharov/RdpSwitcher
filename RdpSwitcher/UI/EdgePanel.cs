using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RdpSwitcher.Config;
using RdpSwitcher.Localization;
using RdpSwitcher.Sessions;

namespace RdpSwitcher.UI
{
    /// <summary>Full: session list, new connection, pin, watermark. Single: the secondary window's one connection only.</summary>
    public enum PanelMode
    {
        Full,
        Single
    }

    /// <summary>
    /// Hideable panel on one edge of the host form, drawn on top of the RDP controls. Shows a button per session,
    /// "new connection", pin, full-screen toggle and edge menu. Asks for actions through events only; it knows
    /// nothing about RDP types. Show/hide timing is EdgePanelLogic, driven by a 50 ms cursor poll
    /// (mouse events do not reach the form while focus is inside the RDP control).
    /// </summary>
    public class EdgePanel : Panel
    {
        private const int ItemMargin = 6;
        private const int Gap = 4;
        private const int SessionLength = 170;
        private const int VerticalItemLength = 36;
        private const int CompactLimit = 100;

        private static readonly Color PanelColor = Color.FromArgb(32, 32, 32);
        private static readonly Color ButtonColor = Color.FromArgb(55, 55, 55);
        private static readonly Color AccentColor = Color.FromArgb(0, 95, 165);

        private readonly SessionManager _sessions;
        private readonly AppConfig _config;
        private readonly EdgePanelLogic _logic;
        private readonly Timer _timer = new Timer { Interval = 50 };
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly ToolTip _tips = new ToolTip();
        private readonly List<SessionButton> _sessionButtons = new List<SessionButton>();
        private readonly ToolButton _newButton;
        private readonly ToolButton _positionButton;
        private readonly ToolButton _reconnectButton;
        private readonly PanelMode _mode;
        private readonly IconButton _pinButton;
        private readonly IconButton _watermarkButton;
        private readonly IconButton _fullscreenButton;
        private readonly IconButton _closeButton;
        private readonly ToolButton _warningButton;
        private string _warning;

        private const int MinMenuAwayMs = 300;

        private Control _host;
        private bool _menuOpen;
        private bool _userPinned;
        private ContextMenuStrip _openMenu;
        private TimeSpan? _menuAwaySince;
        private bool _isFullscreen;
        private bool _watermarkEnabled;

        public EdgePanel(SessionManager sessions, AppConfig config, PanelMode mode = PanelMode.Full)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _mode = mode;
            _logic = new EdgePanelLogic(config.Panel);
            // A secondary window has no pin button or hotkey, so a saved pin would be impossible to undo there.
            _userPinned = mode == PanelMode.Full && config.Panel.Pinned;
            ApplyPin();

            Visible = false;
            BackColor = PanelColor;
            Font = new Font("Segoe UI", 9f);

            _newButton = AddToolButton(() => ShowNewConnectionMenu());
            _positionButton = AddToolButton(() => ShowPositionMenu());
            // The panel only has one session in Single mode; the click asks to reconnect it (no-op unless disconnected).
            _reconnectButton = AddToolButton(() =>
            {
                var active = _sessions.Active;
                if (active != null) SessionClicked?.Invoke(this, active.Id);
                ReturnFocus();
            });
            _pinButton = AddIconButton(IconKind.Pin, () => TogglePin());
            _watermarkButton = AddIconButton(IconKind.Watermark, () =>
            {
                WatermarkToggleRequested?.Invoke(this, EventArgs.Empty);
                ReturnFocus();
            });
            _fullscreenButton = AddIconButton(IconKind.EnterFullscreen, () =>
            {
                ToggleFullscreenRequested?.Invoke(this, EventArgs.Empty);
                ReturnFocus();
            });
            _closeButton = AddIconButton(IconKind.Close, () => CloseRequested?.Invoke(this, EventArgs.Empty));
            _closeButton.HoverColor = Color.FromArgb(196, 43, 28);

            _warningButton = AddToolButton(() => ShowWarningDialog());
            _warningButton.BackColor = Color.FromArgb(180, 110, 0);
            _warningButton.Visible = false;

            // Hidden children are skipped by LayoutItems (IsToolShown); Visible is set only for the controls' own state.
            _newButton.Visible = _mode == PanelMode.Full;
            _pinButton.Visible = _mode == PanelMode.Full;
            _watermarkButton.Visible = _mode == PanelMode.Full;
            _reconnectButton.Visible = _mode == PanelMode.Single;

            _sessions.SessionsChanged += OnSessionsChanged;
            _sessions.ActiveChanged += OnActiveChanged;
            _sessions.SessionStateChanged += OnSessionStateChanged;

            RebuildSessionButtons();
            _timer.Tick += OnTick;
            _timer.Start();
        }

        /// <summary>A session button was clicked; argument is the session id.</summary>
        public event EventHandler<string> SessionClicked;

        /// <summary>"Close session" was chosen in a session button's context menu; argument is the session id.</summary>
        public event EventHandler<string> SessionCloseRequested;

        /// <summary>"Move to a separate window" was chosen in a session button's context menu; argument is the session id.</summary>
        public event EventHandler<string> SessionDetachRequested;

        /// <summary>An item of the "new connection" menu was chosen.</summary>
        public event EventHandler<ConnectionInfo> NewConnectionRequested;

        public event EventHandler ToggleFullscreenRequested;

        /// <summary>The close-application icon was clicked. The owner decides whether to close (and whether to ask for confirmation).</summary>
        public event EventHandler CloseRequested;

        /// <summary>An edge was chosen in the position menu. The owner saves it and calls ApplySettings.</summary>
        public event EventHandler<PanelEdge> EdgeChangeRequested;

        public event EventHandler PinChanged;

        /// <summary>The watermark switch was clicked. The owner flips and saves config.Watermark.Enabled, then sets WatermarkEnabled.</summary>
        public event EventHandler WatermarkToggleRequested;

        /// <summary>Set by the owner: the watermark switch is highlighted while the watermark is enabled.</summary>
        public bool WatermarkEnabled
        {
            get => _watermarkEnabled;
            set
            {
                _watermarkEnabled = value;
                UpdateToolTexts();
            }
        }

        /// <summary>"Settings..." was chosen in the position menu. The owner shows the dialog, then calls ApplySettings.</summary>
        public event EventHandler SettingsRequested;

        /// <summary>"Manage connections..." was chosen in the new-connection menu. The owner shows the dialog; the menu reads the config live.</summary>
        public event EventHandler ConnectionsRequested;

        /// <summary>The user's pin (what is saved). The panel is also kept shown while there are no sessions, without this flag.</summary>
        public bool Pinned => _userPinned;

        /// <summary>Set by the owner so the button reads "leave full screen" or "full screen".</summary>
        public bool IsFullscreen
        {
            get => _isFullscreen;
            set
            {
                _isFullscreen = value;
                UpdateToolTexts();
                LayoutItems();
            }
        }

        public void TogglePin()
        {
            _userPinned = !_userPinned;
            ApplyPin();
            UpdateToolTexts();
            PinChanged?.Invoke(this, EventArgs.Empty);
            ReturnFocus();
        }

        /// <summary>Shows a warning button (e.g. hotkeys that could not be registered); null or empty hides it.</summary>
        public void SetWarning(string text)
        {
            _warning = string.IsNullOrWhiteSpace(text) ? null : text;
            _warningButton.Visible = _warning != null;
            UpdateToolTexts();
            LayoutItems();
        }

        private void ShowWarningDialog()
        {
            if (_warning == null) return;
            _menuOpen = true; // keeps the poll from hiding the panel under the dialog
            try
            {
                MessageBox.Show(FindForm(), _warning, "RdpSwitcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _menuOpen = false;
                ReturnFocus();
            }
        }

        /// <summary>Applies changed panel settings (edge, size, delays) without recreating the panel.</summary>
        public void ApplySettings(PanelSettings settings)
        {
            _logic.Apply(settings);
            UpdateToolTexts();
            LayoutItems();
            PlaceOnEdge();
        }

        /// <summary>Gives keyboard focus back to the active session and keeps the panel above it.</summary>
        public void ReturnFocus()
        {
            _sessions.Active?.FocusInput();
            if (Visible) BringToFront();
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            if (_host != null) _host.SizeChanged -= OnHostSizeChanged;
            _host = Parent;
            if (_host != null) _host.SizeChanged += OnHostSizeChanged;
            PlaceOnEdge();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _tips.Dispose();
                _sessions.SessionsChanged -= OnSessionsChanged;
                _sessions.ActiveChanged -= OnActiveChanged;
                _sessions.SessionStateChanged -= OnSessionStateChanged;
                if (_host != null) _host.SizeChanged -= OnHostSizeChanged;
            }
            base.Dispose(disposing);
        }

        // ---- show / hide ----

        private void OnTick(object sender, EventArgs e)
        {
            var host = Parent;
            if (_menuOpen)
            {
                DismissStrayMenu();
                return;
            }
            if (host == null) return;

            var form = host.FindForm();
            if (form == null || form != Form.ActiveForm) return;

            var area = host.RectangleToScreen(host.ClientRectangle);
            switch (_logic.Update(Cursor.Position, area, _clock.Elapsed))
            {
                case PanelAction.Show:
                    PlaceOnEdge();
                    Visible = true;
                    BringToFront();
                    break;
                case PanelAction.Hide:
                    Visible = false;
                    _sessions.Active?.FocusInput();
                    break;
            }
        }

        // A click inside the RDP control does not reach the menu's mouse filter, so the menu stays open and,
        // with _menuOpen set, the panel would never hide. Close the menu once the cursor has stayed away from
        // the panel and from every open drop-down for the hide delay.
        private void DismissStrayMenu()
        {
            if (_openMenu == null) return;

            var cursor = Cursor.Position;
            if (RectangleToScreen(ClientRectangle).Contains(cursor) || IsOverDropDown(_openMenu, cursor))
            {
                _menuAwaySince = null;
                return;
            }

            var now = _clock.Elapsed;
            if (_menuAwaySince == null) _menuAwaySince = now;
            else if ((now - _menuAwaySince.Value).TotalMilliseconds >= Math.Max(_logic.HideDelayMs, MinMenuAwayMs))
                _openMenu.Close();
        }

        private static bool IsOverDropDown(ToolStripDropDown dropDown, Point screenPoint)
        {
            if (!dropDown.Visible) return false;
            if (dropDown.Bounds.Contains(screenPoint)) return true;

            foreach (ToolStripItem item in dropDown.Items)
            {
                var menuItem = item as ToolStripMenuItem;
                if (menuItem != null && menuItem.HasDropDownItems && IsOverDropDown(menuItem.DropDown, screenPoint))
                    return true;
            }
            return false;
        }

        private void OnHostSizeChanged(object sender, EventArgs e)
        {
            PlaceOnEdge();
        }

        private void PlaceOnEdge()
        {
            if (_host == null) return;
            Bounds = _logic.Bounds(_host.ClientRectangle);
        }

        // ---- sessions ----

        // With no sessions there is nothing else on screen, so the panel stays shown (first start, last session closed).
        private void ApplyPin()
        {
            _logic.Pinned = _userPinned || _sessions.Sessions.Count == 0;
        }

        private void OnSessionsChanged(object sender, EventArgs e)
        {
            ApplyPin();
            RebuildSessionButtons();
            if (Visible) BringToFront();
        }

        // RdpSession.Show() brings its control to the front, so the panel has to climb back above it.
        private void OnActiveChanged(object sender, EventArgs e)
        {
            RefreshSessionButtons();
            if (Visible) BringToFront();
        }

        private void OnSessionStateChanged(object sender, EventArgs e)
        {
            RefreshSessionButtons();
        }

        private void RebuildSessionButtons()
        {
            SuspendLayout();
            foreach (var old in _sessionButtons)
            {
                _tips.SetToolTip(old, null);
                Controls.Remove(old);
                old.Dispose();
            }
            _sessionButtons.Clear();

            foreach (var session in _sessions.Sessions)
            {
                var button = new SessionButton { SessionId = session.Id };
                if (_mode == PanelMode.Single)
                {
                    // Only a label (name, colour, state); reconnecting has its own button.
                    button.Cursor = Cursors.Default;
                }
                else
                {
                    button.MouseUp += (s, e) =>
                    {
                        var id = ((SessionButton)s).SessionId;
                        if (e.Button == MouseButtons.Left)
                        {
                            SessionClicked?.Invoke(this, id);
                            ReturnFocus();
                        }
                        else if (e.Button == MouseButtons.Right)
                        {
                            ShowSessionMenu((SessionButton)s);
                        }
                    };
                }
                _sessionButtons.Add(button);
                Controls.Add(button);
            }

            RefreshSessionButtons();
            UpdateToolTexts();
            LayoutItems();
            ResumeLayout();
        }

        private void RefreshSessionButtons()
        {
            var compact = IsCompact;
            for (var i = 0; i < _sessionButtons.Count; i++)
            {
                var button = _sessionButtons[i];
                var session = _sessions.Find(button.SessionId);
                if (session == null) continue;

                button.Compact = compact;
                button.Caption = compact ? (i + 1).ToString() : session.Info.Name;
                button.ColorTag = ParseColor(session.Info.Color);
                button.State = session.State;
                button.Active = ReferenceEquals(session, _sessions.Active);
                button.Invalidate();
                _tips.SetToolTip(button, DescribeSession(session));
            }
        }

        private static string DescribeSession(ISession session)
        {
            string state;
            switch (session.State)
            {
                case SessionState.Connecting: state = Loc.T("Panel.StateConnecting"); break;
                case SessionState.Connected: state = Loc.T("Panel.StateConnected"); break;
                default: state = Loc.T("Panel.StateDisconnected"); break;
            }

            var text = session.Info.Name + "\n" + session.Info.Host + ":" + session.Info.Port + "\n" + state;
            if (session.State == SessionState.Disconnected && !string.IsNullOrEmpty(session.DisconnectReason))
                text += "\n" + session.DisconnectReason;
            return text;
        }

        private static Color ParseColor(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return Color.Gray;
            try { return ColorTranslator.FromHtml(html); }
            catch (Exception) { return Color.Gray; }
        }

        // ---- layout ----

        private bool IsCompact => !_logic.IsHorizontal && _logic.Size < CompactLimit;

        private void UpdateToolTexts()
        {
            var horizontal = _logic.IsHorizontal;
            _newButton.Text = horizontal ? Loc.T("Panel.NewConnectionMenuButton") : "+";
            _positionButton.Text = horizontal ? Loc.T("Panel.PositionMenuButton") : "...";
            _reconnectButton.Text = horizontal ? Loc.T("Panel.Reconnect") : "↻";
            _pinButton.Highlighted = _userPinned;
            _fullscreenButton.Kind = _isFullscreen ? IconKind.ExitFullscreen : IconKind.EnterFullscreen;
            _watermarkButton.Highlighted = _watermarkEnabled;
            _pinButton.Invalidate();
            _watermarkButton.Invalidate();
            _fullscreenButton.Invalidate();

            _warningButton.Text = horizontal ? Loc.T("Panel.WarningButton") : "!";
            _tips.SetToolTip(_warningButton, _warning);

            _tips.SetToolTip(_newButton, Loc.T("Panel.NewConnection"));
            _tips.SetToolTip(_positionButton, Loc.T("Panel.PanelPosition"));
            _tips.SetToolTip(_reconnectButton, Loc.T("Panel.Reconnect"));
            _tips.SetToolTip(_pinButton, _userPinned ? Loc.T("Panel.Unpin") : Loc.T("Panel.Pin"));
            _tips.SetToolTip(_watermarkButton, _watermarkEnabled ? Loc.T("Panel.HideSessionName") : Loc.T("Panel.ShowSessionName"));
            _tips.SetToolTip(_fullscreenButton, _isFullscreen ? Loc.T("Panel.LeaveFullscreen") : Loc.T("Panel.EnterFullscreen"));
            _tips.SetToolTip(_closeButton, _mode == PanelMode.Single ? Loc.T("Panel.CloseWindow") : Loc.T("Panel.CloseApp"));
        }

        private void LayoutItems()
        {
            SuspendLayout();
            var horizontal = _logic.IsHorizontal;
            var across = _logic.Size - 2 * ItemMargin;
            var pos = ItemMargin;

            RefreshSessionButtons();
            foreach (var button in _sessionButtons)
                pos = Place(button, horizontal ? SessionLength : VerticalItemLength, horizontal, across, pos);

            pos += 3 * Gap;
            foreach (var tool in new Control[] { _newButton, _positionButton, _reconnectButton, _warningButton, _pinButton, _watermarkButton, _fullscreenButton, _closeButton })
            {
                // Not tool.Visible: that is false for every child while the panel itself is hidden.
                if (!IsToolShown(tool)) continue;

                int length;
                if (tool is IconButton) length = across; // square
                else length = horizontal ? TextRenderer.MeasureText(tool.Text, Font).Width + 24 : VerticalItemLength;
                pos = Place(tool, length, horizontal, across, pos);
            }
            ResumeLayout();
        }

        private bool IsToolShown(Control tool)
        {
            if (tool == _warningButton) return _warning != null;
            if (tool == _reconnectButton) return _mode == PanelMode.Single;
            if (tool == _newButton || tool == _pinButton || tool == _watermarkButton) return _mode == PanelMode.Full;
            return true;
        }

        private static int Place(Control control, int along, bool horizontal, int across, int pos)
        {
            if (horizontal) control.SetBounds(pos, ItemMargin, along, across);
            else control.SetBounds(ItemMargin, pos, across, along);
            return pos + along + Gap;
        }

        // ---- menus ----

        private void ShowNewConnectionMenu()
        {
            var menu = new ContextMenuStrip();

            foreach (var info in _config.Connections.Where(c => string.IsNullOrWhiteSpace(c.Group)))
                menu.Items.Add(CreateConnectionItem(info));

            foreach (var group in _config.Connections.Where(c => !string.IsNullOrWhiteSpace(c.Group)).GroupBy(c => c.Group))
            {
                var groupItem = new ToolStripMenuItem(group.Key);
                foreach (var info in group) groupItem.DropDownItems.Add(CreateConnectionItem(info));
                menu.Items.Add(groupItem);
            }

            if (menu.Items.Count > 0) menu.Items.Add(new ToolStripSeparator());

            var manage = new ToolStripMenuItem(Loc.T("Panel.ManageConnections"));
            // Deferred so the menu is fully closed before a modal dialog opens.
            manage.Click += (s, e) => BeginInvoke((Action)(() => ConnectionsRequested?.Invoke(this, EventArgs.Empty)));
            menu.Items.Add(manage);

            ShowMenu(menu, _newButton);
        }

        private ToolStripMenuItem CreateConnectionItem(ConnectionInfo info)
        {
            var item = new ToolStripMenuItem(info.Name) { Checked = _sessions.Find(info.Id) != null };
            item.Click += (s, e) =>
            {
                NewConnectionRequested?.Invoke(this, info);
                ReturnFocus();
            };
            return item;
        }

        private void ShowSessionMenu(SessionButton button)
        {
            var id = button.SessionId;
            var session = _sessions.Find(id);
            if (session == null) return;

            var menu = new ContextMenuStrip();
            if (session.State == SessionState.Disconnected)
            {
                var reconnect = new ToolStripMenuItem(Loc.T("Panel.Reconnect"));
                reconnect.Click += (s, e) => SessionClicked?.Invoke(this, id);
                menu.Items.Add(reconnect);
            }

            var detach = new ToolStripMenuItem(Loc.T("Panel.Detach"));
            // Deferred for the same reason as closing: the owner closes this session.
            detach.Click += (s, e) => BeginInvoke((Action)(() => SessionDetachRequested?.Invoke(this, id)));
            menu.Items.Add(detach);

            var close = new ToolStripMenuItem(Loc.T("Panel.CloseSession"));
            // Deferred: closing rebuilds (disposes) the session buttons, including the one this menu is anchored to.
            close.Click += (s, e) => BeginInvoke((Action)(() => SessionCloseRequested?.Invoke(this, id)));
            menu.Items.Add(close);

            ShowMenu(menu, button);
        }

        private void ShowPositionMenu()
        {
            var menu = new ContextMenuStrip();
            AddEdgeItem(menu, Loc.T("Edge.Top"), PanelEdge.Top);
            AddEdgeItem(menu, Loc.T("Edge.Bottom"), PanelEdge.Bottom);
            AddEdgeItem(menu, Loc.T("Edge.Left"), PanelEdge.Left);
            AddEdgeItem(menu, Loc.T("Edge.Right"), PanelEdge.Right);

            // The settings dialog edits and saves the shared config; a secondary window only moves its panel.
            if (_mode == PanelMode.Full)
            {
                menu.Items.Add(new ToolStripSeparator());

                var settings = new ToolStripMenuItem(Loc.T("Panel.Settings"));
                // Deferred so the menu is fully closed before a modal dialog opens.
                settings.Click += (s, e) => BeginInvoke((Action)(() => SettingsRequested?.Invoke(this, EventArgs.Empty)));
                menu.Items.Add(settings);
            }

            ShowMenu(menu, _positionButton);
        }

        private void AddEdgeItem(ContextMenuStrip menu, string text, PanelEdge edge)
        {
            var item = new ToolStripMenuItem(text) { Checked = _logic.Edge == edge };
            item.Click += (s, e) =>
            {
                EdgeChangeRequested?.Invoke(this, edge);
                ReturnFocus();
            };
            menu.Items.Add(item);
        }

        private void ShowMenu(ContextMenuStrip menu, Control anchor)
        {
            _menuOpen = true;
            _openMenu = menu;
            _menuAwaySince = null;
            menu.Closed += (s, e) =>
            {
                _menuOpen = false;
                _openMenu = null;
                BeginInvoke((Action)menu.Dispose);
                ReturnFocus();
            };

            switch (_logic.Edge)
            {
                case PanelEdge.Bottom:
                    menu.Show(anchor, new Point(0, 0), ToolStripDropDownDirection.AboveRight);
                    break;
                case PanelEdge.Left:
                    menu.Show(anchor, new Point(anchor.Width, 0), ToolStripDropDownDirection.BelowRight);
                    break;
                case PanelEdge.Right:
                    menu.Show(anchor, new Point(0, 0), ToolStripDropDownDirection.BelowLeft);
                    break;
                default:
                    menu.Show(anchor, new Point(0, anchor.Height), ToolStripDropDownDirection.BelowRight);
                    break;
            }
        }

        private ToolButton AddToolButton(Action click)
        {
            var button = new ToolButton();
            button.Click += (s, e) => click();
            Controls.Add(button);
            return button;
        }

        private IconButton AddIconButton(IconKind kind, Action click)
        {
            var button = new IconButton { Kind = kind };
            button.Click += (s, e) => click();
            Controls.Add(button);
            return button;
        }

        // ---- controls ----

        private enum IconKind { Pin, Watermark, EnterFullscreen, ExitFullscreen, Close }

        /// <summary>Square button with a vector icon; never takes keyboard focus.</summary>
        private sealed class IconButton : Control
        {
            private bool _hover;

            public IconButton()
            {
                SetStyle(ControlStyles.Selectable, false);
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
                TabStop = false;
                Cursor = Cursors.Hand;
            }

            public IconKind Kind { get; set; }

            /// <summary>Accent background (pinned state).</summary>
            public bool Highlighted { get; set; }

            public Color HoverColor { get; set; } = Color.FromArgb(75, 75, 75);

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                var back = _hover ? HoverColor : (Highlighted ? AccentColor : ButtonColor);
                using (var brush = new SolidBrush(back)) g.FillRectangle(brush, ClientRectangle);

                // Small panels (size 32 gives 20 px buttons) need a thin margin or the glyph shrinks to a dot.
                var inset = Math.Max(4, Width / 5);
                // A 2 px pen centred on an integer coordinate covers exactly two pixel columns, so straight lines stay
                // sharp; the rectangle is symmetric for that pen (no "-1"). Flat caps keep the line ends crisp too.
                var r = new Rectangle(inset, inset, Width - 2 * inset, Height - 2 * inset);
                using (var pen = new Pen(Color.White, 2f))
                {
                    switch (Kind)
                    {
                        case IconKind.Close:
                            // Only diagonals benefit from round caps.
                            pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                            pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                            g.DrawLine(pen, r.Left, r.Top, r.Right, r.Bottom);
                            g.DrawLine(pen, r.Right, r.Top, r.Left, r.Bottom);
                            break;
                        case IconKind.EnterFullscreen:
                            DrawCorners(g, pen, r, false);
                            break;
                        case IconKind.ExitFullscreen:
                            DrawCorners(g, pen, r, true);
                            break;
                        case IconKind.Watermark:
                            DrawWatermark(g, pen, r);
                            break;
                        default:
                            DrawPin(g, pen, r);
                            break;
                    }
                }
            }

            // Four L-shapes: elbows on the outer corners (enter) or pointing toward the centre (exit).
            private static void DrawCorners(Graphics g, Pen pen, Rectangle r, bool inward)
            {
                var k = Math.Max(3, r.Width / 3);
                var l = r.Left; var t = r.Top; var rt = r.Right; var b = r.Bottom;
                if (!inward)
                {
                    g.DrawLines(pen, new[] { new Point(l, t + k), new Point(l, t), new Point(l + k, t) });
                    g.DrawLines(pen, new[] { new Point(rt - k, t), new Point(rt, t), new Point(rt, t + k) });
                    g.DrawLines(pen, new[] { new Point(l, b - k), new Point(l, b), new Point(l + k, b) });
                    g.DrawLines(pen, new[] { new Point(rt - k, b), new Point(rt, b), new Point(rt, b - k) });
                }
                else
                {
                    g.DrawLines(pen, new[] { new Point(l, t + k), new Point(l + k, t + k), new Point(l + k, t) });
                    g.DrawLines(pen, new[] { new Point(rt - k, t), new Point(rt - k, t + k), new Point(rt, t + k) });
                    g.DrawLines(pen, new[] { new Point(l, b - k), new Point(l + k, b - k), new Point(l + k, b) });
                    g.DrawLines(pen, new[] { new Point(rt - k, b), new Point(rt - k, b - k), new Point(rt, b - k) });
                }
            }

            // Screen outline with a short label line in its lower-right corner.
            private static void DrawWatermark(Graphics g, Pen pen, Rectangle r)
            {
                g.DrawRectangle(pen, r.Left, r.Top + r.Height / 6, r.Width, r.Height * 2 / 3);
                var y = r.Top + r.Height / 6 + r.Height * 2 / 3 - Math.Max(3, r.Height / 6);
                g.DrawLine(pen, r.Left + r.Width / 2, y, r.Right - Math.Max(3, r.Width / 8), y);
            }

            // Thumbtack: round head on top, needle below.
            private static void DrawPin(Graphics g, Pen pen, Rectangle r)
            {
                var head = r.Width / 2;
                var cx = r.Left + r.Width / 2;
                var headRect = new Rectangle(cx - head / 2, r.Top, head, head);
                g.DrawEllipse(pen, headRect);
                g.DrawLine(pen, cx - head, headRect.Bottom + 1, cx + head, headRect.Bottom + 1);
                g.DrawLine(pen, cx, headRect.Bottom + 1, cx, r.Bottom);
            }
        }

        /// <summary>Flat button that never takes keyboard focus, so clicking it does not pull input out of the RDP session.</summary>
        private sealed class ToolButton : Button
        {
            public ToolButton()
            {
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                UseVisualStyleBackColor = false;
                BackColor = ButtonColor;
                ForeColor = Color.White;
            }
        }

        /// <summary>One session: colour tag, name (or number when compact), state dot, highlight when active.</summary>
        private sealed class SessionButton : Control
        {
            private bool _hover;

            public SessionButton()
            {
                SetStyle(ControlStyles.Selectable, false);
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
                TabStop = false;
                Cursor = Cursors.Hand;
            }

            public string SessionId { get; set; }
            public string Caption { get; set; } = "";
            public Color ColorTag { get; set; } = Color.Gray;
            public SessionState State { get; set; }
            public bool Active { get; set; }
            public bool Compact { get; set; }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                var back = Active ? AccentColor : (_hover ? Color.FromArgb(75, 75, 75) : ButtonColor);
                using (var brush = new SolidBrush(back)) g.FillRectangle(brush, ClientRectangle);
                using (var brush = new SolidBrush(ColorTag)) g.FillRectangle(brush, 0, 0, 4, Height);

                var dot = new Rectangle(Compact ? Width - 12 : Width - 16, Compact ? 4 : (Height - 8) / 2, 8, 8);
                using (var brush = new SolidBrush(StateColor())) g.FillEllipse(brush, dot);

                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                Rectangle textRect;
                if (Compact)
                {
                    flags |= TextFormatFlags.HorizontalCenter;
                    textRect = new Rectangle(4, 0, Width - 4, Height);
                }
                else
                {
                    flags |= TextFormatFlags.Left;
                    textRect = new Rectangle(12, 0, Width - 12 - 22, Height);
                }
                TextRenderer.DrawText(g, Caption, Font, textRect, Color.White, flags);
            }

            private Color StateColor()
            {
                switch (State)
                {
                    case SessionState.Connected: return Color.FromArgb(54, 179, 126);
                    case SessionState.Connecting: return Color.FromArgb(255, 171, 0);
                    default: return Color.FromArgb(222, 53, 11);
                }
            }
        }
    }
}
