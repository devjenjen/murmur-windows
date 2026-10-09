using System.Buffers.Binary;
using Shouldly;
using Xunit;

namespace Murmur.Speech.Tests;

public sealed class WaveFixtureReaderTests
{
    [Fact]
    public void ReadMono16k_skips_list_chunk_and_reads_pcm_samples()
    {
        using var fixture = new TempFixture(BuildWave(
            BuildChunk("LIST", [1, 2, 3]),
            BuildChunk("fmt ", BuildFormat()),
            BuildChunk("data", BuildSamples(0, 16384, -16384))));

        WaveFixtureReader.ReadMono16k(fixture.Path).ShouldBe([0f, 0.5f, -0.5f]);
    }

    [Fact]
    public void ReadMono16k_rejects_missing_data_chunk()
    {
        using var fixture = new TempFixture(BuildWave(BuildChunk("fmt ", BuildFormat())));

        Should.Throw<InvalidDataException>(() => WaveFixtureReader.ReadMono16k(fixture.Path));
    }

    [Theory]
    [InlineData(2, 16000)]
    [InlineData(1, 8000)]
    public void ReadMono16k_rejects_wrong_channel_or_sample_rate(ushort channels, uint sampleRate)
    {
        var format = BuildFormat(channels, sampleRate);
        using var fixture = new TempFixture(BuildWave(
            BuildChunk("fmt ", format),
            BuildChunk("data", BuildSamples(1))));

        Should.Throw<InvalidDataException>(() => WaveFixtureReader.ReadMono16k(fixture.Path));
    }

    [Fact]
    public void ReadMono16k_rejects_truncated_pcm_chunk()
    {
        var bytes = BuildWave(
            BuildChunk("fmt ", BuildFormat()),
            BuildChunk("data", [1]));

        using var fixture = new TempFixture(bytes);

        Should.Throw<InvalidDataException>(() => WaveFixtureReader.ReadMono16k(fixture.Path));
    }

    private static byte[] BuildWave(params byte[][] chunks)
    {
        var size = 12 + chunks.Sum(chunk => chunk.Length);
        var bytes = new byte[size];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), checked((uint)(size - 8)));
        "WAVE"u8.CopyTo(bytes.AsSpan(8));
        var offset = 12;
        foreach (var chunk in chunks)
        {
            chunk.CopyTo(bytes, offset);
            offset += chunk.Length;
        }

        return bytes;
    }

    private static byte[] BuildChunk(string id, byte[] contents)
    {
        var paddedLength = contents.Length + (contents.Length & 1);
        var chunk = new byte[8 + paddedLength];
        System.Text.Encoding.ASCII.GetBytes(id).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), checked((uint)contents.Length));
        contents.CopyTo(chunk, 8);
        return chunk;
    }

    private static byte[] BuildFormat(ushort channels = 1, uint sampleRate = 16000)
    {
        var format = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(format, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(2), channels);
        BinaryPrimitives.WriteUInt32LittleEndian(format.AsSpan(4), sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(format.AsSpan(8), sampleRate * channels * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(12), (ushort)(channels * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(14), 16);
        return format;
    }

    private static byte[] BuildSamples(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var index = 0; index < samples.Length; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index * 2), samples[index]);
        }

        return bytes;
    }

    private sealed class TempFixture : IDisposable
    {
        public TempFixture(byte[] contents)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"murmur-wave-{Guid.NewGuid():N}.wav");
            File.WriteAllBytes(Path, contents);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
