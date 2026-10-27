namespace Murmur.Platform.Windows.Tests;

using System.Reflection;
using Murmur.Platform.Windows.Audio;
using NAudio.Wave;
using Shouldly;
using Xunit;

public class WasapiAudioCaptureTests
{
    [Fact]
    public async Task StopWaitsForWholeStoppedCallbackEvenAfterRunningFlagCleared()
    {
        using var capture = new WasapiAudioCapture();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(WasapiAudioCapture).GetField("_stopped", flags)!.SetValue(capture, stopped);
        capture.CaptureFailed += _ => { entered.TrySetResult(); release.Wait(); };
        var callback = Task.Run(() => typeof(WasapiAudioCapture).GetMethod("OnRecordingStopped", flags)!
            .Invoke(capture, [null, new StoppedEventArgs(new IOException("fake stopped"))]));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            capture.IsRunning.ShouldBeFalse();
            var stop = capture.StopAsync();
            stop.IsCompleted.ShouldBeFalse();
            release.Set();
            await Task.WhenAll(stop, callback).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { release.Set(); await callback.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Theory]
    [InlineData(short.MinValue, -1f)]
    [InlineData(0, 0f)]
    [InlineData(short.MaxValue, 0.9999695f)]
    public void Decode_Pcm16_Samples(int value, float expected)
    {
        Decode(BitConverter.GetBytes((short)value), 0, new WaveFormat(16000, 16, 1))
            .ShouldBe(expected, 0.000001f);
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(0f)]
    [InlineData(1f)]
    public void Decode_IeeeFloat32_Samples(float value)
    {
        Decode(BitConverter.GetBytes(value), 0, WaveFormat.CreateIeeeFloatWaveFormat(16000, 1))
            .ShouldBe(value);
    }

    [Fact]
    public void Decode_Extensible_IeeeFloat32_Sample()
    {
        var format = new WaveFormatExtensible(16000, 32, 1, 0);
        typeof(WaveFormatExtensible).GetField("subFormat", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(format, new Guid("00000003-0000-0010-8000-00aa00389b71"));

        Decode(BitConverter.GetBytes(0.25f), 0, format).ShouldBe(0.25f);
    }

    [Theory]
    [InlineData(int.MinValue, -1f)]
    [InlineData(0, 0f)]
    [InlineData(int.MaxValue, 1f)]
    public void Decode_Pcm32_Samples(int value, float expected)
    {
        Decode(BitConverter.GetBytes(value), 0, new WaveFormat(16000, 32, 1))
            .ShouldBe(expected);
    }

    [Fact]
    public void Decode_Uses_Interleaved_Sample_Index()
    {
        var buffer = new byte[8];
        BitConverter.GetBytes((short)8192).CopyTo(buffer, 0);
        BitConverter.GetBytes((short)-16384).CopyTo(buffer, 2);
        BitConverter.GetBytes((short)16384).CopyTo(buffer, 4);
        BitConverter.GetBytes((short)-8192).CopyTo(buffer, 6);

        Decode(buffer, 3, new WaveFormat(16000, 16, 2)).ShouldBe(-0.25f);
    }

    [Fact]
    public void Decode_Rejects_Unsupported_Bit_Depth()
    {
        Should.Throw<NotSupportedException>(() => Decode([0, 0, 0], 0, new WaveFormat(16000, 24, 1)));
    }

    [Fact]
    public void Decode_Rejects_NonPcm_32Bit_Format()
    {
        var format = new Unsupported32BitWaveFormat();

        Should.Throw<NotSupportedException>(() => Decode([0, 0, 0, 0], 0, format))
            .Message.ShouldContain(format.ToString());
    }

    private static float Decode(byte[] buffer, int sampleIndex, WaveFormat format)
        => WasapiAudioCapture.ReadSample(buffer, sampleIndex, format);

    private sealed class Unsupported32BitWaveFormat : WaveFormat
    {
        public Unsupported32BitWaveFormat()
            : base(16000, 32, 1)
        {
            waveFormatTag = WaveFormatEncoding.Mpeg;
        }
    }
}
