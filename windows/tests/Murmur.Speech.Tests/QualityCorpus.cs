namespace Murmur.Speech.Tests;

using System.Buffers.Binary;
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
                cases.Add(new QualityCase(id, language, reference, Array.AsReadOnly(ReadPcm16Mono16k(audioPath))));
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

    private static float[] ReadPcm16Mono16k(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 44 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8)
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) != bytes.Length - 8)
        {
            throw new InvalidDataException("Quality audio must be a complete RIFF WAVE file.");
        }

        var offset = 12;
        ReadOnlySpan<byte> format = default;
        ReadOnlySpan<byte> data = default;
        while (offset + 8 <= bytes.Length)
        {
            var chunkLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            if (chunkLength < 0 || offset + 8L + chunkLength > bytes.Length) throw new InvalidDataException("Invalid RIFF chunk length.");
            var chunk = bytes.AsSpan(offset + 8, chunkLength);
            if (bytes.AsSpan(offset, 4).SequenceEqual("fmt "u8)) format = chunk;
            if (bytes.AsSpan(offset, 4).SequenceEqual("data"u8)) data = chunk;
            offset += 8 + chunkLength + (chunkLength & 1);
        }

        if (offset != bytes.Length || format.Length != 16 || data.Length == 0 || data.Length % 2 != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(format) != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(format[2..]) != 1
            || BinaryPrimitives.ReadInt32LittleEndian(format[4..]) != 16000
            || BinaryPrimitives.ReadInt32LittleEndian(format[8..]) != 32000
            || BinaryPrimitives.ReadUInt16LittleEndian(format[12..]) != 2
            || BinaryPrimitives.ReadUInt16LittleEndian(format[14..]) != 16)
        {
            throw new InvalidDataException("Quality audio must be PCM16 mono 16 kHz WAV.");
        }

        var samples = new float[data.Length / 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = BinaryPrimitives.ReadInt16LittleEndian(data[(i * 2)..]) / 32768f;
        return samples;
    }
}
