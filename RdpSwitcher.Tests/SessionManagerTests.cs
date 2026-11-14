using System;
using System.Collections.Generic;
using System.Linq;
using RdpSwitcher.Config;
using RdpSwitcher.Sessions;
using Xunit;

namespace RdpSwitcher.Tests
{
    internal class FakeSession : ISession
    {
        public FakeSession(ConnectionInfo info) { Info = info; }

        public string Id => Info.Id;
        public ConnectionInfo Info { get; }
        public SessionState State { get; private set; } = SessionState.Disconnected;
        public string DisconnectReason { get; private set; }
        public event EventHandler StateChanged;

        public bool Visible;
        public int ConnectCalls;
        public int DisconnectCalls;
        public int FocusCalls;
        public bool Disposed;

        public void Connect()
        {
            ConnectCalls++;
            SetState(SessionState.Connected);
        }

        public void Disconnect()
        {
            DisconnectCalls++;
            SetState(SessionState.Disconnected);
        }

        public void Show() { Visible = true; }
        public void Hide() { Visible = false; }
        public void FocusInput() { FocusCalls++; }
        public void Dispose() { Disposed = true; }

        public void SimulateDrop(string reason)
        {
            DisconnectReason = reason;
            SetState(SessionState.Disconnected);
        }

        private void SetState(SessionState state)
        {
            State = state;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public class SessionManagerTests
    {
        private readonly List<FakeSession> _created = new List<FakeSession>();
        private readonly SessionManager _manager;

        public SessionManagerTests()
        {
            _manager = new SessionManager(info =>
            {
                var s = new FakeSession(info);
                _created.Add(s);
                return s;
            });
        }

        private static ConnectionInfo Conn(string id) => new ConnectionInfo { Id = id, Name = id, Host = id };

        private void OpenAll(params string[] ids)
        {
            foreach (var id in ids) _manager.Open(Conn(id));
        }

        private FakeSession Fake(string id) => _created.Single(s => s.Id == id);

        [Fact]
        public void Open_ConnectsAndActivatesNewSession()
        {
            _manager.Open(Conn("a"));

            Assert.Equal("a", _manager.Active.Id);
            Assert.Equal(1, Fake("a").ConnectCalls);
            Assert.True(Fake("a").Visible);
            Assert.True(Fake("a").FocusCalls > 0);
        }

        [Fact]
        public void Open_SecondSession_HidesFirst_KeepsItConnected()
        {
            OpenAll("a", "b");

            Assert.Equal("b", _manager.Active.Id);
            Assert.False(Fake("a").Visible);
            Assert.True(Fake("b").Visible);
            Assert.Equal(SessionState.Connected, Fake("a").State);
            Assert.Equal(0, Fake("a").DisconnectCalls);
        }

        [Fact]
        public void Open_SameId_ActivatesExistingWithoutCreatingNew()
        {
            OpenAll("a", "b");

            var again = _manager.Open(Conn("a"));

            Assert.Single(_created, s => s.Id == "a");
            Assert.Same(Fake("a"), again);
            Assert.Equal("a", _manager.Active.Id);
            Assert.Equal(1, Fake("a").ConnectCalls);
            Assert.Equal(2, _manager.Sessions.Count);
        }

        [Fact]
        public void Activate_ShowsOnlyTarget()
        {
            OpenAll("a", "b", "c");

            _manager.Activate("b");

            Assert.False(Fake("a").Visible);
            Assert.True(Fake("b").Visible);
            Assert.False(Fake("c").Visible);
        }

        [Fact]
        public void Activate_UnknownId_ReturnsFalse()
        {
            OpenAll("a");

            Assert.False(_manager.Activate("zzz"));
            Assert.Equal("a", _manager.Active.Id);
        }

        [Fact]
        public void Activate_FiresActiveChangedOnlyOnChange()
        {
            OpenAll("a", "b");
            var count = 0;
            _manager.ActiveChanged += (s, e) => count++;

            _manager.Activate("b");
            Assert.Equal(0, count);

            _manager.Activate("a");
            Assert.Equal(1, count);
        }

        [Fact]
        public void NextPrev_CycleInOpeningOrder()
        {
            OpenAll("a", "b", "c");

            _manager.Next();
            Assert.Equal("a", _manager.Active.Id);
            _manager.Next();
            Assert.Equal("b", _manager.Active.Id);
            _manager.Prev();
            Assert.Equal("a", _manager.Active.Id);
            _manager.Prev();
            Assert.Equal("c", _manager.Active.Id);
        }

        [Fact]
        public void NextPrev_IncludeDisconnectedSessions()
        {
            OpenAll("a", "b", "c");
            Fake("a").SimulateDrop("network");
            _manager.Activate("c");

            _manager.Next();

            Assert.Equal("a", _manager.Active.Id);
        }

        [Fact]
        public void NextPrev_WithNoSessions_DoNothing()
        {
            _manager.Next();
            _manager.Prev();

            Assert.Null(_manager.Active);
        }

        [Fact]
        public void ActivateIndex_UsesZeroBasedOrder()
        {
            OpenAll("a", "b", "c");

            Assert.True(_manager.ActivateIndex(0));
            Assert.Equal("a", _manager.Active.Id);
            Assert.False(_manager.ActivateIndex(3));
            Assert.False(_manager.ActivateIndex(-1));
            Assert.Equal("a", _manager.Active.Id);
        }

        [Fact]
        public void Close_ActiveInMiddle_ActivatesFollowingSession()
        {
            OpenAll("a", "b", "c");
            _manager.Activate("b");

            _manager.Close("b");

            Assert.Equal("c", _manager.Active.Id);
            Assert.True(Fake("b").Disposed);
            Assert.Equal(1, Fake("b").DisconnectCalls);
            Assert.Equal(new[] { "a", "c" }, _manager.Sessions.Select(s => s.Id).ToArray());
        }

        [Fact]
        public void Close_ActiveLast_ActivatesPrevious()
        {
            OpenAll("a", "b");

            _manager.Close("b");

            Assert.Equal("a", _manager.Active.Id);
            Assert.True(Fake("a").Visible);
        }

        [Fact]
        public void Close_NonActive_KeepsActive()
        {
            OpenAll("a", "b");
            var activeChanges = 0;
            _manager.ActiveChanged += (s, e) => activeChanges++;

            _manager.Close("a");

            Assert.Equal("b", _manager.Active.Id);
            Assert.Equal(0, activeChanges);
        }

        [Fact]
        public void Close_LastSession_ClearsActiveAndFiresEvent()
        {
            OpenAll("a");
            var activeChanges = 0;
            _manager.ActiveChanged += (s, e) => activeChanges++;

            _manager.Close("a");

            Assert.Null(_manager.Active);
            Assert.Empty(_manager.Sessions);
            Assert.Equal(1, activeChanges);
        }

        [Fact]
        public void Close_DisconnectedSession_DoesNotCallDisconnect()
        {
            OpenAll("a", "b");
            Fake("a").SimulateDrop("x");

            _manager.Close("a");

            Assert.Equal(0, Fake("a").DisconnectCalls);
            Assert.True(Fake("a").Disposed);
        }

        [Fact]
        public void Close_UnknownId_ReturnsFalse()
        {
            Assert.False(_manager.Close("nope"));
        }

        [Fact]
        public void CloseAll_RemovesEverything()
        {
            OpenAll("a", "b", "c");

            _manager.CloseAll();

            Assert.Empty(_manager.Sessions);
            Assert.Null(_manager.Active);
            Assert.All(_created, s => Assert.True(s.Disposed));
        }

        [Fact]
        public void Reconnect_OnlyAffectsTargetSession()
        {
            OpenAll("a", "b");
            Fake("a").SimulateDrop("network");

            _manager.Reconnect("a");

            Assert.Equal(2, Fake("a").ConnectCalls);
            Assert.Equal(1, Fake("b").ConnectCalls);
            Assert.Equal(0, Fake("b").DisconnectCalls);
            Assert.Equal("a", _manager.Active.Id);
        }

        [Fact]
        public void Reconnect_ConnectedSession_OnlyActivates()
        {
            OpenAll("a", "b");

            _manager.Reconnect("a");

            Assert.Equal(1, Fake("a").ConnectCalls);
            Assert.Equal("a", _manager.Active.Id);
        }

        [Fact]
        public void SessionStateChanged_IsForwardedWithSessionAsSender()
        {
            OpenAll("a");
            object sender = null;
            _manager.SessionStateChanged += (s, e) => sender = s;

            Fake("a").SimulateDrop("x");

            Assert.Same(Fake("a"), sender);
        }

        [Fact]
        public void SessionStateChanged_NotForwardedAfterClose()
        {
            OpenAll("a", "b");
            var a = Fake("a");
            _manager.Close("a");
            var fired = false;
            _manager.SessionStateChanged += (s, e) => fired = true;

            a.SimulateDrop("x");

            Assert.False(fired);
        }

        [Fact]
        public void SessionsChanged_FiresOnOpenAndClose()
        {
            var count = 0;
            _manager.SessionsChanged += (s, e) => count++;

            _manager.Open(Conn("a"));
            Assert.Equal(1, count);
            _manager.Close("a");
            Assert.Equal(2, count);
        }
    }
}
