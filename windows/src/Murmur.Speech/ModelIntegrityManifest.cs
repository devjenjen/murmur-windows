using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Murmur.Speech;

/// <summary>Integrity state of an installed model directory.</summary>
public enum ModelIntegrityStatus
{
    /// <summary>Directory or required file is missing.</summary>
    Missing,
    /// <summary>Required files exist, but no integrity manifest is present.</summary>
    LegacyUnverified,
    /// <summary>Manifest and all required file hashes match.</summary>
    Valid,
    /// <summary>Manifest or one of its files does not match.</summary>
    Corrupt,
}

/// <summary>Hash and length for one extracted model file.</summary>
public sealed record ModelIntegrityFile(string Path, long Length, string Sha256);

/// <summary>Versioned manifest for extracted model files.</summary>
public sealed record ModelIntegrityManifest(int Version, string ModelId, IReadOnlyList<ModelIntegrityFile> Files)
{
    /// <summary>Current integrity manifest schema version.</summary>
    public const int CurrentVersion = 1;
    /// <summary>File name used inside an extracted model directory.</summary>
    public const string FileName = "integrity.json";

    internal static async Task WriteAsync(ModelManifest model, string directory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);

        var files = new List<ModelIntegrityFile>(ModelManifest.RequiredFiles.Count);
        foreach (var relativePath in ModelManifest.RequiredFiles)
        {
            var path = ResolveSafePath(directory, relativePath);
            if (!File.Exists(path)) throw new InvalidDataException($"Missing required model file: {relativePath}");
            var info = new FileInfo(path);
            if (info.Length == 0) throw new InvalidDataException($"Required model file is empty: {relativePath}");
            files.Add(new ModelIntegrityFile(relativePath, info.Length, await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false)));
        }

        var manifest = new ModelIntegrityManifest(CurrentVersion, model.Id, files);
        var json = JsonSerializer.Serialize(manifest, ModelIntegrityJsonContext.Default.ModelIntegrityManifest);
        await File.WriteAllTextAsync(Path.Combine(directory, FileName), json, cancellationToken).ConfigureAwait(false);
    }

    internal static ModelIntegrityManifest Read(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize(json, ModelIntegrityJsonContext.Default.ModelIntegrityManifest)
            ?? throw new InvalidDataException("Integrity manifest is empty.");
    }

    internal bool Verify(ModelManifest model, string directory)
    {
        if (Version != CurrentVersion || !string.Equals(ModelId, model.Id, StringComparison.Ordinal) || Files is null)
        {
            return false;
        }

        var required = ModelManifest.RequiredFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Files.Count != required.Count)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Files)
        {
            if (!required.Contains(entry.Path) || !seen.Add(entry.Path) || entry.Length <= 0 ||
                !IsSha256(entry.Sha256))
            {
                return false;
            }

            var path = ResolveSafePath(directory, entry.Path);
            if (!File.Exists(path)) return false;
            var info = new FileInfo(path);
            if (info.Length != entry.Length ||
                !string.Equals(ComputeSha256(path), entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return seen.SetEquals(required);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string ResolveSafePath(string directory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"Unsafe integrity path: {relativePath}");
        }

        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(directory, relativePath));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsafe integrity path: {relativePath}");
        }
        return fullPath;
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(ModelIntegrityManifest))]
internal partial class ModelIntegrityJsonContext : JsonSerializerContext
{
}
