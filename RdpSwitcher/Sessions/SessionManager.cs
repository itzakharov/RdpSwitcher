using System;
using System.Collections.Generic;
using RdpSwitcher.Config;

namespace RdpSwitcher.Sessions
{
    /// <summary>
    /// Owns the sessions (in opening order) and the active one. The panel and hotkeys only ask it to change
    /// the active session; it shows the chosen session and hides the rest. No WinForms/COM types here.
    /// </summary>
    public class SessionManager
    {
        private readonly Func<ConnectionInfo, ISession> _factory;
        private readonly Func<ConnectionInfo, bool> _isBlocked;
        private readonly List<ISession> _sessions = new List<ISession>();

        /// <param name="isBlocked">
        /// Optional. True when connecting to this connection must not happen (e.g. it is open in a secondary window,
        /// and a second logon would push the first one out). Checked before every Connect() this manager initiates.
        /// </param>
        public SessionManager(Func<ConnectionInfo, ISession> factory, Func<ConnectionInfo, bool> isBlocked = null)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _isBlocked = isBlocked;
        }

        public IReadOnlyList<ISession> Sessions => _sessions;

        public ISession Active { get; private set; }

        /// <summary>Active session changed (may become null after closing the last one).</summary>
        public event EventHandler ActiveChanged;

        /// <summary>A session was added or removed.</summary>
        public event EventHandler SessionsChanged;

        /// <summary>State of any session changed; sender is the session.</summary>
        public event EventHandler SessionStateChanged;

        /// <summary>Open/Reconnect refused to connect because isBlocked returned true; argument is the connection.</summary>
        public event EventHandler<ConnectionInfo> ConnectBlocked;

        public ISession Find(string id)
        {
            return _sessions.Find(s => s.Id == id);
        }

        /// <summary>
        /// Creates and connects a session for the connection and activates it.
        /// If a session with the same id already exists it is just activated.
        /// Returns null (nothing is created) when the connection is blocked; ConnectBlocked is raised.
        /// </summary>
        public ISession Open(ConnectionInfo info)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));

            var existing = Find(info.Id);
            if (existing != null)
            {
                Activate(existing.Id);
                return existing;
            }

            if (IsBlocked(info)) return null;

            var session = _factory(info);
            session.StateChanged += OnSessionStateChanged;
            _sessions.Add(session);
            SessionsChanged?.Invoke(this, EventArgs.Empty);

            session.Connect();
            Activate(session.Id);
            return session;
        }

        /// <summary>Shows the session and hides the others. Returns false if the id is unknown.</summary>
        public bool Activate(string id)
        {
            var target = Find(id);
            if (target == null) return false;

            var changed = !ReferenceEquals(Active, target);
            if (changed && Active != null) Active.Hide();

            Active = target;
            target.Show();
            target.FocusInput();

            if (changed) ActiveChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>Activates by zero-based position in opening order. Returns false if out of range.</summary>
        public bool ActivateIndex(int index)
        {
            if (index < 0 || index >= _sessions.Count) return false;
            return Activate(_sessions[index].Id);
        }

        public void Next()
        {
            Step(+1);
        }

        public void Prev()
        {
            Step(-1);
        }

        /// <summary>
        /// Connects a disconnected session again and activates it; other sessions are not touched.
        /// Returns false if the id is unknown, or if the connection is blocked (the session is then only
        /// activated, and ConnectBlocked is raised).
        /// </summary>
        public bool Reconnect(string id)
        {
            var session = Find(id);
            if (session == null) return false;

            var blocked = session.State == SessionState.Disconnected && IsBlocked(session.Info);
            if (session.State == SessionState.Disconnected && !blocked) session.Connect();
            Activate(id);
            return !blocked;
        }

        public bool Close(string id)
        {
            var session = Find(id);
            if (session == null) return false;

            var index = _sessions.IndexOf(session);
            var wasActive = ReferenceEquals(Active, session);

            session.StateChanged -= OnSessionStateChanged;
            _sessions.RemoveAt(index);
            session.Hide();
            Dispose(session);

            if (wasActive)
            {
                Active = null;
                if (_sessions.Count > 0)
                {
                    // The session that followed the closed one, or the last one if it was the last.
                    var neighbour = _sessions[Math.Min(index, _sessions.Count - 1)];
                    Activate(neighbour.Id);
                }
                else
                {
                    ActiveChanged?.Invoke(this, EventArgs.Empty);
                }
            }

            SessionsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void CloseAll()
        {
            foreach (var session in _sessions.ToArray()) Close(session.Id);
        }

        private bool IsBlocked(ConnectionInfo info)
        {
            if (_isBlocked == null || !_isBlocked(info)) return false;

            ConnectBlocked?.Invoke(this, info);
            return true;
        }

        private void Step(int delta)
        {
            var count = _sessions.Count;
            if (count == 0) return;

            var current = Active == null ? -1 : _sessions.IndexOf(Active);
            int next;
            if (current < 0) next = delta > 0 ? 0 : count - 1;
            else next = ((current + delta) % count + count) % count;

            Activate(_sessions[next].Id);
        }

        private void OnSessionStateChanged(object sender, EventArgs e)
        {
            SessionStateChanged?.Invoke(sender, EventArgs.Empty);
        }

        private static void Dispose(ISession session)
        {
            if (session.State != SessionState.Disconnected) session.Disconnect();
            session.Dispose();
        }
    }
}
