using System.Buffers.Binary;

namespace Murmur.Speech.Tests;

/// <summary>Reads the narrow WAV format accepted by real-model and quality fixtures.</summary>
internal static class WaveFixtureReader
{
    public static float[] ReadMono16k(string path)
    {
        var wave = ReadPcm16Mono(path);
        if (wave.SampleRate != 16_000)
        {
            throw new InvalidDataException("Audio fixture must be PCM16, mono, 16 kHz WAV.");
        }

        return wave.Samples;
    }

    /// <summary>
    /// Reads the pinned model archives' PCM16/mono fixtures and resamples their known
    /// 22.05/24 kHz rate to the 16 kHz input rate expected by the transcribers.
    /// </summary>
    public static float[] ReadModelMono16k(string path)
    {
        var wave = ReadPcm16Mono(path);
        return wave.SampleRate switch
        {
            16_000 => wave.Samples,
            22_050 => Resample(wave.Samples, 22_050, 16_000),
            24_000 => Resample(wave.Samples, 24_000, 16_000),
            _ => throw new InvalidDataException("Model audio fixture must use 16 kHz or a pinned 22.05/24 kHz PCM16 rate.")
        };
    }

    private static PcmWave ReadPcm16Mono(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 12 ||
            !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) != bytes.Length - 8)
        {
            throw new InvalidDataException("Audio fixture must be a complete RIFF WAVE file.");
        }

        ReadOnlySpan<byte> format = default;
        ReadOnlySpan<byte> data = default;
        var offset = 12;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 8)
            {
                throw new InvalidDataException("Audio fixture contains a truncated RIFF chunk header.");
            }

            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            var chunkEnd = (long)offset + 8 + chunkSize;
            if (chunkEnd > bytes.Length)
            {
                throw new InvalidDataException("Audio fixture contains a truncated RIFF chunk.");
            }

            var chunk = bytes.AsSpan(offset + 8, checked((int)chunkSize));
            if (bytes.AsSpan(offset, 4).SequenceEqual("fmt "u8))
            {
                if (!format.IsEmpty) throw new InvalidDataException("Audio fixture contains duplicate fmt chunks.");
                format = chunk;
            }
            else if (bytes.AsSpan(offset, 4).SequenceEqual("data"u8))
            {
                if (!data.IsEmpty) throw new InvalidDataException("Audio fixture contains duplicate data chunks.");
                data = chunk;
            }

            var paddedEnd = chunkEnd + (chunkSize & 1);
            if (paddedEnd > bytes.Length)
            {
                throw new InvalidDataException("Audio fixture is missing RIFF chunk padding.");
            }

            offset = checked((int)paddedEnd);
        }

        if (format.Length < 16 || data.IsEmpty || data.Length % 2 != 0)
        {
            throw new InvalidDataException("Audio fixture must contain PCM16 format and data chunks.");
        }

        var formatTag = BinaryPrimitives.ReadUInt16LittleEndian(format);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..]);
        var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(format[4..]);
        var byteRate = BinaryPrimitives.ReadUInt32LittleEndian(format[8..]);
        var blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(format[12..]);
        var bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(format[14..]);
        if (formatTag != 1 || channels != 1 || sampleRate == 0 || byteRate != sampleRate * 2 ||
            blockAlign != 2 || bitsPerSample != 16)
        {
            throw new InvalidDataException("Audio fixture must be PCM16, mono, WAV.");
        }

        var samples = new float[data.Length / 2];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = BinaryPrimitives.ReadInt16LittleEndian(data[(index * 2)..]) / 32768f;
        }

        return new PcmWave(sampleRate, samples);
    }

    private static float[] Resample(float[] source, uint sourceRate, uint targetRate)
    {
        var targetLength = checked((int)Math.Round(source.Length * (double)targetRate / sourceRate));
        var result = new float[targetLength];
        for (var index = 0; index < result.Length; index++)
        {
            var sourcePosition = index * (double)sourceRate / targetRate;
            var lower = Math.Min((int)sourcePosition, source.Length - 1);
            var upper = Math.Min(lower + 1, source.Length - 1);
            var fraction = sourcePosition - lower;
            result[index] = (float)(source[lower] + ((source[upper] - source[lower]) * fraction));
        }

        return result;
    }

    private sealed record PcmWave(uint SampleRate, float[] Samples);
}
