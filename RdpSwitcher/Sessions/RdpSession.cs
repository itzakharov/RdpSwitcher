using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AxMSTSCLib;
using MSTSCLib;
using RdpSwitcher.Config;

namespace RdpSwitcher.Sessions
{
    /// <summary>
    /// ISession over AxMsRdpClient (mstscax.dll). UI thread only. Knows nothing about the panel or hotkeys.
    /// The control is added to <c>parent</c> (sized to its client area) here because AxHost properties need a created control.
    /// Credentials: only the user name from the config is set; the password is typed by the user in the
    /// standard Windows window and is never touched, stored or logged here.
    /// </summary>
    public sealed class RdpSession : ISession
    {
        private readonly Control _parent;
        private readonly AxMsRdpClient9NotSafeForScripting _client;
        private DisplaySettings _display;
        private Padding _insets;
        private bool _disposed;

        /// <param name="parent">Form (or panel) that hosts the RDP controls. Its handle must already exist.</param>
        /// <param name="info">Connection to open.</param>
        /// <param name="display">Remote desktop size (physical pixels) and scale factors.</param>
        /// <param name="insets">Part of the parent's client area the control must leave free (the owner decides why).</param>
        public RdpSession(Control parent, ConnectionInfo info, DisplaySettings display, Padding insets = default(Padding))
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
            Info = info ?? throw new ArgumentNullException(nameof(info));
            _display = display ?? throw new ArgumentNullException(nameof(display));
            _insets = insets;

            _client = new AxMsRdpClient9NotSafeForScripting();
            ((ISupportInitialize)_client).BeginInit();
            // Explicit bounds instead of Dock.Fill: with a hidden AxHost added to an already full-screen form
            // the docked size was not applied (the control stayed tiny in the top-right corner).
            _client.Dock = DockStyle.None;
            _client.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _client.Bounds = ViewBounds();
            _client.Visible = false;
            _parent.Controls.Add(_client);
            ((ISupportInitialize)_client).EndInit();
            _client.Bounds = ViewBounds();
            _parent.SizeChanged += OnParentSizeChanged;

            // The built-in connection bar is off: full screen is provided by our form, not by the control.
            _client.AdvancedSettings9.DisplayConnectionBar = false;
            _client.AdvancedSettings9.SmartSizing = true;
            _client.AdvancedSettings9.EnableCredSspSupport = true;
            SetKeyboardHookMode();

            _client.OnLoginComplete += OnLoginComplete;
            _client.OnDisconnected += OnDisconnected;
        }

        public string Id => Info.Id;

        public ConnectionInfo Info { get; }

        public SessionState State { get; private set; } = SessionState.Disconnected;

        public string DisconnectReason { get; private set; }

        public event EventHandler StateChanged;

        /// <summary>Display geometry used by the next Connect(). To change a live connection use UpdateDisplay().</summary>
        public DisplaySettings Display => _display;

        /// <summary>
        /// Stores new display settings and, if the session is connected, resizes the remote desktop in place
        /// (UpdateSessionDisplaySettings). Without a live connection the settings apply on the next Connect().
        /// </summary>
        public void UpdateDisplay(DisplaySettings display)
        {
            EnsureUiThread();
            if (display == null) throw new ArgumentNullException(nameof(display));
            if (_disposed || display.Equals(_display)) return;

            _display = display;
            if (State != SessionState.Connected) return;

            try
            {
                var ocx = (IMsRdpClient9)_client.GetOcx();
                ocx.UpdateSessionDisplaySettings(
                    (uint)display.Size.Width, (uint)display.Size.Height,
                    (uint)display.PhysicalSizeMm.Width, (uint)display.PhysicalSizeMm.Height,
                    0, (uint)display.DesktopScaleFactor, (uint)display.DeviceScaleFactor);
            }
            catch (COMException)
            {
                // Server without dynamic resolution support: SmartSizing keeps the picture usable until reconnect.
            }
        }

        public void Connect()
        {
            EnsureUiThread();
            if (_disposed || State != SessionState.Disconnected) return;

            _client.Server = Info.Host;
            _client.AdvancedSettings9.RDPPort = Info.Port;
            if (!string.IsNullOrEmpty(Info.User)) _client.UserName = Info.User;
            _client.DesktopWidth = _display.Size.Width;
            _client.DesktopHeight = _display.Size.Height;
            SetScaleFactors();

            DisconnectReason = null;
            SetState(SessionState.Connecting);
            try
            {
                _client.Connect();
            }
            catch (COMException ex)
            {
                DisconnectReason = "Connect failed: " + ex.Message;
                SetState(SessionState.Disconnected);
            }
        }

        public void Disconnect()
        {
            EnsureUiThread();
            if (_disposed) return;

            try
            {
                // Connected: 0 = not connected, 1 = connected, 2 = connecting.
                if (_client.Connected != 0) _client.Disconnect();
            }
            catch (COMException)
            {
                // Already gone; OnDisconnected (if it comes) fixes the state.
            }
        }

        public void Show()
        {
            EnsureUiThread();
            _client.Bounds = ViewBounds();
            _client.Visible = true;
            _client.BringToFront();
        }

        private void OnParentSizeChanged(object sender, EventArgs e)
        {
            if (!_disposed) _client.Bounds = ViewBounds();
        }

        /// <summary>
        /// Margins kept free inside the parent. Moves the control at once; the remote desktop size is changed
        /// separately by the owner through UpdateDisplay().
        /// </summary>
        public Padding Insets
        {
            get => _insets;
            set
            {
                EnsureUiThread();
                if (_disposed || value == _insets) return;

                _insets = value;
                _client.Bounds = ViewBounds();
            }
        }

        private Rectangle ViewBounds()
        {
            var area = _parent.ClientRectangle;
            return new Rectangle(
                area.X + _insets.Left, area.Y + _insets.Top,
                Math.Max(0, area.Width - _insets.Horizontal), Math.Max(0, area.Height - _insets.Vertical));
        }

        public void Hide()
        {
            EnsureUiThread();
            _client.Visible = false;
        }

        public void FocusInput()
        {
            EnsureUiThread();
            if (_client.Visible) _client.Focus();
        }

        public void Dispose()
        {
            if (_disposed) return;
            EnsureUiThread();

            _parent.SizeChanged -= OnParentSizeChanged;
            _client.OnLoginComplete -= OnLoginComplete;
            _client.OnDisconnected -= OnDisconnected;
            try
            {
                if (_client.Connected != 0) _client.Disconnect();
            }
            catch (COMException) { }

            _disposed = true;
            _parent.Controls.Remove(_client);
            _client.Dispose();
        }

        private void OnLoginComplete(object sender, EventArgs e)
        {
            if (State == SessionState.Connecting) SetState(SessionState.Connected);
        }

        private void OnDisconnected(object sender, IMsTscAxEvents_OnDisconnectedEvent e)
        {
            DisconnectReason = Describe(e.discReason);
            SetState(SessionState.Disconnected);
        }

        private string Describe(int reason)
        {
            string text = null;
            try
            {
                text = _client.GetErrorDescription((uint)reason, (uint)_client.ExtendedDisconnectReason);
            }
            catch (COMException) { }
            catch (AxHost.InvalidActiveXStateException) { }

            return string.IsNullOrWhiteSpace(text) ? "Code " + reason : "Code " + reason + ": " + text;
        }

        // KeyboardHookMode = 1: Win, Alt+Tab etc. always go to the remote machine (2 would mean "only when the control is full screen").
        private void SetKeyboardHookMode()
        {
            var ocx = (IMsRdpClient9)_client.GetOcx();
            ocx.SecuredSettings2.KeyboardHookMode = 1;
        }

        // Scale factors are not on the typed settings interfaces; they are extended properties of the control.
        private void SetScaleFactors()
        {
            try
            {
                var extended = (IMsRdpExtendedSettings)_client.GetOcx();
                object desktop = (uint)_display.DesktopScaleFactor;
                object device = (uint)_display.DeviceScaleFactor;
                extended.set_Property("DesktopScaleFactor", ref desktop);
                extended.set_Property("DeviceScaleFactor", ref device);
            }
            catch (COMException)
            {
                // Older client without these properties: the desktop is simply not scaled.
            }
        }

        private void SetState(SessionState state)
        {
            State = state;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void EnsureUiThread()
        {
            if (_client.IsHandleCreated && _client.InvokeRequired)
                throw new InvalidOperationException("RdpSession must be used from the UI thread.");
        }
    }
}
