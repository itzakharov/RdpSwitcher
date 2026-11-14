using System;
using RdpSwitcher.Config;

namespace RdpSwitcher.Sessions
{
    /// <summary>
    /// One RDP session as seen by SessionManager. Implementations (RdpSession) must be used from the UI thread only
    /// and must not know about the panel or hotkeys.
    /// </summary>
    public interface ISession : IDisposable
    {
        /// <summary>Same as Info.Id.</summary>
        string Id { get; }

        ConnectionInfo Info { get; }

        SessionState State { get; }

        /// <summary>Human-readable cause of the last disconnect (code + description), or null.</summary>
        string DisconnectReason { get; }

        /// <summary>Raised on the UI thread after State or DisconnectReason changed.</summary>
        event EventHandler StateChanged;

        /// <summary>Starts connecting (also used to reconnect after a disconnect).</summary>
        void Connect();

        void Disconnect();

        /// <summary>Makes the session visible on top of the others.</summary>
        void Show();

        void Hide();

        /// <summary>Moves keyboard focus into the remote screen.</summary>
        void FocusInput();
    }
}
