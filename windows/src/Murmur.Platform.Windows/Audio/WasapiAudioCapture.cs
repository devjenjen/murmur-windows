using Murmur.Core.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Murmur.Platform.Windows.Audio;

/// <summary>
/// WASAPI microphone audio capture streaming float32 samples and computing smoothed RMS levels.
/// </summary>
public sealed class WasapiAudioCapture : IAudioCapture
{
    private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    /// <inheritdoc />
    public event Action<ReadOnlyMemory<float>>? AudioChunkAvailable;

    /// <inheritdoc />
    public event Action<float>? LevelChanged;

    /// <inheritdoc />
    public event Action<Exception>? CaptureFailed;

    /// <summary>Fired after final signal-health measurements are available.</summary>
    public event Action? SessionStopped;

    private readonly string? _endpointId;
    private readonly Lock _lock = new();
    private WasapiCapture? _capture;
    private AudioNormalizer? _normalizer;
    private TaskCompletionSource? _stopped;
    private double _sumOfSquares;
    private float _peak;
    private long _clippedSamples;
    private long _outputSamples;

    /// <summary>
    /// Initializes a new instance of the <see cref="WasapiAudioCapture"/> class.
    /// </summary>
    /// <param name="endpointId">The optional MMDevice endpoint ID to capture from, or null for default.</param>
    public WasapiAudioCapture(string? endpointId = null)
    {
        _endpointId = endpointId;
    }

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public AudioCaptureFormat? Format { get; private set; }

    /// <inheritdoc />
    public AudioCaptureStats? LastSessionStats { get; private set; }

    /// <summary>Friendly name reported by the microphone used for capture.</summary>
    public string? FriendlyName { get; private set; }

    /// <summary>
    /// Enumerates all currently active audio capture devices.
    /// </summary>
    public static IReadOnlyList<AudioInputDevice> GetActiveDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        string? defaultId = null;
        try
        {
            using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            defaultId = defaultDevice?.ID;
        }
        catch
        {
            // No default capture device found
        }

        var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        var list = new List<AudioInputDevice>(endpoints.Count);
        foreach (var device in endpoints)
        {
            using (device)
            {
                var isDefault = defaultId != null && string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase);
                list.Add(new AudioInputDevice(device.ID, device.FriendlyName, isDefault));
            }
        }

        return list;
    }

    internal static string? ChooseEndpointId(
        string? configuredId,
        IReadOnlyList<AudioInputDevice> activeDevices)
    {
        if (activeDevices.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(configuredId))
        {
            var matched = activeDevices.FirstOrDefault(d => string.Equals(d.Id, configuredId, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                return matched.Id;
            }
        }

        var defaultDevice = activeDevices.FirstOrDefault(d => d.IsDefault);
        return defaultDevice?.Id ?? activeDevices[0].Id;
    }

    /// <inheritdoc />
    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning)
            {
                return;
            }

            if (_capture != null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.Dispose();
                _capture = null;
            }

            var activeDevices = GetActiveDevices();
            var chosenId = ChooseEndpointId(_endpointId, activeDevices);
            if (chosenId == null)
            {
                throw new InvalidOperationException("No active microphone found.");
            }

            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(chosenId);
            FriendlyName = device.FriendlyName;
            _capture = new WasapiCapture(device, useEventSync: true, audioBufferMillisecondsLength: 30);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            var waveFormat = _capture.WaveFormat;
            Format = new AudioCaptureFormat(
                waveFormat.SampleRate,
                waveFormat.Channels,
                waveFormat.BitsPerSample,
                waveFormat.Encoding.ToString());

            _normalizer = new AudioNormalizer(_capture.WaveFormat.SampleRate);
            _stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _sumOfSquares = 0;
            _peak = 0;
            _clippedSamples = 0;
            _outputSamples = 0;
            LastSessionStats = null;
            IsRunning = true;
            _capture.StartRecording();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource? stoppedTask;
        lock (_lock)
        {
            if (!IsRunning || _capture == null)
            {
                return;
            }

            stoppedTask = _stopped;
            _capture.StopRecording();
        }

        if (stoppedTask != null)
        {
            await stoppedTask.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        var failure = e.Exception;
        lock (_lock)
        {
            IsRunning = false;
            LastSessionStats = new AudioCaptureStats(
                _outputSamples == 0 ? 0 : Math.Sqrt(_sumOfSquares / _outputSamples),
                _peak,
                _clippedSamples,
                _outputSamples);
            _normalizer = null;
            LevelChanged?.Invoke(0);

            _stopped?.TrySetResult();
        }

        if (failure != null)
        {
            CaptureFailed?.Invoke(failure);
        }
        SessionStopped?.Invoke();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || _capture == null || _normalizer == null)
        {
            return;
        }

        var waveFormat = _capture.WaveFormat;
        var bytesPerSample = waveFormat.BitsPerSample / 8;
        if (bytesPerSample == 0)
        {
            throw new NotSupportedException($"Invalid bits per sample in format: {waveFormat}");
        }

        var bytesPerFrame = waveFormat.Channels * bytesPerSample;
        var frameCount = e.BytesRecorded / bytesPerFrame;
        var mono = new float[frameCount];

        for (var frame = 0; frame < frameCount; frame++)
        {
            double sum = 0;
            for (var channel = 0; channel < waveFormat.Channels; channel++)
            {
                sum += ReadSample(e.Buffer, (frame * waveFormat.Channels) + channel, waveFormat);
            }
            mono[frame] = (float)(sum / waveFormat.Channels);
        }

        var normalized = _normalizer.Push(mono);
        if (normalized.Length == 0)
        {
            return;
        }

        // Calculate RMS Level (normalized 0..1)
        double rmsSum = 0;
        foreach (var sample in normalized)
        {
            rmsSum += sample * sample;
            _sumOfSquares += sample * sample;
            _peak = Math.Max(_peak, Math.Abs(sample));
            if (Math.Abs(sample) >= 0.999f)
            {
                _clippedSamples++;
            }
        }
        _outputSamples += normalized.Length;
        var rms = (float)Math.Sqrt(rmsSum / normalized.Length);
        var db = 20 * Math.Log10(Math.Max(rms, 1e-7));
        var normalizedLevel = (float)Math.Clamp((db + 50) / 50.0, 0.0, 1.0);

        LevelChanged?.Invoke(normalizedLevel);
        AudioChunkAvailable?.Invoke(normalized);
    }

    internal static float ReadSample(ReadOnlySpan<byte> buffer, int sampleIndex, WaveFormat waveFormat)
    {
        var subFormat = (waveFormat as WaveFormatExtensible)?.SubFormat;
        var isIeeeFloat = waveFormat.Encoding == WaveFormatEncoding.IeeeFloat || subFormat == IeeeFloatSubFormat;
        var isPcm = waveFormat.Encoding == WaveFormatEncoding.Pcm || subFormat == PcmSubFormat;
        if (isIeeeFloat && waveFormat.BitsPerSample == 32)
        {
            return BitConverter.ToSingle(buffer.Slice(sampleIndex * sizeof(float), sizeof(float)));
        }
        if (isPcm && waveFormat.BitsPerSample == 16)
        {
            var val = BitConverter.ToInt16(buffer.Slice(sampleIndex * sizeof(short), sizeof(short)));
            return val / 32768.0f;
        }
        if (isPcm && waveFormat.BitsPerSample == 32)
        {
            var val = BitConverter.ToInt32(buffer.Slice(sampleIndex * sizeof(int), sizeof(int)));
            return val / 2147483648.0f;
        }

        throw new NotSupportedException($"Audio format not supported: {waveFormat}");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            if (IsRunning && _capture != null)
            {
                try
                {
                    _capture.StopRecording();
                }
                catch
                {
                    // Best-effort stop
                }
            }

            IsRunning = false;

            if (_capture != null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.Dispose();
                _capture = null;
            }

            _normalizer = null;
        }
    }
}
