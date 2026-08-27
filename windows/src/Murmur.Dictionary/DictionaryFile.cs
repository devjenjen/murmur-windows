using System.Text;

namespace Murmur.Dictionary;

/// <summary>Loads the shared plain-text dictionary format.</summary>
public static class DictionaryFile
{
    /// <summary>Safe starter corrections for common proper nouns.</summary>
    public static IReadOnlyList<DictionaryEntry> DefaultEntries { get; } =
    [
        DictionaryEntry.Correction("cloud code", "Claude Code"),
        DictionaryEntry.Correction("clad code", "Claude Code"),
        DictionaryEntry.Correction("clawed code", "Claude Code"),
        DictionaryEntry.Correction("git hub", "GitHub"),
        DictionaryEntry.Correction("get hub", "GitHub"),
        DictionaryEntry.Correction("githup", "GitHub"),
        DictionaryEntry.Correction("whisper flow", "Wispr Flow")
    ];

    /// <summary>Default local dictionary path.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Murmur", "dictionary.txt");

    /// <summary>Loads entries from a dictionary file, or an empty dictionary when absent.</summary>
    public static IReadOnlyList<DictionaryEntry> Load(string? path = null)
    {
        var filePath = path ?? DefaultPath;
        return File.Exists(filePath) ? Parse(File.ReadAllText(filePath)) : [];
    }

    /// <summary>Loads the user's dictionary, creating safe starter corrections on first run.</summary>
    public static IReadOnlyList<DictionaryEntry> LoadOrCreate(string? path = null)
    {
        var filePath = path ?? DefaultPath;
        if (File.Exists(filePath)) return Load(filePath);

        Save(DefaultEntries, filePath);
        return DefaultEntries;
    }

    /// <summary>Saves the plain-text dictionary atomically.</summary>
    public static void Save(IEnumerable<DictionaryEntry> entries, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var filePath = path ?? DefaultPath;
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory ?? string.Empty,
            $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        var lines = entries.Select(entry => entry.ToFileLine());
        var text = "# Murmur dictionary\n# X -> Y corrects a commonly misheard phrase\n\n"
            + string.Join(Environment.NewLine, lines)
            + Environment.NewLine;

        try
        {
            File.WriteAllText(temporaryPath, text, new UTF8Encoding(false));
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    /// <summary>Parses bare terms, corrections, disabled entries, comments, and blank lines.</summary>
    public static IReadOnlyList<DictionaryEntry> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var entries = new List<DictionaryEntry>();
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            var enabled = true;
            if (line.StartsWith("# off:", StringComparison.OrdinalIgnoreCase))
            {
                enabled = false;
                line = line[6..].Trim();
            }
            else if (line.StartsWith('#'))
            {
                continue;
            }

            var arrow = line.IndexOf("->", StringComparison.Ordinal);
            if (arrow < 0)
            {
                if (line.Length > 0) entries.Add(DictionaryEntry.Term(line) with { IsEnabled = enabled });
                continue;
            }

            if (line.IndexOf("->", arrow + 2, StringComparison.Ordinal) >= 0) continue;
            var hear = line[..arrow].Trim();
            var write = line[(arrow + 2)..].Trim();
            if (hear.Length > 0 && write.Length > 0)
            {
                entries.Add(DictionaryEntry.Correction(hear, write) with { IsEnabled = enabled });
            }
        }
        return entries;
    }
}
