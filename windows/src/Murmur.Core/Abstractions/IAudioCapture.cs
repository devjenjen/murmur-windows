namespace Murmur.Core.Abstractions;

/// <summary>Native microphone format used for a capture session.</summary>
public sealed record AudioCaptureFormat(int SampleRate, int Channels, int BitsPerSample, string Encoding);

/// <summary>Signal-health measurements from the 16 kHz mono output of a capture session.</summary>
public sealed record AudioCaptureStats(double Rms, float Peak, long ClippedSamples, long OutputSamples);

/// <summary>
/// Captures microphone audio and computes smoothed RMS level.
/// </summary>
public interface IAudioCapture : IDisposable
{
    /// <summary>Fired when a 16kHz mono float32 chunk is available.</summary>
    event Action<ReadOnlyMemory<float>>? AudioChunkAvailable;

    /// <summary>Fired with normalized 0..1 RMS level for UI waveform rendering.</summary>
    event Action<float>? LevelChanged;

    /// <summary>Whether capture is currently active.</summary>
    bool IsRunning { get; }

    /// <summary>Format reported by the active microphone.</summary>
    AudioCaptureFormat? Format { get; }

    /// <summary>Final signal-health measurements from the most recent capture session.</summary>
    AudioCaptureStats? LastSessionStats { get; }

    /// <summary>Starts audio capture.</summary>
    void Start();

    /// <summary>Stops audio capture asynchronously.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
