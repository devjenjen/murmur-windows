namespace Murmur.Speech.Tests;

using System.Reflection;
using System.Threading.Tasks;
using Murmur.Core.Abstractions;
using Murmur.Speech;
using Shouldly;
using Xunit;

[Trait("Category", "RealModel")]
[Collection("Real model inference")]
public class RealModelDownloadTests
{
    [Theory]
    [InlineData("parakeet-v3", "de")]
    [InlineData("parakeet-v3", "en")]
    [InlineData("nemotron-3.5-streaming-160ms", "de")]
    [InlineData("nemotron-3.5-streaming-160ms", "en")]
    [InlineData("whisper-v3-turbo", "de")]
    [InlineData("whisper-v3-turbo", "en")]
    public async Task Download_And_Transcribe_German_And_English_Smoke_Test(string modelId, string language)
    {
        var model = ModelManifest.Get(modelId);
        var modelDirectory = ModelTestPaths.GetDirectory(model);
        await EnsureModelAsync(model);
        var samples = await LoadFixtureAsync(model, language);

        using ITranscriber transcriber = model.SupportsLivePreview
            ? new NemotronStreamingTranscriber(modelDirectory)
            : string.Equals(model.Id, ModelManifest.WhisperV3Turbo.Id, StringComparison.OrdinalIgnoreCase)
                ? new SherpaOnnxWhisperTranscriber(modelDirectory)
                : new SherpaOnnxTranscriber(modelDirectory);
        var result = await transcriber.TranscribeAsync(samples);
        result.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task German_Audio_Emits_Partial_Before_Final_Result()
    {
        var model = ModelManifest.NemotronStreaming160Ms;
        var modelDir = ModelTestPaths.GetDirectory(model);
        await new ModelDownloader().DownloadModelAsync(model, targetDir: modelDir);

        var samples = WaveFixtureReader.ReadModelMono16k(Path.Combine(modelDir, "test_wavs", "de.wav"));
        var partials = new List<string>();
        using var transcriber = new NemotronStreamingTranscriber(modelDir);
        transcriber.PartialTranscriptChanged += partials.Add;

        transcriber.BeginSession();
        foreach (var chunk in samples.Chunk(1600))
        {
            transcriber.AcceptAudio(chunk);
        }
        var final = await transcriber.CompleteSessionAsync();

        partials.ShouldContain(text => !string.IsNullOrWhiteSpace(text));
        final.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Completion_before_worker_start_preserves_accepted_audio()
    {
        var model = ModelManifest.NemotronStreaming160Ms;
        var modelDirectory = ModelTestPaths.GetDirectory(model);
        await new ModelDownloader().DownloadModelAsync(model, targetDir: modelDirectory);
        var samples = WaveFixtureReader.ReadModelMono16k(Path.Combine(modelDirectory, "test_wavs", "de.wav"));
        var partials = new List<string>();
        using var transcriber = new NemotronStreamingTranscriber(modelDirectory);
        transcriber.PartialTranscriptChanged += partials.Add;
        var gate = (Lock)typeof(NemotronStreamingTranscriber)
            .GetField("_lock", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(transcriber)!;

        Task<string> completion;
        lock (gate)
        {
            // Private lock is deliberate scheduling control: Complete runs before worker can capture session refs.
            transcriber.BeginSession();
            foreach (var chunk in samples.Chunk(1600))
            {
                transcriber.AcceptAudio(chunk);
            }

            completion = transcriber.CompleteSessionAsync();
        }

        var final = await completion.WaitAsync(TimeSpan.FromSeconds(30));
        final.ShouldNotBeNullOrWhiteSpace();
        partials.ShouldContain(text => !string.IsNullOrWhiteSpace(text));
    }

    private static async Task EnsureModelAsync(ModelManifest model)
    {
        var directory = ModelTestPaths.GetDirectory(model);
        await new ModelDownloader().DownloadModelAsync(model, targetDir: directory);
        ModelLocator.IsModelDownloaded(model, directory).ShouldBeTrue();
    }

    private static async Task<float[]> LoadFixtureAsync(ModelManifest model, string language)
    {
        var candidate = Path.Combine(ModelTestPaths.GetDirectory(model), "test_wavs", $"{language}.wav");
        if (!File.Exists(candidate))
        {
            // Pinned Whisper or Nemotron archives may lack a language fixture; use the pinned Parakeet
            // fixture after validating that model in the same isolated root.
            await EnsureModelAsync(ModelManifest.ParakeetV3);
            candidate = Path.Combine(ModelTestPaths.GetDirectory(ModelManifest.ParakeetV3), "test_wavs", $"{language}.wav");
        }

        if (!File.Exists(candidate))
        {
            throw new FileNotFoundException($"Real-model fixture is missing for {model.Id}/{language}.", candidate);
        }

        return WaveFixtureReader.ReadModelMono16k(candidate);
    }
}
