namespace Murmur.Speech.Tests;

using System.Text.Json;

internal sealed record QualityCase(string Id, string Language, string Reference, IReadOnlyList<float> Samples);

internal sealed record QualityCorpus(IReadOnlyList<QualityCase> Cases)
{
    public static QualityCorpus Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var root = Path.GetFullPath(directory);
        var manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath)) throw new InvalidDataException("Quality corpus manifest.json is missing.");

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var rootObject = document.RootElement;
            if (rootObject.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Quality corpus manifest must be an object.");
            RejectUnknownProperties(rootObject, "cases");
            if (!rootObject.TryGetProperty("cases", out var casesElement) || casesElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Quality corpus manifest requires a cases array.");
            }

            var cases = new List<QualityCase>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in casesElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Quality case must be an object.");
                RejectUnknownProperties(item, "id", "audio", "language", "reference");
                var id = RequiredString(item, "id");
                var audio = RequiredString(item, "audio");
                var language = RequiredString(item, "language");
                var reference = RequiredString(item, "reference");
                if (!ids.Add(id)) throw new InvalidDataException($"Duplicate quality case ID: {id}.");

                var audioPath = Path.GetFullPath(Path.Combine(root, audio));
                if (!IsWithin(root, audioPath)) throw new InvalidDataException($"Quality case {id} audio path escapes corpus directory.");
                if (!File.Exists(audioPath)) throw new InvalidDataException($"Quality case {id} audio file is missing.");
                cases.Add(new QualityCase(id, language, reference, Array.AsReadOnly(WaveFixtureReader.ReadMono16k(audioPath))));
            }

            if (cases.Count == 0)
            {
                throw new InvalidDataException("Quality corpus must contain at least one case.");
            }

            return new QualityCorpus(Array.AsReadOnly(cases.ToArray()));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Quality corpus manifest is invalid JSON.", exception);
        }
    }

    private static string RequiredString(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"Quality case requires a non-empty {name} string.");
        }
        return value.GetString()!;
    }

    private static void RejectUnknownProperties(JsonElement item, params string[] permitted)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in item.EnumerateObject())
        {
            if (!seen.Add(property.Name) || !permitted.Contains(property.Name, StringComparer.Ordinal))
            {
                throw new InvalidDataException($"Unexpected or duplicate manifest property: {property.Name}.");
            }
        }
    }

    private static bool IsWithin(string root, string path) =>
        path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

}
