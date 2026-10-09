using System.Diagnostics;
using Murmur.Speech;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Murmur.Speech.Tests;

[Trait("Category", "RealModel")]
[Collection("Real model inference")]
public class ModelBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public ModelBenchmarkTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Benchmark_Parakeet_V3_Reports_Latency_Rtf_Cpu_And_Memory()
    {
        var model = ModelManifest.ParakeetV3;
        var modelDirectory = ModelTestPaths.GetDirectory(model);
        await new ModelDownloader().DownloadModelAsync(model, targetDir: modelDirectory);
        ModelLocator.IsModelDownloaded(model, modelDirectory).ShouldBeTrue();

        var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var peakWorkingSet = process.WorkingSet64;
        var modelSizeBytes = ModelManifest.RequiredFiles
            .Select(file => new FileInfo(Path.Combine(modelDirectory, file)).Length)
            .Sum();

        using var transcriber = new SherpaOnnxTranscriber(modelDirectory);
        await transcriber.TranscribeAsync(new float[16000]);

        foreach (var language in new[] { "de", "en" })
        {
            var source = WaveFixtureReader.ReadModelMono16k(Path.Combine(modelDirectory, "test_wavs", $"{language}.wav"));
            foreach (var durationSeconds in new[] { 3, 5, 10 })
            {
                var audio = RepeatToDuration(source, durationSeconds);
                var latencies = new List<double>();
                var lastText = string.Empty;
                for (var run = 0; run < 5; run++)
                {
                    var stopwatch = Stopwatch.StartNew();
                    lastText = await transcriber.TranscribeAsync(audio);
                    stopwatch.Stop();
                    latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
                    process.Refresh();
                    peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
                }

                latencies.Sort();
                var p50 = Percentile(latencies, 0.50);
                var p95 = Percentile(latencies, 0.95);
                var audioMilliseconds = audio.Length / 16.0;
                var rtf = p50 / audioMilliseconds;
                lastText.ShouldNotBeNullOrWhiteSpace();
                _output.WriteLine(
                    $"model={model.Id} language={language} audioSeconds={audio.Length / 16000.0:F3} " +
                    $"modelBytes={modelSizeBytes} p50Ms={p50:F2} p95Ms={p95:F2} rtf={rtf:F4} " +
                    $"lastTextLength={lastText.Length}");
            }
        }

        process.Refresh();
        var cpuMilliseconds = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
        _output.WriteLine(
            $"model={model.Id} warmup=true cpuMs={cpuMilliseconds:F2} peakWorkingSetBytes={peakWorkingSet} modelBytes={modelSizeBytes}");
    }

    private static float[] RepeatToDuration(float[] source, int durationSeconds)
    {
        var length = checked(durationSeconds * 16000);
        var result = new float[length];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = source[index % source.Length];
        }

        return result;
    }

    private static double Percentile(List<double> sortedValues, double percentile)
    {
        var index = Math.Clamp((int)Math.Ceiling(sortedValues.Count * percentile) - 1, 0, sortedValues.Count - 1);
        return sortedValues[index];
    }
}
