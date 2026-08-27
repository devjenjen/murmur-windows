using NAudio.Wave;

namespace Murmur.Platform.Windows.Audio;

/// <summary>
/// Lightweight audio player for dictation feedback sounds using NAudio.
/// </summary>
public sealed class ClickSoundPlayer : IDisposable
{
    private readonly Lock _lock = new();
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;

    /// <summary>Plays a WAV file at the specified file path asynchronously.</summary>
    public bool Play(string path)
    {
        if (!File.Exists(path)) return false;

        lock (_lock)
        {
            StopPlayback();
            try
            {
                _reader = new AudioFileReader(path);
                _output = new WaveOutEvent();
                _output.PlaybackStopped += OnPlaybackStopped;
                _output.Init(_reader);
                _output.Play();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning(
                    "Murmur feedback playback failed for {0}: {1}",
                    path,
                    ex.Message);
                StopPlayback();
                return false;
            }
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        lock (_lock)
        {
            StopPlayback();
        }
    }

    private void StopPlayback()
    {
        if (_output != null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            _output.Stop();
            _output.Dispose();
            _output = null;
        }

        _reader?.Dispose();
        _reader = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            StopPlayback();
        }
    }
}
