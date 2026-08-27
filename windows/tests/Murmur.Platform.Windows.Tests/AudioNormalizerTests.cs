namespace Murmur.Platform.Windows.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using Murmur.Platform.Windows.Audio;
using Shouldly;
using Xunit;

public class AudioNormalizerTests
{
    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    public void Converts_One_Second_To_Approximately_16000_Samples(int inputRate)
    {
        var input = Enumerable.Range(0, inputRate)
            .Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * 440 * i / inputRate)))
            .ToArray();
        var normalizer = new AudioNormalizer(inputRate);
        var output = new List<float>();

        for (var offset = 0; offset < input.Length; offset += 997)
        {
            var count = Math.Min(997, input.Length - offset);
            output.AddRange(normalizer.Push(input.AsSpan(offset, count)));
        }

        output.Count.ShouldBeInRange(15980, 16020);
        output.Max(Math.Abs).ShouldBeLessThanOrEqualTo(0.55f);
    }

    [Fact]
    public void Chunk_Continuity_Matches_One_Shot()
    {
        const int inputRate = 48000;
        var input = Enumerable.Range(0, inputRate)
            .Select(i => (float)(0.4 * Math.Sin(2 * Math.PI * 300 * i / inputRate)))
            .ToArray();

        var oneShotNormalizer = new AudioNormalizer(inputRate);
        var oneShotOutput = oneShotNormalizer.Push(input.AsSpan());

        var chunkNormalizer = new AudioNormalizer(inputRate);
        var chunkOutput = new List<float>();
        for (var offset = 0; offset < input.Length; offset += 512)
        {
            var count = Math.Min(512, input.Length - offset);
            chunkOutput.AddRange(chunkNormalizer.Push(input.AsSpan(offset, count)));
        }

        Math.Abs(oneShotOutput.Length - chunkOutput.Count).ShouldBeLessThanOrEqualTo(2);
        var minLength = Math.Min(oneShotOutput.Length, chunkOutput.Count);
        for (var i = 0; i < minLength; i++)
        {
            Math.Abs(oneShotOutput[i] - chunkOutput[i]).ShouldBeLessThanOrEqualTo(0.001f);
        }
    }

    [Fact]
    public void Final_40Ms_Signal_Survives_Capture_Chunking()
    {
        const int inputRate = 48000;
        var input = new float[inputRate];
        Array.Fill(input, 0.5f, inputRate - (inputRate * 40 / 1000), inputRate * 40 / 1000);
        var normalizer = new AudioNormalizer(inputRate);
        var output = new List<float>();

        for (var offset = 0; offset < input.Length; offset += 1440)
        {
            var count = Math.Min(1440, input.Length - offset);
            output.AddRange(normalizer.Push(input.AsSpan(offset, count)));
        }

        output.Count.ShouldBe(16000);
        output.TakeLast(640).Max(Math.Abs).ShouldBeGreaterThan(0.1f);
    }
}
