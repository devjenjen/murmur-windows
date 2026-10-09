namespace Murmur.Speech.Tests;

using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Murmur.Core.Abstractions;
using Murmur.Core.Formatting;
using Murmur.Dictionary;
using Murmur.Speech;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

[Collection("Real model inference")]
[Trait("Category", "QualityCorpus")]
public sealed class ModelQualityTests
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new() { WriteIndented = true };

    private readonly ITestOutputHelper _output;

    public ModelQualityTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Word_error_rate_is_zero_for_identical_text()
    {
        TextErrorRate.WordErrorRate("one two", "one two").ShouldBe(0d);
    }

    [Fact]
    public void Word_error_rate_counts_an_insertion()
    {
        TextErrorRate.WordErrorRate("one two", "one extra two").ShouldBe(0.5d);
    }

    [Fact]
    public void Word_error_rate_counts_a_deletion()
    {
        TextErrorRate.WordErrorRate("one two", "one").ShouldBe(0.5d);
    }

    [Fact]
    public void Word_error_rate_counts_a_substitution()
    {
        TextErrorRate.WordErrorRate("one two", "one three").ShouldBe(0.5d);
    }

    [Fact]
    public void Error_rates_normalize_unicode_to_nfc()
    {
        TextErrorRate.WordErrorRate("café", "cafe\u0301").ShouldBe(0d);
        TextErrorRate.CharacterErrorRate("café", "cafe\u0301").ShouldBe(0d);
    }

    [Fact]
    public void Error_rates_handle_empty_reference()
    {
        TextErrorRate.WordErrorRate("", "").ShouldBe(0d);
        TextErrorRate.CharacterErrorRate("", "").ShouldBe(0d);
        TextErrorRate.WordErrorRate("", "word").ShouldBe(1d);
        TextErrorRate.CharacterErrorRate("", "word").ShouldBe(1d);
    }

    [Fact]
    public void Corpus_rejects_malformed_manifest()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "manifest.json"), "{");

        Should.Throw<InvalidDataException>(() => QualityCorpus.Load(directory.Path));
    }

    [Fact]
    public void Corpus_rejects_audio_path_outside_manifest_directory()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "manifest.json"),
            """{"cases":[{"id":"case-1","audio":"../outside.wav","language":"en","reference":"reference"}]}""");

        Should.Throw<InvalidDataException>(() => QualityCorpus.Load(directory.Path));
    }

    [Fact]
    public void Corpus_rejects_wrong_wav_format()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "manifest.json"),
            """{"cases":[{"id":"case-1","audio":"case.wav","language":"en","reference":"reference"}]}""");
        File.WriteAllBytes(Path.Combine(directory.Path, "case.wav"), MakeWav(channels: 2));

        Should.Throw<InvalidDataException>(() => QualityCorpus.Load(directory.Path));
    }

    [Fact]
    public void Quality_dictionary_stage_uses_parser_loaded_entries_and_hashes_that_file()
    {
        using var directory = new TempDirectory();
        var dictionaryPath = Path.Combine(directory.Path, "dictionary.txt");
        File.WriteAllText(dictionaryPath, "heard -> written");

        var (dictionary, hash) = LoadDictionary(dictionaryPath);

        dictionary.Apply("heard").Text.ShouldBe("written");
        hash.ShouldBe(HashFile(dictionaryPath));
    }

    [Fact]
    public void Quality_dictionary_stage_uses_no_hash_when_dictionary_file_is_absent()
    {
        using var directory = new TempDirectory();

        var (dictionary, hash) = LoadDictionary(Path.Combine(directory.Path, "dictionary.txt"));

        dictionary.IsEmpty.ShouldBeTrue();
        hash.ShouldBeNull();
    }

    [Fact]
    public async Task Model_quality_report()
    {
        var corpusDirectory = Environment.GetEnvironmentVariable("MURMUR_QUALITY_CORPUS");
        if (string.IsNullOrWhiteSpace(corpusDirectory)) return;

        var corpus = QualityCorpus.Load(corpusDirectory);
        var dictionaryPath = Environment.GetEnvironmentVariable("MURMUR_QUALITY_DICTIONARY");
        var (dictionary, dictionaryHash) = LoadDictionary(dictionaryPath);
        var commit = GetCommitInfo();
        var report = new QualityReport(
            commit.Sha,
            commit.Dirty,
            GetRuntimeMetadata(),
            dictionaryHash,
            []);

        foreach (var model in ModelManifest.All)
        {
            var modelDirectory = ModelTestPaths.GetDirectory(model);
            if (!ModelLocator.IsModelDownloaded(model, modelDirectory))
            {
                throw new InvalidOperationException($"Validated quality model is missing: {model.Id} ({modelDirectory}).");
            }

            using var transcriber = CreateTranscriber(model, modelDirectory);
            var formatter = new RuleBasedFormatter(removeFillers: false);
            var cases = new List<QualityCaseResult>();
            foreach (var item in corpus.Cases)
            {
                var timer = Stopwatch.StartNew();
                var raw = await transcriber.TranscribeAsync(item.Samples.ToArray());
                timer.Stop();
                var formatted = formatter.Format(raw);
                var corrected = dictionary.Apply(formatted).Text;
                cases.Add(new QualityCaseResult(
                    item.Id, item.Language, timer.Elapsed.TotalMilliseconds,
                    Metrics(item.Reference, raw), Metrics(item.Reference, formatted), Metrics(item.Reference, corrected)));
            }

            var aggregate = Aggregate(cases);
            report.Models.Add(new ModelQualityReport(
                model.Id,
                ModelManifest.RequiredFiles.ToDictionary(file => file, file => HashFile(Path.Combine(modelDirectory, file))),
                new FormatterFlags(RemoveFillers: false),
                cases.GroupBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
                aggregate,
                cases));
            _output.WriteLine($"model={model.Id} cases={cases.Count} rawWer={aggregate.Raw.WordErrorRate:F4} formattedWer={aggregate.Formatted.WordErrorRate:F4} correctedWer={aggregate.Corrected.WordErrorRate:F4}");
            foreach (var item in cases) _output.WriteLine($"case={item.Id} category={item.Category}");
        }

        if (report.Models.Count != ModelManifest.All.Count)
        {
            throw new InvalidOperationException("Quality report does not contain both validated models.");
        }

        var outputDirectory = Environment.GetEnvironmentVariable("MURMUR_QUALITY_OUTPUT_DIRECTORY");
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            outputDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Murmur", "quality-results");
        }

        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, $"{report.CommitSha}.json");
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, ReportJsonOptions));
        _output.WriteLine($"quality-report={outputPath}");
    }

    private static ITranscriber CreateTranscriber(ModelManifest model, string modelDirectory) => model.SupportsLivePreview
        ? new NemotronStreamingTranscriber(modelDirectory)
        : new SherpaOnnxTranscriber(modelDirectory);

    private static ErrorMetrics Metrics(string reference, string hypothesis) => new(
        TextErrorRate.WordErrorRate(reference, hypothesis),
        TextErrorRate.CharacterErrorRate(reference, hypothesis));

    private static AggregateMetrics Aggregate(IReadOnlyList<QualityCaseResult> cases) => new(
        Mean(cases.Select(item => item.Raw.WordErrorRate)),
        Mean(cases.Select(item => item.Raw.CharacterErrorRate)),
        Mean(cases.Select(item => item.Formatted.WordErrorRate)),
        Mean(cases.Select(item => item.Formatted.CharacterErrorRate)),
        Mean(cases.Select(item => item.Corrected.WordErrorRate)),
        Mean(cases.Select(item => item.Corrected.CharacterErrorRate)),
        Mean(cases.Select(item => item.InferenceMilliseconds)));

    private static double Mean(IEnumerable<double> values)
    {
        var valuesArray = values.ToArray();
        return valuesArray.Length == 0 ? 0d : valuesArray.Average();
    }

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static (DictionaryCorrector Dictionary, string? Hash) LoadDictionary(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return (new DictionaryCorrector([]), null);
        try
        {
            return (new DictionaryCorrector(DictionaryFile.Load(path)), HashFile(path));
        }
        catch (IOException)
        {
            return (new DictionaryCorrector([]), null);
        }
        catch (UnauthorizedAccessException)
        {
            return (new DictionaryCorrector([]), null);
        }
    }

    private static CommitInfo GetCommitInfo()
    {
        var repository = FindRepository(Environment.CurrentDirectory) ?? FindRepository(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException("Could not find Git repository for quality report.");
        var sha = RunGit(repository, "rev-parse", "HEAD");
        if (sha.Length == 0) throw new InvalidOperationException("Could not determine Git commit SHA.");
        var status = RunGit(repository, "status", "--porcelain");
        return new CommitInfo(sha, status.Length != 0);
    }

    private static string RunGit(string repository, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"Git command failed: {string.Join(' ', arguments)}.");
        return output;
    }

    private static Dictionary<string, string> GetRuntimeMetadata()
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["framework"] = RuntimeInformation.FrameworkDescription,
            ["os"] = RuntimeInformation.OSDescription,
            ["architecture"] = RuntimeInformation.OSArchitecture.ToString(),
            ["murmur.speech"] = GetAssemblyVersion(typeof(ModelManifest).Assembly),
            ["murmur.core"] = GetAssemblyVersion(typeof(RuleBasedFormatter).Assembly),
            ["murmur.dictionary"] = GetAssemblyVersion(typeof(DictionaryCorrector).Assembly),
        };

        foreach (var nativePath in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll")
                     .Where(path => Path.GetFileName(path).Contains("onnx", StringComparison.OrdinalIgnoreCase)
                         || Path.GetFileName(path).Contains("sherpa", StringComparison.OrdinalIgnoreCase)))
        {
            var nativeName = Path.GetFileName(nativePath);
            metadata[$"native:{nativeName}"] = FileVersionInfo.GetVersionInfo(nativePath).FileVersion ?? "unknown";
        }

        return metadata;
    }

    private static string GetAssemblyVersion(Assembly assembly) =>
        assembly.GetName().Version?.ToString() ?? "unknown";

    private static string? FindRepository(string start)
    {
        for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git"))) return directory.FullName;
        }
        return null;
    }

    private sealed record CommitInfo(string Sha, bool Dirty);
    private sealed record ErrorMetrics(double WordErrorRate, double CharacterErrorRate);
    private sealed record QualityCaseResult(string Id, string Category, double InferenceMilliseconds, ErrorMetrics Raw, ErrorMetrics Formatted, ErrorMetrics Corrected);
    private sealed record FormatterFlags(bool RemoveFillers);
    private sealed record AggregateMetrics(double RawWordErrorRate, double RawCharacterErrorRate, double FormattedWordErrorRate, double FormattedCharacterErrorRate, double CorrectedWordErrorRate, double CorrectedCharacterErrorRate, double MeanInferenceMilliseconds)
    {
        public ErrorMetrics Raw => new(RawWordErrorRate, RawCharacterErrorRate);
        public ErrorMetrics Formatted => new(FormattedWordErrorRate, FormattedCharacterErrorRate);
        public ErrorMetrics Corrected => new(CorrectedWordErrorRate, CorrectedCharacterErrorRate);
    }
    private sealed record ModelQualityReport(string ModelId, IReadOnlyDictionary<string, string> ModelFileHashes, FormatterFlags Formatter, IReadOnlyDictionary<string, int> CasesByLanguage, AggregateMetrics Aggregate, IReadOnlyList<QualityCaseResult> Cases);
    private sealed record QualityReport(string CommitSha, bool Dirty, IReadOnlyDictionary<string, string> Runtime, string? DictionaryFileHash, List<ModelQualityReport> Models);

    private static byte[] MakeWav(ushort channels)
    {
        var bytes = new byte[46];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), channels);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), 16000);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), 16000 * channels * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32), (ushort)(channels * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(34), 16);
        "data"u8.CopyTo(bytes.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), 2);
        return bytes;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"murmur-quality-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
