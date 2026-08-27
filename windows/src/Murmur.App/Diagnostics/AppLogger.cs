namespace Murmur.App.Diagnostics;

internal static class AppLogger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Murmur",
        "murmur.log");

    private static readonly Lock LogLock = new();

    static AppLogger()
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
        catch { }
    }

    public static void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        Console.WriteLine(line);
        try
        {
            lock (LogLock)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch { }
    }

    public static void LogException(string context, Exception ex)
    {
        Log($"ERROR in {context}: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
    }
}
