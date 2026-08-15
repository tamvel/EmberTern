using System;
using System.IO;
using Avalonia;

namespace EmberTern.LicenseManager;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Same posture as EmberTern's Program: a fatal crash that tears the process down with no dialog
        // is otherwise silent, and this application is the only thing that can issue a licence.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogFatal("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            LogFatal("StartWithClassicDesktopLifetime", ex);
            throw;
        }
    }

    private static void LogFatal(string source, Exception? ex)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "EmberTern-LicenseManager-debug.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] FATAL ({source}): {ex}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never throw.
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
