using System.Diagnostics;
using Microsoft.Win32;

namespace Murmur.Platform.Windows.Startup;

/// <summary>
/// Manages automatic application startup with Windows via the HKCU Run registry key.
/// </summary>
public static class WindowsStartup
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Murmur";

    internal static string InstalledExecutablePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "Murmur", "Murmur.App.exe");

    internal static string ResolveExecutablePath(string processPath, string installedPath, bool installedExists) =>
        Path.GetFullPath(installedExists ? installedPath : processPath);

    internal static string BuildCommand(string executablePath) =>
        $"\"{Path.GetFullPath(executablePath)}\"";

    internal static string? GetRegisteredExecutablePath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        if (key?.GetValue(ValueName) is not string command) return null;
        return command.Trim().Trim('"');
    }

    /// <summary>
    /// Checks whether the application is registered to start with Windows.
    /// </summary>
    public static bool IsEnabled(string? executablePath = null)
    {
        var processPath = executablePath ?? Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        var exe = executablePath ?? ResolveDefaultExecutablePath(processPath);
        if (string.IsNullOrEmpty(exe))
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        if (key?.GetValue(ValueName) is string storedCommand)
        {
            var expected = BuildCommand(exe);
            return string.Equals(storedCommand.Trim(), expected, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Enables automatic startup with Windows for the given executable.
    /// </summary>
    public static void Enable(string? executablePath = null)
    {
        var processPath = executablePath ?? Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        var exe = executablePath ?? ResolveDefaultExecutablePath(processPath);
        if (string.IsNullOrEmpty(exe))
        {
            throw new InvalidOperationException("Could not determine executable path.");
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key == null)
        {
            throw new InvalidOperationException("Could not open HKCU Run registry key.");
        }

        key.SetValue(ValueName, BuildCommand(exe));
    }

    private static string ResolveDefaultExecutablePath(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("Could not determine executable path.");
        }

        return ResolveExecutablePath(processPath, InstalledExecutablePath, File.Exists(InstalledExecutablePath));
    }

    /// <summary>
    /// Disables automatic startup with Windows.
    /// </summary>
    public static void Disable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
