using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace RdpSwitcher.Native
{
    /// <summary>Finds and starts secondary-window processes (one per connection, told apart by a named mutex).</summary>
    public static class SecondaryWindows
    {
        /// <summary>True if a secondary window process currently holds the mutex of this connection.</summary>
        public static bool IsOpen(string connectionId)
        {
            System.Threading.Mutex existing;
            try
            {
                if (!System.Threading.Mutex.TryOpenExisting(LaunchOptions.SecondaryMutexName(connectionId), out existing))
                    return false;
            }
            catch (UnauthorizedAccessException)
            {
                // It exists but cannot be opened: still somebody's window.
                return true;
            }

            existing.Dispose();
            return true;
        }

        /// <summary>Starts <c>RdpSwitcher.exe --connection id [--monitor n]</c>. Throws on process start failure.</summary>
        public static void Launch(string connectionId, int? monitor)
        {
            var args = "--connection " + connectionId;
            if (monitor != null) args += " --monitor " + monitor.Value;

            Process.Start(new ProcessStartInfo(Application.ExecutablePath, args) { UseShellExecute = false })?.Dispose();
        }

        /// <summary>Index of the screen in Screen.AllScreens (the numbering --monitor uses), or null if not found.</summary>
        public static int? IndexOf(Screen screen)
        {
            var screens = Screen.AllScreens;
            for (var i = 0; i < screens.Length; i++)
                if (screens[i].Equals(screen)) return i;
            return null;
        }
    }
}
