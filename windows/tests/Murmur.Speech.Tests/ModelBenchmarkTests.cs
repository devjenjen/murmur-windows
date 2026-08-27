namespace Murmur.Speech.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

[Trait("Category", "RealModel")]
public class ModelBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public ModelBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static float[] ReadWavTo16kMono(string path, double targetDurationSeconds)
    {
        var bytes = File.ReadAllBytes(path);
        // Standard 16-bit PCM RIFF header is 44 bytes
        var sampleCount = (bytes.Length - 44) / 2;
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var val = BitConverter.ToInt16(bytes, 44 + i * 2);
            samples[i] = val / 32768.0f;
        }

        var targetCount = (int)(targetDurationSeconds * 16000);
        var result = new float[targetCount];
        for (var i = 0; i < targetCount; i++)
        {
            result[i] = samples[i % samples.Length];
        }
        return result;
    }

    [Fact]
    public async Task Benchmark_Parakeet_V3_Latency_And_RTF()
    {
        var modelDir = ModelLocator.GetDefaultModelDirectory(ModelManifest.ParakeetV3);
        if (!ModelLocator.IsModelDownloaded(ModelManifest.ParakeetV3, modelDir)) return;

        var deWavPath = Path.Combine(modelDir, "test_wavs", "de.wav");
        var enWavPath = Path.Combine(modelDir, "test_wavs", "en.wav");

        // Cold init measurement
        var swInit = Stopwatch.StartNew();
        using var transcriber = new SherpaOnnxTranscriber(modelDir);
        swInit.Stop();
        var coldInitMs = swInit.ElapsedMilliseconds;
        _output.WriteLine($"[Cold Init] {coldInitMs} ms");

        // Warm up
        await transcriber.TranscribeAsync(new float[16000]);

        var durations = new[] { 3.0, 5.0, 10.0 };

        // German benchmark
        _output.WriteLine("\n--- German Benchmark ---");
        foreach (var duration in durations)
        {
            var audio = ReadWavTo16kMono(deWavPath, duration);
            var latencies = new List<double>();
            string lastText = "";

            for (var run = 0; run < 3; run++)
            {
                var sw = Stopwatch.StartNew();
                lastText = await transcriber.TranscribeAsync(audio);
                sw.Stop();
                latencies.Add(sw.Elapsed.TotalMilliseconds);
            }

            latencies.Sort();
            var median = latencies[1];
            var audioMs = duration * 1000.0;
            var rtf = median / audioMs;
            _output.WriteLine($"DE {duration:F0}s (audio: {audioMs:F0}ms) -> median latency: {median:F2} ms (runs: {latencies[0]:F1}, {latencies[1]:F1}, {latencies[2]:F1} ms), RTF: {rtf:F4}");
            _output.WriteLine($"   Transcript: \"{lastText}\"");

            if (duration == 5.0) median.ShouldBeLessThanOrEqualTo(750.0);
            if (duration == 10.0) median.ShouldBeLessThanOrEqualTo(1000.0);
        }

        // English benchmark
        _output.WriteLine("\n--- English Benchmark ---");
        foreach (var duration in durations)
        {
            var audio = ReadWavTo16kMono(enWavPath, duration);
            var latencies = new List<double>();
            string lastText = "";

            for (var run = 0; run < 3; run++)
            {
                var sw = Stopwatch.StartNew();
                lastText = await transcriber.TranscribeAsync(audio);
                sw.Stop();
                latencies.Add(sw.Elapsed.TotalMilliseconds);
            }

            latencies.Sort();
            var median = latencies[1];
            var audioMs = duration * 1000.0;
            var rtf = median / audioMs;
            _output.WriteLine($"EN {duration:F0}s (audio: {audioMs:F0}ms) -> median latency: {median:F2} ms (runs: {latencies[0]:F1}, {latencies[1]:F1}, {latencies[2]:F1} ms), RTF: {rtf:F4}");
            _output.WriteLine($"   Transcript: \"{lastText}\"");

            if (duration == 5.0) median.ShouldBeLessThanOrEqualTo(750.0);
            if (duration == 10.0) median.ShouldBeLessThanOrEqualTo(1000.0);
        }
    }
}
