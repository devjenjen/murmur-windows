using System.Text.Json;

namespace Murmur.Speech;

/// <summary>
/// Locates local speech models and verifies completeness.
/// </summary>
public static class ModelLocator
{
    /// <summary>Gets the default model storage directory under %LOCALAPPDATA% for the specified model.</summary>
    public static string GetDefaultModelDirectory(ModelManifest? model = null)
    {
        var m = model ?? ModelManifest.ParakeetV3;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Murmur", "models", m.Id);
    }

    /// <summary>Gets the default model storage directory under %LOCALAPPDATA% for the specified variant name.</summary>
    public static string GetDefaultModelDirectory(string variant)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Murmur", "models", variant);
    }

    /// <summary>Checks whether all required model files exist and are non-empty for the specified model.</summary>
    public static bool IsModelDownloaded(ModelManifest model, string? modelDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var dir = modelDirectory ?? GetDefaultModelDirectory(model);
        return GetIntegrityStatus(model, dir) is ModelIntegrityStatus.Valid or ModelIntegrityStatus.LegacyUnverified;
    }

    /// <summary>Checks whether all required model files exist and are non-empty in the specified directory.</summary>
    public static bool IsModelDownloaded(string? modelDirectory = null)
    {
        var dir = modelDirectory ?? GetDefaultModelDirectory(ModelManifest.ParakeetV3);
        return IsModelDownloaded(ModelManifest.ParakeetV3, dir);
    }

    /// <summary>Returns the integrity state without treating legacy installs as corrupt.</summary>
    public static ModelIntegrityStatus GetIntegrityStatus(ModelManifest model, string? modelDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var dir = modelDirectory ?? GetDefaultModelDirectory(model);
        if (!Directory.Exists(dir)) return ModelIntegrityStatus.Missing;

        foreach (var file in ModelManifest.RequiredFiles)
        {
            var path = Path.Combine(dir, file);
            if (!File.Exists(path) || new FileInfo(path).Length == 0) return ModelIntegrityStatus.Missing;
        }

        var manifestPath = Path.Combine(dir, ModelIntegrityManifest.FileName);
        if (!File.Exists(manifestPath)) return ModelIntegrityStatus.LegacyUnverified;

        try
        {
            return ModelIntegrityManifest.Read(manifestPath).Verify(model, dir)
                ? ModelIntegrityStatus.Valid
                : ModelIntegrityStatus.Corrupt;
        }
        catch (JsonException)
        {
            return ModelIntegrityStatus.Corrupt;
        }
        catch (InvalidDataException)
        {
            return ModelIntegrityStatus.Corrupt;
        }
        catch (IOException)
        {
            return ModelIntegrityStatus.Corrupt;
        }
        catch (UnauthorizedAccessException)
        {
            return ModelIntegrityStatus.Corrupt;
        }
    }
}
