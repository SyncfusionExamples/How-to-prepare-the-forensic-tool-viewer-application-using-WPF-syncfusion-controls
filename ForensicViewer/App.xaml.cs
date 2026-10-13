using Syncfusion.Licensing;
using System;
using System.IO;
using System.Windows;

namespace ForensicViewer
{
    public partial class App : Application
    {
        public static readonly string LogPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash-log.txt");

        protected override void OnStartup(StartupEventArgs e)
        {
            // Register your Syncfusion license key here for production/customer builds.
            // Syncfusion.Licensing.LicenseProvider.RegisterLicense("YOUR_LICENSE_KEY");

            // ---- Global exception capture: WinExe apps die silently otherwise ----
            DispatcherUnhandledException += (s, args) =>
            {
                Log("UNHANDLED UI EXCEPTION", args.Exception);
                args.Handled = true; // keep the app alive; error is logged + shown
                MessageBox.Show(
                    "An unexpected error occurred (logged to):\n" + LogPath +
                    "\n\n" + args.Exception.GetType().Name + ": " + args.Exception.Message,
                    "Forensic Viewer — Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
                Log("UNHANDLED DOMAIN EXCEPTION (FATAL)", args.ExceptionObject as Exception);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) =>
                Log("UNOBSERVED TASK EXCEPTION", args.Exception);

            base.OnStartup(e);
        }

        private static void Log(string title, Exception ex)
        {
            try
            {
                var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {title}{Environment.NewLine}" +
                            (ex != null ? ex.ToString() : "    [no exception object]") +
                            Environment.NewLine +
                            new string('=', 80) + Environment.NewLine;
                File.AppendAllText(LogPath, entry);
            }
            catch { /* never throw from the logger */ }
        }
    }
}