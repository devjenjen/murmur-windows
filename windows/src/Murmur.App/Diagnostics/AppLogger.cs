namespace Murmur.App.Diagnostics;

internal static class AppLogger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Murmur",
        "murmur.log");

    private static readonly RotatingFileLog FileLog = new(LogPath, 2L * 1024 * 1024, backupCount: 2);

    public static void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        Console.WriteLine(line);
        try
        {
            FileLog.WriteLine(line);
        }
        catch { }
    }

    public static void LogException(string context, Exception ex)
    {
        Log($"ERROR in {context}: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
    }
}
