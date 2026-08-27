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

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsStore"/> class.
    /// </summary>
    /// <param name="path">The path to the settings file, or null to use the default path.</param>
    public SettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Murmur",
            "settings.json");
    }

    /// <summary>
    /// Loads application settings from disk, returning defaults on missing or invalid file.
    /// </summary>
    public AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            return AppSettings.Default;
        }

        try
        {
            var json = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
            return (settings ?? AppSettings.Default).Normalize();
        }
        catch (JsonException)
        {
            var dir = Path.GetDirectoryName(_path) ?? "";
            var invalidPath = Path.Combine(dir, "settings.invalid.json");
            try
            {
                File.Move(_path, invalidPath, overwrite: true);
            }
            catch
            {
                // Best effort rename
            }

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

        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = Path.Combine(dir ?? "", $"{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(settings.Normalize(), AppSettingsJsonContext.Default.AppSettings);

        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _path, overwrite: true);
    }
}
