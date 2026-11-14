using System;
using System.Globalization;
using RdpSwitcher.Localization;

namespace RdpSwitcher
{
    /// <summary>
    /// Command line: <c>--connection &lt;id&gt; [--monitor &lt;n&gt;]</c>. With --connection the process is a secondary
    /// window for that one connection; without it, the primary manager.
    /// </summary>
    public sealed class LaunchOptions
    {
        public const string PrimaryMutexName = @"Local\RdpSwitcher.Primary";

        private const string ConnectionArg = "--connection";
        private const string MonitorArg = "--monitor";

        /// <summary>Connection id as typed (matched against config ids ignoring case); null in primary mode.</summary>
        public string ConnectionId { get; private set; }

        /// <summary>Zero-based monitor index, or null for "the monitor under the cursor".</summary>
        public int? Monitor { get; private set; }

        public bool IsSecondary => ConnectionId != null;

        /// <summary>One window per connection. Built from the id stored in config.json, so every process agrees on the name.</summary>
        public static string SecondaryMutexName(string connectionId)
        {
            return @"Local\RdpSwitcher.Secondary." + (connectionId ?? "").Trim().ToLowerInvariant();
        }

        public static bool TryParse(string[] args, out LaunchOptions options, out string error)
        {
            options = null;
            error = null;
            var result = new LaunchOptions();

            for (var i = 0; i < (args?.Length ?? 0); i++)
            {
                var arg = args[i];
                if (string.Equals(arg, ConnectionArg, StringComparison.OrdinalIgnoreCase))
                {
                    if (result.ConnectionId != null) { error = Loc.T("Launch.DuplicateConnection"); return false; }
                    if (i + 1 >= args.Length) { error = Loc.T("Launch.ConnectionNeedsId"); return false; }

                    var value = args[++i].Trim();
                    Guid guid;
                    if (!Guid.TryParse(value, out guid))
                    {
                        error = Loc.T("Launch.IdNotGuid", value);
                        return false;
                    }
                    result.ConnectionId = value;
                }
                else if (string.Equals(arg, MonitorArg, StringComparison.OrdinalIgnoreCase))
                {
                    if (result.Monitor != null) { error = Loc.T("Launch.DuplicateMonitor"); return false; }
                    if (i + 1 >= args.Length) { error = Loc.T("Launch.MonitorNeedsNumber"); return false; }

                    var text = args[++i].Trim();
                    int number;
                    if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number))
                    {
                        error = Loc.T("Launch.MonitorNotNumber", text);
                        return false;
                    }
                    result.Monitor = number;
                }
                else
                {
                    error = Loc.T("Launch.UnknownOption", arg);
                    return false;
                }
            }

            if (result.Monitor != null && result.ConnectionId == null)
            {
                error = Loc.T("Launch.MonitorNeedsConnection");
                return false;
            }

            options = result;
            return true;
        }
    }
}
