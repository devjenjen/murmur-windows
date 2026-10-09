namespace Murmur.Speech.Tests;

using System;
using System.IO;
using System.Reflection;
using Murmur.Speech;
using Shouldly;
using Xunit;

[Collection("Real model inference")]
public class NemotronStreamingTranscriberTests
{
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"murmur-nemotron-tests-{Guid.NewGuid():N}");

        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    [Fact]
    public void Constructor_Rejects_Incomplete_Model_Directory()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(Path.Combine(temp.Path, "encoder.int8.onnx"), [1]);
        File.WriteAllBytes(Path.Combine(temp.Path, "decoder.int8.onnx"), [1]);
        File.WriteAllBytes(Path.Combine(temp.Path, "joiner.int8.onnx"), [1]);

        Should.Throw<DirectoryNotFoundException>(() =>
            new NemotronStreamingTranscriber(temp.Path));
    }

    [Fact]
    [Trait("Category", "RealModel")]
    public async Task Complete_called_twice_uses_one_session_completion()
    {
        var modelDirectory = await EnsureNemotronModelAsync();
        var samples = WaveFixtureReader.ReadModelMono16k(Path.Combine(modelDirectory, "test_wavs", "de.wav"));
        using var transcriber = new NemotronStreamingTranscriber(modelDirectory);
        var gate = GetSessionLock(transcriber);

        Task<string> firstCompletion;
        Task<string> secondCompletion;
        lock (gate)
        {
            transcriber.BeginSession();
            foreach (var chunk in samples.Chunk(1600))
            {
                transcriber.AcceptAudio(chunk);
            }

            firstCompletion = transcriber.CompleteSessionAsync();
            secondCompletion = transcriber.CompleteSessionAsync();
        }

        var results = await Task.WhenAll(firstCompletion, secondCompletion).WaitAsync(TimeSpan.FromSeconds(30));
        results[0].ShouldNotBeNullOrWhiteSpace();
        results[1].ShouldBe(results[0]);
    }

    [Fact]
    [Trait("Category", "RealModel")]
    public async Task Cancel_during_queue_drain_is_idempotent_and_allows_next_session()
    {
        var modelDirectory = await EnsureNemotronModelAsync();
        var samples = WaveFixtureReader.ReadModelMono16k(Path.Combine(modelDirectory, "test_wavs", "de.wav"));
        using var transcriber = new NemotronStreamingTranscriber(modelDirectory);
        var gate = GetSessionLock(transcriber);

        Task cancelTask;
        lock (gate)
        {
            transcriber.BeginSession();
            foreach (var chunk in samples.Chunk(1600))
            {
                transcriber.AcceptAudio(chunk);
            }

            cancelTask = Task.Run(transcriber.CancelSession);
        }

        await cancelTask.WaitAsync(TimeSpan.FromSeconds(30));
        transcriber.BeginSession();
        var nextResult = await transcriber.CompleteSessionAsync().WaitAsync(TimeSpan.FromSeconds(30));
        nextResult.ShouldNotBeNull();
    }

    [Fact]
    [Trait("Category", "RealModel")]
    public async Task Dispose_during_close_is_idempotent()
    {
        var modelDirectory = await EnsureNemotronModelAsync();
        using var transcriber = new NemotronStreamingTranscriber(modelDirectory);
        var gate = GetSessionLock(transcriber);

        Task<string> completion;
        lock (gate)
        {
            transcriber.BeginSession();
            transcriber.AcceptAudio(new float[1600]);
            completion = transcriber.CompleteSessionAsync();
        }

        var disposeTask = Task.Run(transcriber.Dispose);
        await Task.WhenAll(completion, disposeTask).WaitAsync(TimeSpan.FromSeconds(30));
        transcriber.Dispose();
    }

    [Fact]
    [Trait("Category", "RealModel")]
    public async Task Begin_after_successful_close_allows_new_session()
    {
        var modelDirectory = await EnsureNemotronModelAsync();
        using var transcriber = new NemotronStreamingTranscriber(modelDirectory);

        transcriber.BeginSession();
        await transcriber.CompleteSessionAsync().WaitAsync(TimeSpan.FromSeconds(30));

        transcriber.BeginSession();
        var result = await transcriber.CompleteSessionAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.ShouldNotBeNull();
    }

    [Fact]
    [Trait("Category", "RealModel")]
    public async Task New_session_does_not_receive_partials_from_previous_session()
    {
        var modelDirectory = await EnsureNemotronModelAsync();
        var samples = WaveFixtureReader.ReadModelMono16k(Path.Combine(modelDirectory, "test_wavs", "de.wav"));
        var partials = new List<string>();
        using var transcriber = new NemotronStreamingTranscriber(modelDirectory);
        transcriber.PartialTranscriptChanged += partials.Add;

        transcriber.BeginSession();
        foreach (var chunk in samples.Chunk(1600))
        {
            transcriber.AcceptAudio(chunk);
        }

        var firstResult = await transcriber.CompleteSessionAsync().WaitAsync(TimeSpan.FromSeconds(30));
        firstResult.ShouldNotBeNullOrWhiteSpace();
        var firstSessionPartialCount = partials.Count;
        firstSessionPartialCount.ShouldBeGreaterThan(0);

        transcriber.BeginSession();
        await transcriber.CompleteSessionAsync().WaitAsync(TimeSpan.FromSeconds(30));
        partials.Count.ShouldBe(firstSessionPartialCount);
    }

    private static async Task<string> EnsureNemotronModelAsync()
    {
        var model = ModelManifest.NemotronStreaming160Ms;
        var directory = ModelTestPaths.GetDirectory(model);
        await new ModelDownloader().DownloadModelAsync(model, targetDir: directory);
        return directory;
    }

    private static Lock GetSessionLock(NemotronStreamingTranscriber transcriber) =>
        (Lock)typeof(NemotronStreamingTranscriber)
            .GetField("_lock", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(transcriber)!;

}
