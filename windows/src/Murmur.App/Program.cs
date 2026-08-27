using Avalonia;
using Murmur.App.Diagnostics;

namespace Murmur.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                AppLogger.LogException("AppDomain.UnhandledException", ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            AppLogger.LogException("TaskScheduler.UnobservedTaskException", e.Exception);
        };

        AppLogger.Log("Program.Main started.");
        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
            AppLogger.Log("Program.Main StartWithClassicDesktopLifetime exited normally.");
        }
        catch (Exception ex)
        {
            AppLogger.LogException("Program.Main", ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
