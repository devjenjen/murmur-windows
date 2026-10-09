using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Murmur.Core.History;

[JsonSerializable(typeof(DictationHistoryEntry))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class HistoryJsonContext : JsonSerializerContext
{
}

/// <summary>Persists completed dictations as JSON Lines.</summary>
public sealed class HistoryStore
{
    private readonly string _path;
    private readonly Lock _lock = new();

    /// <summary>Creates a history store at the default or supplied path.</summary>
    public HistoryStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Murmur",
            "history.jsonl");
    }

    /// <summary>Appends one completed dictation.</summary>
    public void Append(DictationHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock)
        {
            EnsureDirectory();
            var json = JsonSerializer.Serialize(entry, HistoryJsonContext.Default.DictationHistoryEntry);
            File.AppendAllText(_path, json + Environment.NewLine, new UTF8Encoding(false));
        }
    }

    /// <summary>Loads valid entries newest first and reports skipped malformed lines.</summary>
    public IReadOnlyList<DictationHistoryEntry> Load(out int skippedLineCount)
    {
        lock (_lock)
        {
            return LoadUnsafe(out skippedLineCount);
        }
    }

    /// <summary>Deletes one entry while preserving all other valid entries.</summary>
    public void Delete(Guid id)
    {
        lock (_lock)
        {
            var remaining = LoadUnsafe(out _).Where(entry => entry.Id != id);
            RewriteUnsafe(remaining);
        }
    }

    /// <summary>Deletes all history entries.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            if (File.Exists(_path))
            {
                RewriteUnsafe([]);
            }
        }
    }

    /// <summary>Removes entries older than the retention boundary and beyond the newest-entry limit.</summary>
    public void Prune(DateTimeOffset now, int retentionDays, int maxEntries)
    {
        lock (_lock)
        {
            var normalizedRetention = retentionDays is 0 or 7 or 30 or 90 ? retentionDays : 30;
            var normalizedMax = maxEntries is 0 or 1000 ? maxEntries : 1000;
            if (normalizedRetention == 0 && normalizedMax == 0) return;
            if (!File.Exists(_path))
            {
                return;
            }

            var original = LoadUnsafe(out _);
            var entries = original.AsEnumerable();
            if (normalizedRetention > 0)
            {
                var cutoff = now.AddDays(-normalizedRetention);
                entries = entries.Where(entry => entry.Timestamp >= cutoff);
            }

            if (normalizedMax > 0)
            {
                entries = entries.Take(normalizedMax);
            }

            var remaining = entries.ToArray();
            if (remaining.Length != original.Length) RewriteUnsafe(remaining);
        }
    }

    private DictationHistoryEntry[] LoadUnsafe(out int skippedLineCount)
    {
        skippedLineCount = 0;
        if (!File.Exists(_path))
        {
            return [];
        }

        var entries = new List<DictationHistoryEntry>();
        foreach (var line in File.ReadLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var entry = JsonSerializer.Deserialize(line, HistoryJsonContext.Default.DictationHistoryEntry);
                if (entry is null || entry.Id == Guid.Empty || entry.Timestamp == default ||
                    string.IsNullOrWhiteSpace(entry.Engine) || string.IsNullOrEmpty(entry.Text))
                {
                    skippedLineCount++;
                    continue;
                }

                entries.Add(entry);
            }
            catch (JsonException)
            {
                skippedLineCount++;
            }
        }

        return entries.OrderByDescending(entry => entry.Timestamp).ToArray();
    }

    private void RewriteUnsafe(IEnumerable<DictationHistoryEntry> entries)
    {
        EnsureDirectory();
        var directory = Path.GetDirectoryName(_path) ?? string.Empty;
        var temporaryPath = Path.Combine(directory, $"history.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var writer = new StreamWriter(temporaryPath, append: false, encoding: new UTF8Encoding(false)))
            {
                foreach (var entry in entries)
                {
                    writer.WriteLine(JsonSerializer.Serialize(entry, HistoryJsonContext.Default.DictationHistoryEntry));
                }
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void EnsureDirectory()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
