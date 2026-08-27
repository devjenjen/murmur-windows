namespace Murmur.Speech.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Murmur.Speech;
using Shouldly;
using Xunit;

[Trait("Category", "RealModel")]
public class RealModelDownloadTests
{
    [Fact]
    public async Task Download_And_Transcribe_Smoke_Test()
    {
        var downloader = new ModelDownloader();
        var progressValues = new List<double>();
        var progress = new Progress<double>(p => progressValues.Add(p));

        await downloader.DownloadModelAsync(ModelManifest.ParakeetV3, progress: progress);

        ModelLocator.IsModelDownloaded(ModelManifest.ParakeetV3).ShouldBeTrue();

        using var transcriber = new SherpaOnnxTranscriber();
        var result = await transcriber.TranscribeAsync(new float[16000]);
        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task Download_And_Transcribe_Nemotron_Smoke_Test()
    {
        var downloader = new ModelDownloader();
        var progressValues = new List<double>();
        var progress = new Progress<double>(p => progressValues.Add(p));

        await downloader.DownloadModelAsync(ModelManifest.NemotronStreaming160Ms, progress: progress);

        ModelLocator.IsModelDownloaded(ModelManifest.NemotronStreaming160Ms).ShouldBeTrue();

        using var transcriber = new NemotronStreamingTranscriber();
        var result = await transcriber.TranscribeAsync(new float[16000]);
        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task German_Audio_Emits_Partial_Before_Final_Result()
    {
        var model = ModelManifest.NemotronStreaming160Ms;
        var modelDir = ModelLocator.GetDefaultModelDirectory(model);
        await new ModelDownloader().DownloadModelAsync(model);

        var samples = ReadPcm16Wav(Path.Combine(modelDir, "test_wavs", "de.wav"));
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

    private static float[] ReadPcm16Wav(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var samples = new float[(bytes.Length - 44) / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToInt16(bytes, 44 + (i * 2)) / 32768.0f;
        }
        return samples;
    }
}
