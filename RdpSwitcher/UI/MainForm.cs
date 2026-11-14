using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using RdpSwitcher.Config;
using RdpSwitcher.Localization;
using RdpSwitcher.Native;
using RdpSwitcher.Sessions;

namespace RdpSwitcher.UI
{
    /// <summary>
    /// Borderless full-screen window that hosts the RDP controls. Sessions are owned by SessionManager;
    /// this form only creates them (through the factory), goes full screen and opens autoConnect sessions.
    /// </summary>
    public class MainForm : Form
    {
        private readonly AppConfig _config;
        private readonly ConfigStore _store;

        private bool _fullscreen;
        private Rectangle _restoreBounds;
        private FormWindowState _restoreState;

        private readonly Queue<ConnectionInfo> _autoQueue = new Queue<ConnectionInfo>();
        private ISession _waitingFor;
        private readonly HashSet<ISession> _attempting = new HashSet<ISession>();
        private string _firstAutoId;
        private const int DetachTimeoutMs = 5000;
        private readonly HashSet<string> _detaching = new HashSet<string>();
        private readonly EdgePanel _panel;
        private readonly Label _hint = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(150, 150, 150),
            BackColor = Color.Black,
            Font = new Font("Segoe UI", 14f)
        };
        private readonly HotkeyManager _hotkeys;
        private readonly Timer _resizeTimer;
        private readonly WatermarkForm _watermark = new WatermarkForm();
        private bool _closing;

        // Secondary window: the one connection it shows, and the monitor for its first full-screen entry.
        private readonly ConnectionInfo _single;
        private Screen _startScreen;

        /// <param name="single">Not null: secondary window for this connection only (no hotkeys, no autoConnect, config is not saved).</param>
        /// <param name="monitor">Zero-based monitor for the first full-screen entry of a secondary window; null = under the cursor.</param>
        public MainForm(AppConfig config, ConfigStore store, ConnectionInfo single = null, int? monitor = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _single = single;

            Text = single == null ? "RdpSwitcher" : "RdpSwitcher: " + single.Name;
            BackColor = Color.Black;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1024, 768);

            var screens = Screen.AllScreens;
            if (single != null && monitor != null && monitor.Value < screens.Length)
            {
                // Created on that monitor already, so the restored (non-full-screen) window also stays there.
                _startScreen = screens[monitor.Value];
                var area = _startScreen.WorkingArea;
                var size = new Size(Math.Min(Size.Width, area.Width), Math.Min(Size.Height, area.Height));
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(
                    area.Left + (area.Width - size.Width) / 2, area.Top + (area.Height - size.Height) / 2,
                    size.Width, size.Height);
            }

            // A second logon pushes the first one out, so the primary refuses to connect what a secondary window holds.
            // The secondary window itself is the holder and has nothing to check.
            Sessions = new SessionManager(
                info => new RdpSession(this, info, CurrentDisplay(), CurrentInsets()),
                IsSecondary ? (Func<ConnectionInfo, bool>)null : info => SecondaryWindows.IsOpen(info.Id));
            Sessions.SessionStateChanged += OnSessionStateChanged;
            // Deferred: a modal box must not run inside the click handler or COM event that led here.
            Sessions.ConnectBlocked += (s, info) => BeginInvoke((Action)(() =>
                MessageBox.Show(this, Loc.T("Main.ConnectBlocked", info.Name), "RdpSwitcher",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning)));

            _panel = new EdgePanel(Sessions, _config, IsSecondary ? PanelMode.Single : PanelMode.Full);
            Controls.Add(_panel);
            // Added after the panel, so it lies below it; session controls cover it once they exist.
            Controls.Add(_hint);
            Sessions.SessionsChanged += (s, e) => UpdateHint();
            UpdateHint();
            _panel.PinChanged += (s, e) =>
            {
                _config.Panel.Pinned = _panel.Pinned;
                SaveAndApplySettings();
            };
            // Reconnect() connects only a disconnected session and activates it; other sessions are untouched.
            _panel.SessionClicked += (s, id) => Sessions.Reconnect(id);
            _panel.SessionCloseRequested += (s, id) => Sessions.Close(id);
            _panel.SessionDetachRequested += (s, id) => DetachToWindow(id);
            _panel.NewConnectionRequested += (s, info) => OpenConnection(info);
            _panel.ToggleFullscreenRequested += (s, e) => ToggleFullscreen();
            _panel.EdgeChangeRequested += OnEdgeChangeRequested;
            _panel.SettingsRequested += OnSettingsRequested;
            _panel.ConnectionsRequested += OnConnectionsRequested;
            _panel.WatermarkEnabled = _config.Watermark.Enabled;
            _panel.WatermarkToggleRequested += (s, e) =>
            {
                _config.Watermark.Enabled = !_config.Watermark.Enabled;
                SaveAndApplySettings();
            };
            // The label would cover the panel when both sit in the same corner, so it steps aside while the panel is shown.
            _panel.VisibleChanged += (s, e) => UpdateWatermark();
            Sessions.ActiveChanged += (s, e) => UpdateWatermark();
            Sessions.SessionsChanged += (s, e) => UpdateWatermark();
            LocationChanged += (s, e) => UpdateWatermark();
            // Deferred: the form must not be closed (and the panel disposed) inside the panel's own click handler.
            _panel.CloseRequested += (s, e) => BeginInvoke((Action)Close);
            FullscreenChanged += (s, e) =>
            {
                _panel.IsFullscreen = _fullscreen;
                UpdateHotkeys();
                ApplyDisplayToSessions();
                UpdateWatermark();
            };
            // The form moved to a monitor with another scale (PerMonitorV2).
            DpiChanged += (s, e) => ApplyDisplayToSessions();
            // Window resizing fires SizeChanged continuously; resize the remote desktop once, after the user stops.
            _resizeTimer = new Timer { Interval = 400 };
            _resizeTimer.Tick += (s, e) =>
            {
                _resizeTimer.Stop();
                ApplyDisplayToSessions();
            };
            SizeChanged += (s, e) =>
            {
                if (_fullscreen || !IsHandleCreated) return;
                _resizeTimer.Stop();
                _resizeTimer.Start();
            };

            // A secondary window never grabs global hotkeys: the primary process keeps them.
            if (!IsSecondary)
            {
                _hotkeys = new HotkeyManager();
                _hotkeys.HotkeyPressed += OnHotkeyPressed;
                Sessions.ActiveChanged += (s, e) => { if (_fullscreen) _hotkeys.Reinstall(); };
            }

            Shown += (s, e) =>
            {
                EnterFullscreen();
                if (IsSecondary) Sessions.Open(_single);
                else StartAutoConnect();
            };
        }

        public bool IsSecondary => _single != null;

        public SessionManager Sessions { get; }

        public EdgePanel Panel => _panel;

        public AppConfig Config => _config;

        public ConfigStore Store => _store;

        public bool IsFullscreen => _fullscreen;

        public event EventHandler FullscreenChanged;

        /// <summary>Opens (or just activates) a session for the connection.</summary>
        public ISession OpenConnection(ConnectionInfo info)
        {
            return Sessions.Open(info);
        }

        // Moving a session out: disconnect it here, wait until the server side has been released (otherwise the
        // new process could race with the old logon), close it, start RdpSwitcher.exe --connection <id>.
        private void DetachToWindow(string id)
        {
            var session = Sessions.Find(id);
            if (session == null || IsSecondary) return;

            var info = session.Info;
            if (SecondaryWindows.IsOpen(info.Id))
            {
                // Nothing is closed here: the session stays where it is.
                MessageBox.Show(this, Loc.T("Main.AlreadyDetached", info.Name), "RdpSwitcher",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                _panel.ReturnFocus();
                return;
            }

            Guid parsed;
            if (!Guid.TryParse(info.Id, out parsed))
            {
                // The secondary process only accepts GUIDs; refuse before the session is closed.
                MessageBox.Show(this, Loc.T("Main.IdNotGuid", info.Name),
                    "RdpSwitcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _panel.ReturnFocus();
                return;
            }

            if (!_detaching.Add(info.Id)) return;

            // A user-requested disconnect during Connecting is not a failed attempt: no error box.
            _attempting.Remove(session);

            if (session.State == SessionState.Disconnected)
            {
                FinishDetach(info);
                return;
            }

            var timer = new Timer { Interval = DetachTimeoutMs };
            var finished = false;
            EventHandler onState = null;
            Action finish = () =>
            {
                if (finished) return;
                finished = true;
                session.StateChanged -= onState;
                timer.Dispose();
                FinishDetach(info);
            };
            onState = (s, e) =>
            {
                if (session.State == SessionState.Disconnected && !_closing) BeginInvoke(finish);
            };
            timer.Tick += (s, e) => finish();
            session.StateChanged += onState;
            timer.Start();
            session.Disconnect();
        }

        private void FinishDetach(ConnectionInfo info)
        {
            _detaching.Remove(info.Id);
            if (_closing || IsDisposed) return;

            var current = Screen.FromControl(this);
            var target = PickDetachScreen(current);
            Sessions.Close(info.Id);

            try
            {
                SecondaryWindows.Launch(info.Id, SecondaryWindows.IndexOf(target));
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            {
                MessageBox.Show(this, Loc.T("Main.DetachFailed", ex.Message), "RdpSwitcher",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Same monitor: a TopMost full-screen primary would cover the new window and its credential prompt.
            // UpdateTopMost() restores it on the next session state change.
            if (target.Equals(current)) TopMost = false;
            _panel.ReturnFocus();
        }

        // The point of a separate window is usually another monitor: take the first one that is not ours.
        private static Screen PickDetachScreen(Screen current)
        {
            return Screen.AllScreens.FirstOrDefault(s => !s.Equals(current)) ?? current;
        }

        public void ToggleFullscreen()
        {
            if (_fullscreen) LeaveFullscreen();
            else EnterFullscreen();
        }

        /// <summary>Borderless, covers the monitor under the cursor, TopMost (see UpdateTopMost).</summary>
        public void EnterFullscreen()
        {
            if (_fullscreen) return;

            _restoreState = WindowState;
            if (WindowState == FormWindowState.Normal) _restoreBounds = Bounds;
            else
            {
                _restoreBounds = RestoreBounds;
                WindowState = FormWindowState.Normal;
            }

            var screen = _startScreen ?? Screen.FromPoint(Cursor.Position);
            _startScreen = null;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = screen.Bounds;
            _fullscreen = true;
            UpdateTopMost();
            FullscreenChanged?.Invoke(this, EventArgs.Empty);
        }

        public void LeaveFullscreen()
        {
            if (!_fullscreen) return;

            _fullscreen = false;
            TopMost = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            Bounds = _restoreBounds;
            WindowState = _restoreState;
            FullscreenChanged?.Invoke(this, EventArgs.Empty);
        }

        // Remote desktop = whole monitor in physical pixels (the process is PerMonitorV2-aware, so Screen.Bounds
        // is physical) plus the scale of that monitor, so remote UI has the same apparent size as local.
        // In a window the remote desktop follows the client area, otherwise SmartSizing would squash the picture.
        // With panel.pinMode = reserve the pinned panel's strip is taken out of the remote desktop.
        private DisplaySettings CurrentDisplay()
        {
            var size = _fullscreen ? Screen.FromControl(this).Bounds.Size : ClientSize;
            var insets = CurrentInsets();
            return DisplaySettings.ForMonitor(new Size(size.Width - insets.Horizontal, size.Height - insets.Vertical), DeviceDpi);
        }

        // Only the user's pin reserves space: the panel shown on hover, or because there are no sessions, never does.
        private Padding CurrentInsets()
        {
            if (IsSecondary || _config.Panel.PinMode != PinMode.Reserve || !_panel.Pinned) return Padding.Empty;

            var area = ClientRectangle;
            var rest = EdgePanelLogic.ComputeRemaining(area, _config.Panel.Edge, _config.Panel.Size);
            return new Padding(rest.Left - area.Left, rest.Top - area.Top, area.Right - rest.Right, area.Bottom - rest.Bottom);
        }

        private void ApplyDisplayToSessions()
        {
            var display = CurrentDisplay();
            var insets = CurrentInsets();
            foreach (var session in Sessions.Sessions.OfType<RdpSession>())
            {
                session.Insets = insets;
                session.UpdateDisplay(display);
            }
        }

        // A TopMost form would hide the standard Windows credential window of a session that is still
        // connecting, so TopMost is on only while no session is in the Connecting state.
        private void UpdateTopMost()
        {
            TopMost = _fullscreen && !Sessions.Sessions.Any(s => s.State == SessionState.Connecting);
        }

        private void StartAutoConnect()
        {
            foreach (var info in _config.Connections.Where(c => c.AutoConnect))
                _autoQueue.Enqueue(info);
            OpenNextAuto();
        }

        // autoConnect sessions open one at a time: each credential prompt is answered before the next one starts.
        private void OpenNextAuto()
        {
            _waitingFor = null;
            if (_autoQueue.Count == 0)
            {
                if (_firstAutoId != null) Sessions.Activate(_firstAutoId);
                return;
            }

            var info = _autoQueue.Dequeue();
            var session = Sessions.Open(info);
            if (session == null)
            {
                // Blocked (open in a secondary window); the user has been told. Carry on with the rest.
                BeginInvoke((Action)OpenNextAuto);
                return;
            }
            if (_firstAutoId == null) _firstAutoId = session.Id;

            if (session.State == SessionState.Connecting) _waitingFor = session;
            else BeginInvoke((Action)OpenNextAuto);
        }

        // Shown only in full screen, for the active session, and never while a session is connecting
        // (the credential window of another process must not end up under it).
        private void UpdateWatermark()
        {
            if (_closing) return;

            var settings = _config.Watermark;
            var active = Sessions.Active;
            // A panel pinned with pinMode = reserve owns its own strip: the label moves into the remaining
            // remote-desktop area instead of hiding.
            var insets = CurrentInsets();
            var reserved = insets != Padding.Empty;
            var area = new Rectangle(Bounds.Left + insets.Left, Bounds.Top + insets.Top,
                Bounds.Width - insets.Horizontal, Bounds.Height - insets.Vertical);
            var show = _fullscreen
                && settings.Enabled
                && active != null
                && !Sessions.Sessions.Any(s => s.State == SessionState.Connecting)
                && (reserved || !(_panel.Visible && WatermarkLogic.OverlapsPanel(_config.Panel.Edge, settings.Position)));

            if (!show)
            {
                if (_watermark.Visible) _watermark.Hide();
                return;
            }

            _watermark.Present(active.Info.Name, settings.Opacity, area, settings.Position);
            if (!_watermark.Visible) _watermark.Show(this);
        }

        private void OnSessionStateChanged(object sender, EventArgs e)
        {
            UpdateTopMost();
            UpdateWatermark();
            // RDP controls install their own keyboard hook on connect; ours has to be the newest to see keys first.
            if (_fullscreen) _hotkeys?.Reinstall();

            var session = sender as ISession;
            TrackConnectFailure(session);
            if (session != null && ReferenceEquals(session, _waitingFor) && session.State != SessionState.Connecting)
            {
                _waitingFor = null;
                BeginInvoke((Action)OpenNextAuto);
            }
        }

        /// <summary>Registers the global hotkeys in full screen, releases them otherwise. Also reusable after settings change.</summary>
        public void UpdateHotkeys()
        {
            if (IsSecondary) return;

            if (!_fullscreen)
            {
                _hotkeys.Unregister();
                _panel.SetWarning(null);
                return;
            }

            var failures = _hotkeys.Register(_config.Hotkeys);
            _panel.SetWarning(failures.Count == 0
                ? null
                : Loc.T("Main.HotkeysFailed", string.Join("\n", failures)));
        }

        private void OnHotkeyPressed(object sender, HotkeyEventArgs e)
        {
            switch (e.Action)
            {
                case HotkeyAction.Next: Sessions.Next(); break;
                case HotkeyAction.Prev: Sessions.Prev(); break;
                case HotkeyAction.Jump: Sessions.ActivateIndex(e.Number - 1); break;
                case HotkeyAction.TogglePin: _panel.TogglePin(); break;
                case HotkeyAction.LeaveFullscreen: LeaveFullscreen(); break;
            }
        }

        // Connecting -> Disconnected without ever reaching Connected is a failed attempt: tell the user why.
        // The control stays in place, so the session can be retried from the panel.
        private void TrackConnectFailure(ISession session)
        {
            if (session == null) return;

            if (session.State == SessionState.Connecting)
            {
                _attempting.Add(session);
            }
            else if (_attempting.Remove(session) && session.State == SessionState.Disconnected)
            {
                var text = Loc.T("Main.ConnectFailed", session.Info.Name, session.Info.Host,
                    session.DisconnectReason ?? Loc.T("Main.ReasonUnknown"));
                // Deferred: a modal box must not run inside the COM event that reported the disconnect.
                BeginInvoke((Action)(() => MessageBox.Show(this, text, "RdpSwitcher",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning)));
            }
        }

        // The panel only reports the choice; the config is changed and saved here, then the panel re-lays itself out.
        private void OnEdgeChangeRequested(object sender, PanelEdge edge)
        {
            _config.Panel.Edge = edge;
            SaveAndApplySettings();
        }

        private void OnSettingsRequested(object sender, EventArgs e)
        {
            using (var dialog = new SettingsDialog(_config))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    dialog.ApplyTo(_config);
                    Loc.SetLanguage(_config.Language);
                    SaveAndApplySettings();
                }
            }
            _panel.ReturnFocus();
        }

        private void OnConnectionsRequested(object sender, EventArgs e)
        {
            using (var dialog = new ConnectionsDialog(_config))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    dialog.ApplyTo(_config);
                    SaveAndApplySettings();
                }
            }
            _panel.ReturnFocus();
        }

        // First start or every session closed: an empty black form explains what to do next.
        private void UpdateHint()
        {
            _hint.Visible = !IsSecondary && Sessions.Sessions.Count == 0;
            if (!_hint.Visible) return;

            string edge;
            switch (_config.Panel.Edge)
            {
                case PanelEdge.Bottom: edge = Loc.T("Main.EdgeBottom"); break;
                case PanelEdge.Left: edge = Loc.T("Main.EdgeLeft"); break;
                case PanelEdge.Right: edge = Loc.T("Main.EdgeRight"); break;
                default: edge = Loc.T("Main.EdgeTop"); break;
            }

            _hint.Text = Loc.T("Main.Hint", edge);
        }

        // Normalizes, saves and applies without restart: panel layout and hotkey registration are refreshed.
        private void SaveAndApplySettings()
        {
            _config.Normalize();
            // A secondary window applies changes to its own copy of the config for the lifetime of the window only.
            if (!IsSecondary)
            {
                try
                {
                    _store.Save(_config);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    MessageBox.Show(this, Loc.T("Main.SaveFailed", ex.Message), "RdpSwitcher",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            _panel.ApplySettings(_config.Panel);
            ApplyDisplayToSessions(); // pin, edge, size or pin mode may have changed the reserved strip
            UpdateHint();
            _panel.WatermarkEnabled = _config.Watermark.Enabled;
            UpdateWatermark();
            UpdateHotkeys();
        }

        // A second launch of the primary posts this message instead of opening another window (see Program).
        // It is broadcast, so secondary windows receive it too and must ignore it.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.ShowInstanceMessage)
            {
                if (IsSecondary) return;

                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
                _panel.ReturnFocus();
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            var active = Sessions.Sessions.Count(s => s.State != SessionState.Disconnected);
            if (active > 0 && e.CloseReason != CloseReason.WindowsShutDown)
            {
                var question = IsSecondary
                    ? Loc.T("Main.CloseSecondary", _single.Name)
                    : Loc.T("Main.CloseAll", active);
                var answer = MessageBox.Show(this, question, "RdpSwitcher",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes)
                {
                    // Nothing has been released yet, so the form simply stays as it was.
                    e.Cancel = true;
                    _panel.ReturnFocus();
                    return;
                }
            }

            // Controls must be released before the form goes away.
            _closing = true;
            _watermark.Dispose();
            Sessions.SessionStateChanged -= OnSessionStateChanged;
            _resizeTimer.Dispose();
            _hotkeys?.Dispose();
            Sessions.CloseAll();
            base.OnFormClosing(e);
        }
    }
}
