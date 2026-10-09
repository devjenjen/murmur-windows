using System.Text.Json;
using System.Text.Json.Serialization;
using Murmur.Core.Formatting;

namespace Murmur.Core.Settings;

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(DictationProfile))]
[JsonSerializable(typeof(DictationProfile[]))]
[JsonSerializable(typeof(List<DictationProfile>))]
[JsonSerializable(typeof(IReadOnlyList<DictationProfile>))]
[JsonSerializable(typeof(ProfileFormatterKind))]
[JsonSerializable(typeof(MathFormatMode))]
[JsonSerializable(typeof(TextInsertionMode))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Loads and saves application settings.
/// </summary>
public sealed class SettingsStore
{
    private readonly string _path;
    private readonly Func<string, string> _readAllText;
    private readonly Action<string, string, bool> _moveFile;

    /// <summary>Gets the last non-fatal load error, if settings access failed.</summary>
    public Exception? LastLoadError { get; private set; }

    /// <summary>Gets the last save error that was rethrown to the caller.</summary>
    public Exception? LastSaveError { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsStore"/> class.
    /// </summary>
    /// <param name="path">The path to the settings file, or null to use the default path.</param>
    public SettingsStore(string? path = null)
        : this(
            path,
            File.ReadAllText,
            static (source, destination, overwrite) => File.Move(source, destination, overwrite))
    {
    }

    internal SettingsStore(
        string? path,
        Func<string, string> readAllText,
        Action<string, string, bool> moveFile)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Murmur",
            "settings.json");
        _readAllText = readAllText ?? throw new ArgumentNullException(nameof(readAllText));
        _moveFile = moveFile ?? throw new ArgumentNullException(nameof(moveFile));
    }

    /// <summary>
    /// Loads application settings from disk, returning defaults on missing or invalid file.
    /// </summary>
    public AppSettings Load()
    {
        LastLoadError = null;
        if (!File.Exists(_path))
        {
            return AppSettings.Default;
        }

        try
        {
            var json = _readAllText(_path);
            var settings = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
            if (settings is null)
            {
                return AppSettings.Default;
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("textInsertionMode", out _))
            {
                settings = settings with { TextInsertionMode = TextInsertionMode.DocumentEnd };
            }

            // Settings written before the history controls kept every completed dictation.
            // Missing file still uses fresh-install defaults above; only an existing legacy
            // file receives the compatibility migration.
            if (!root.TryGetProperty("historyEnabled", out _))
            {
                settings = settings with { HistoryEnabled = true };
            }
            if (!root.TryGetProperty("historyRetentionDays", out _))
            {
                settings = settings with { HistoryRetentionDays = 0 };
            }
            if (!root.TryGetProperty("historyMaxEntries", out _))
            {
                settings = settings with { HistoryMaxEntries = 0 };
            }

            return settings.Normalize();
        }
        catch (JsonException)
        {
            var dir = Path.GetDirectoryName(_path) ?? "";
            var invalidPath = Path.Combine(dir, "settings.invalid.json");
            try
            {
                _moveFile(_path, invalidPath, true);
            }
            catch
            {
                // Best effort rename
            }

            return AppSettings.Default;
        }
        catch (UnauthorizedAccessException ex)
        {
            LastLoadError = ex;
            return AppSettings.Default;
        }
        catch (IOException ex)
        {
            LastLoadError = ex;
            return AppSettings.Default;
        }
    }

    /// <summary>
    /// Saves application settings to disk atomically.
    /// </summary>
    /// <param name="settings">The settings to save.</param>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        LastSaveError = null;

        var dir = Path.GetDirectoryName(_path);
        var tempPath = Path.Combine(dir ?? "", $"{Guid.NewGuid():N}.tmp");

        try
        {
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(settings.Normalize(), AppSettingsJsonContext.Default.AppSettings);
            File.WriteAllText(tempPath, json);
            _moveFile(tempPath, _path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LastSaveError = ex;
            throw;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Preserve the original save error; cleanup is best effort.
            }
        }
    }
}
