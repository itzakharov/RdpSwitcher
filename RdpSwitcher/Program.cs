using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using RdpSwitcher.Config;
using RdpSwitcher.Localization;
using RdpSwitcher.Native;
using RdpSwitcher.UI;

namespace RdpSwitcher
{
    internal static class Program
    {
        /// <summary>Registered window message that asks the running primary instance to come to the front.</summary>
        public static readonly int ShowInstanceMessage = NativeMethods.RegisterWindowMessage("RdpSwitcher.ShowInstance");

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Until the config is read, the system language is used (command-line errors, config errors).
            Loc.SetLanguage(null);

            LaunchOptions options;
            string error;
            if (!LaunchOptions.TryParse(args, out options, out error))
            {
                MessageBox.Show(error + "\n\nRdpSwitcher.exe [--connection <id> [--monitor <n>]]", "RdpSwitcher",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (options.IsSecondary) RunSecondary(options);
            else RunPrimary();
        }

        private static void RunPrimary()
        {
            // Per logon session: another user's instance on the same machine does not block this one.
            bool createdNew;
            using (var mutex = new Mutex(true, LaunchOptions.PrimaryMutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // Let the running instance take the foreground, then ask it to do so.
                    NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
                    NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, ShowInstanceMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                var store = new ConfigStore();
                var config = store.Load();
                if (store.LoadError != null)
                    MessageBox.Show(store.LoadError, "RdpSwitcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                // First start (or an unknown code in the file): take the system language once and remember it.
                if (config.Language == null)
                {
                    config.Language = Loc.Detect(CultureInfo.CurrentUICulture);
                    try { store.Save(config); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
                }
                Loc.SetLanguage(config.Language);

                Application.Run(new MainForm(config, store));
            }
        }

        // One window for one connection. The config is only read: a missing or corrupt file is the primary's to repair.
        private static void RunSecondary(LaunchOptions options)
        {
            var store = new ConfigStore(readOnly: true);
            AppConfig config;
            try
            {
                config = store.Load();
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                ShowError(Loc.T("Program.ReadConfigFailed", ex.Message));
                return;
            }
            Loc.SetLanguage(config.Language);

            var connection = config.Connections.FirstOrDefault(
                c => string.Equals(c.Id, options.ConnectionId, StringComparison.OrdinalIgnoreCase));
            if (connection == null)
            {
                ShowError(Loc.T("Program.NoSuchConnection", options.ConnectionId));
                return;
            }

            bool createdNew;
            using (var mutex = new Mutex(true, LaunchOptions.SecondaryMutexName(connection.Id), out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(Loc.T("Program.WindowAlreadyOpen", connection.Name), "RdpSwitcher",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var monitor = options.Monitor;
                if (monitor != null && monitor.Value >= Screen.AllScreens.Length)
                {
                    MessageBox.Show(Loc.T("Program.NoSuchMonitor", monitor.Value, Screen.AllScreens.Length), "RdpSwitcher",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    monitor = null;
                }

                Application.Run(new MainForm(config, store, connection, monitor));
            }
        }

        private static void ShowError(string text)
        {
            MessageBox.Show(text, "RdpSwitcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
