using Xunit;

namespace RdpSwitcher.Tests
{
    public class LaunchOptionsTests
    {
        private const string Id = "b4f1c0de-0000-0000-0000-000000000001";

        private static LaunchOptions Parse(params string[] args)
        {
            LaunchOptions options;
            string error;
            Assert.True(LaunchOptions.TryParse(args, out options, out error), error);
            Assert.Null(error);
            return options;
        }

        private static string Fail(params string[] args)
        {
            LaunchOptions options;
            string error;
            Assert.False(LaunchOptions.TryParse(args, out options, out error));
            Assert.Null(options);
            Assert.False(string.IsNullOrEmpty(error));
            return error;
        }

        [Fact]
        public void NoArguments_IsPrimary()
        {
            var options = Parse();

            Assert.False(options.IsSecondary);
            Assert.Null(options.ConnectionId);
            Assert.Null(options.Monitor);
        }

        [Fact]
        public void NullArguments_IsPrimary()
        {
            Assert.False(Parse(null).IsSecondary);
        }

        [Fact]
        public void Connection_IsSecondaryWithDefaultMonitor()
        {
            var options = Parse("--connection", Id);

            Assert.True(options.IsSecondary);
            Assert.Equal(Id, options.ConnectionId);
            Assert.Null(options.Monitor);
        }

        [Fact]
        public void ConnectionAndMonitor_AnyOrder()
        {
            var options = Parse("--monitor", "1", "--connection", Id);

            Assert.Equal(Id, options.ConnectionId);
            Assert.Equal(1, options.Monitor);
        }

        [Fact]
        public void Monitor_ZeroIsValid()
        {
            Assert.Equal(0, Parse("--connection", Id, "--monitor", "0").Monitor);
        }

        [Fact]
        public void ArgumentNames_AreCaseInsensitive()
        {
            Assert.True(Parse("--CONNECTION", Id, "--Monitor", "2").IsSecondary);
        }

        [Theory]
        [InlineData("--connection")]
        [InlineData("--connection not-a-guid")]
        [InlineData("--connection ")]
        public void BadConnection_Fails(string commandLine)
        {
            Fail(commandLine.Split(' '));
        }

        [Theory]
        [InlineData("--monitor 1")]
        [InlineData("--connection b4f1c0de-0000-0000-0000-000000000001 --monitor")]
        [InlineData("--connection b4f1c0de-0000-0000-0000-000000000001 --monitor -1")]
        [InlineData("--connection b4f1c0de-0000-0000-0000-000000000001 --monitor x")]
        [InlineData("--connection b4f1c0de-0000-0000-0000-000000000001 --monitor 1.5")]
        public void BadMonitor_Fails(string commandLine)
        {
            Fail(commandLine.Split(' '));
        }

        [Fact]
        public void DuplicateAndUnknownArguments_Fail()
        {
            Fail("--connection", Id, "--connection", Id);
            Fail("--connection", Id, "--monitor", "0", "--monitor", "1");
            Fail("--connection", Id, "--fullscreen");
        }

        [Fact]
        public void SecondaryMutexName_IsLowercaseAndPerConnection()
        {
            Assert.Equal(@"Local\RdpSwitcher.Secondary.abc", LaunchOptions.SecondaryMutexName(" ABC "));
            Assert.NotEqual(LaunchOptions.SecondaryMutexName("a"), LaunchOptions.SecondaryMutexName("b"));
            Assert.NotEqual(LaunchOptions.PrimaryMutexName, LaunchOptions.SecondaryMutexName("primary"));
        }
    }
}
