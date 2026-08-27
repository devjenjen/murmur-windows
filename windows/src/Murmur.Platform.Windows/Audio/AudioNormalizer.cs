using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Murmur.Platform.Windows.Audio;

internal sealed class AudioNormalizer
{
    private const int OutputRate = 16000;
    private readonly int _inputRate;
    private readonly BufferedWaveProvider _buffer;
    private readonly WdlResamplingSampleProvider _resampler;

    public AudioNormalizer(int inputSampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputSampleRate);
        _inputRate = inputSampleRate;
        _buffer = new BufferedWaveProvider(
            WaveFormat.CreateIeeeFloatWaveFormat(inputSampleRate, 1))
        {
            DiscardOnBufferOverflow = false,
            ReadFully = false
        };
        _resampler = new WdlResamplingSampleProvider(_buffer.ToSampleProvider(), OutputRate);
    }

    public float[] Push(ReadOnlySpan<float> monoSamples)
    {
        if (monoSamples.IsEmpty)
        {
            return [];
        }

        var bytes = MemoryMarshal.AsBytes(monoSamples).ToArray();
        _buffer.AddSamples(bytes, 0, bytes.Length);

        var availableInputSamples = _buffer.BufferedBytes / sizeof(float);
        var expectedOutputSamples = (int)(availableInputSamples * (double)OutputRate / _inputRate);
        if (expectedOutputSamples == 0)
        {
            return [];
        }

        var output = new float[expectedOutputSamples];
        var read = _resampler.Read(output, 0, output.Length);
        return output[..read];
    }
}
