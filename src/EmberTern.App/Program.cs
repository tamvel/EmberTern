using System;
using System.IO;
using Avalonia;

namespace EmberTern.App;

internal static class Program
{
    [System.STAThread]
    public static void Main(string[] args)
    {
        // Capture otherwise-silent fatal crashes (e.g. an unhandled UI-thread exception
        // tearing the process down with no dialog) into the shared debug log so they can
        // be diagnosed. Best-effort — never let logging itself break startup/shutdown.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogFatal("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogFatal("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

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
            var path = Path.Combine(Path.GetTempPath(), "EmberTern-debug.log");
            File.AppendAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] FATAL ({source}): {ex}{Environment.NewLine}");
        }
        catch { /* logging must never throw */ }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
