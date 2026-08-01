using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace KarachiRailway.Desktop;

/// <summary>
/// Application entry point.  Registers a global exception handler so that
/// unhandled dispatcher exceptions show a friendly message instead of silently
/// crashing on startup or during simulation playback.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Catch crashes that happen before the WPF dispatcher is up
        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
        {
            var err = ex.ExceptionObject?.ToString() ?? "unknown";
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
            File.AppendAllText(logPath, $"[AppDomain {DateTime.Now:HH:mm:ss}]\r\n{err}\r\n\r\n");
            MessageBox.Show(err, "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private static void OnDispatcherUnhandledException(object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        // Write full details to a log file next to the exe so we can diagnose
        try
        {
            var logPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "error.log");
            File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\r\n" +
                e.Exception.ToString() + "\r\n\r\n");
        }
        catch { /* best-effort */ }

        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\n" +
            "The application will attempt to continue. " +
            "If the problem persists, please restart.\n\n" +
            $"Full details written to: error.log",
            "Karachi Railway System – Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}

