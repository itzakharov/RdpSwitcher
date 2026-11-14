using System.Collections.Generic;
using System.Linq;
using RdpSwitcher.Config;
using RdpSwitcher.Sessions;
using Xunit;

namespace RdpSwitcher.Tests
{
    public class SessionManagerBlockedTests
    {
        private readonly List<FakeSession> _created = new List<FakeSession>();
        private readonly HashSet<string> _blocked = new HashSet<string>();
        private readonly List<string> _reported = new List<string>();
        private readonly SessionManager _manager;

        public SessionManagerBlockedTests()
        {
            _manager = new SessionManager(
                info =>
                {
                    var s = new FakeSession(info);
                    _created.Add(s);
                    return s;
                },
                info => _blocked.Contains(info.Id));
            _manager.ConnectBlocked += (s, info) => _reported.Add(info.Id);
        }

        private static ConnectionInfo Conn(string id) => new ConnectionInfo { Id = id, Name = id, Host = id };

        private FakeSession Fake(string id) => _created.Single(s => s.Id == id);

        [Fact]
        public void Open_Blocked_CreatesNothingAndReports()
        {
            _blocked.Add("a");

            var session = _manager.Open(Conn("a"));

            Assert.Null(session);
            Assert.Empty(_created);
            Assert.Empty(_manager.Sessions);
            Assert.Null(_manager.Active);
            Assert.Equal(new[] { "a" }, _reported);
        }

        [Fact]
        public void Open_NotBlocked_Works_OtherConnectionBlockedDoesNotMatter()
        {
            _blocked.Add("b");

            var session = _manager.Open(Conn("a"));

            Assert.NotNull(session);
            Assert.Equal(1, Fake("a").ConnectCalls);
            Assert.Empty(_reported);
        }

        [Fact]
        public void Open_ExistingSession_IsActivatedEvenIfNowBlocked()
        {
            _manager.Open(Conn("a"));
            _manager.Open(Conn("b"));
            _blocked.Add("a");

            var again = _manager.Open(Conn("a"));

            Assert.Same(Fake("a"), again);
            Assert.Equal("a", _manager.Active.Id);
            Assert.Equal(1, Fake("a").ConnectCalls);
            Assert.Empty(_reported);
        }

        [Fact]
        public void Reconnect_Blocked_DoesNotConnectButActivates()
        {
            _manager.Open(Conn("a"));
            _manager.Open(Conn("b"));
            Fake("a").SimulateDrop("lost");
            _blocked.Add("a");

            var result = _manager.Reconnect("a");

            Assert.False(result);
            Assert.Equal(1, Fake("a").ConnectCalls);
            Assert.Equal(SessionState.Disconnected, Fake("a").State);
            Assert.Equal("a", _manager.Active.Id);
            Assert.Equal(new[] { "a" }, _reported);
        }

        [Fact]
        public void Reconnect_NotBlocked_Connects()
        {
            _manager.Open(Conn("a"));
            Fake("a").SimulateDrop("lost");

            var result = _manager.Reconnect("a");

            Assert.True(result);
            Assert.Equal(2, Fake("a").ConnectCalls);
            Assert.Empty(_reported);
        }

        [Fact]
        public void Reconnect_ConnectedSession_IsNotCheckedAgainstBlock()
        {
            _manager.Open(Conn("a"));
            _blocked.Add("a");

            var result = _manager.Reconnect("a");

            Assert.True(result);
            Assert.Empty(_reported);
        }

        [Fact]
        public void NoPredicate_NeverBlocks()
        {
            var manager = new SessionManager(info => new FakeSession(info));

            Assert.NotNull(manager.Open(Conn("a")));
        }
    }
}
