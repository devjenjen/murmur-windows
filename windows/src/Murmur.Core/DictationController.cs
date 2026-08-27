using System.Diagnostics;
using Murmur.Core.Abstractions;
using Murmur.Core.Formatting;
using Murmur.Core.Settings;
using Murmur.Core.State;
using Murmur.Dictionary;

namespace Murmur.Core;

/// <summary>
/// Coordinates audio capture, hotkey triggers, speech transcription, formatting, dictionary corrections, and text injection.
/// </summary>
public sealed class DictationController : IDisposable
{
    private sealed record RuntimeState(
        DictationProfile Profile,
        ITextFormatter Formatter,
        DictionaryCorrector Dictionary);

    private readonly IAudioCapture _audioCapture;
    private readonly IHotkeySource _hotkeySource;
    private readonly ITranscriber _transcriber;
    private readonly IStreamingTranscriber? _streamingTranscriber;
    private readonly ITextInjector _textInjector;
    private readonly string _engine;

    private RuntimeState _runtimeState;
    private RuntimeState? _activeSessionState;

    private readonly List<float> _audioBuffer = [];
    private readonly Lock _lock = new();

    /// <summary>Gets the currently active dictation profile.</summary>
    public DictationProfile CurrentProfile => Volatile.Read(ref _runtimeState).Profile;

    /// <summary>Fired when the active profile changes.</summary>
    public event Action<DictationProfile>? ProfileChanged;

    /// <summary>Current dictation state.</summary>
    public DictationState State { get; private set; } = DictationState.Idle;

    /// <summary>Current smoothed RMS audio level (0..1).</summary>
    public float Level { get; private set; }

    /// <summary>Current transcript text.</summary>
    public string CurrentTranscript { get; private set; } = string.Empty;

    /// <summary>Fired when dictation state changes.</summary>
    public event Action<DictationState>? StateChanged;

    /// <summary>Fired when audio input level changes.</summary>
    public event Action<float>? LevelChanged;

    /// <summary>Fired when transcript changes.</summary>
    public event Action<string>? TranscriptChanged;

    /// <summary>Fired when dictation metrics are recorded.</summary>
    public event Action<DictationMetrics>? MetricsRecorded;

    /// <summary>Fired after final text is ready and before injection starts.</summary>
    public event Action<CompletedDictation>? DictationCompleted;

    /// <summary>Fired after final text was successfully inserted into the foreground application.</summary>
    public event Action<CompletedDictation>? TextInjected;

    /// <summary>Initializes a new instance of DictationController.</summary>
    public DictationController(
        IAudioCapture audioCapture,
        IHotkeySource hotkeySource,
        ITranscriber transcriber,
        ITextInjector textInjector,
        string engine,
        ITextFormatter? formatter = null,
        DictionaryCorrector? dictionaryCorrector = null,
        DictationProfile? profile = null)
    {
        _audioCapture = audioCapture ?? throw new ArgumentNullException(nameof(audioCapture));
        _hotkeySource = hotkeySource ?? throw new ArgumentNullException(nameof(hotkeySource));
        _transcriber = transcriber ?? throw new ArgumentNullException(nameof(transcriber));
        _streamingTranscriber = transcriber as IStreamingTranscriber;
        _textInjector = textInjector ?? throw new ArgumentNullException(nameof(textInjector));
        _engine = string.IsNullOrWhiteSpace(engine)
            ? throw new ArgumentException("Engine display name is required.", nameof(engine))
            : engine;

        var initialProfile = profile ?? new DictationProfile();
        var initialFormatter = formatter ?? new RuleBasedFormatter();
        var initialDictionary = dictionaryCorrector ?? new DictionaryCorrector([]);
        _runtimeState = new RuntimeState(initialProfile, initialFormatter, initialDictionary);

        _hotkeySource.Pressed += OnHotkeyPressed;
        _hotkeySource.Released += OnHotkeyReleased;

        _audioCapture.AudioChunkAvailable += OnAudioChunkAvailable;
        _audioCapture.LevelChanged += OnAudioLevelChanged;

        if (_streamingTranscriber != null)
        {
            _streamingTranscriber.PartialTranscriptChanged += OnPartialTranscriptChanged;
        }
    }

    /// <summary>Starts listening for hotkey events.</summary>
    public void Start()
    {
        _hotkeySource.Start();
    }

    /// <summary>Updates the profile and its formatter atomically for subsequent dictations.</summary>
    public void UpdateProfile(DictationProfile profile, ITextFormatter formatter)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(formatter);

        var current = Volatile.Read(ref _runtimeState);
        Volatile.Write(ref _runtimeState, new RuntimeState(profile, formatter, current.Dictionary));
        ProfileChanged?.Invoke(profile);
    }

    /// <summary>Replaces dictionary rules for the next and later dictations.</summary>
    public void SetDictionary(IEnumerable<DictionaryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var current = Volatile.Read(ref _runtimeState);
        Volatile.Write(ref _runtimeState, new RuntimeState(current.Profile, current.Formatter, new DictionaryCorrector(entries)));
    }

    /// <summary>Stops capture and listeners.</summary>
    public void Stop()
    {
        _hotkeySource.Stop();
        try
        {
            _audioCapture.StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort shutdown. Active dictation reports failures in its own error path.
        }
        _activeSessionState = null;
        SetState(DictationState.Idle);
    }

    private void OnHotkeyPressed()
    {
        if (State != DictationState.Idle) return;

        _activeSessionState = Volatile.Read(ref _runtimeState);

        lock (_lock)
        {
            _audioBuffer.Clear();
        }

        CurrentTranscript = string.Empty;
        TranscriptChanged?.Invoke(string.Empty);
        SetState(DictationState.Starting);

        try
        {
            _streamingTranscriber?.BeginSession();
            _audioCapture.Start();
            SetState(DictationState.Listening);
        }
        catch (Exception ex)
        {
            CancelStreamingSession();
            _activeSessionState = null;
            SetState(DictationState.ErrorState($"Microphone error: {ex.Message}"));
        }
    }

    private async void OnHotkeyReleased()
    {
        if (!State.IsActive || State.Status == DictationStatus.Finishing) return;

        try
        {
            SetState(DictationState.Finishing);
            await Task.Delay(40).ConfigureAwait(false);
            await _audioCapture.StopAsync().ConfigureAwait(false);
            Level = 0;
            LevelChanged?.Invoke(0);

            float[] audioCopy;
            lock (_lock)
            {
                audioCopy = [.. _audioBuffer];
                _audioBuffer.Clear();
            }

            if (audioCopy.Length == 0 && _streamingTranscriber is null)
            {
                _activeSessionState = null;
                SetState(DictationState.Idle);
                return;
            }

            var audioDurationMs = (audioCopy.Length / 16000.0) * 1000.0;
            var sw = Stopwatch.StartNew();
            var raw = _streamingTranscriber is null
                ? await _transcriber.TranscribeAsync(audioCopy).ConfigureAwait(false)
                : await _streamingTranscriber.CompleteSessionAsync().ConfigureAwait(false);
            sw.Stop();
            var inferenceMs = (double)sw.ElapsedMilliseconds;
            var rtf = audioDurationMs > 0 ? (inferenceMs / audioDurationMs) : 0.0;

            if (string.IsNullOrWhiteSpace(raw))
            {
                MetricsRecorded?.Invoke(new DictationMetrics(audioDurationMs, inferenceMs, rtf, 0));
                _activeSessionState = null;
                SetState(DictationState.Idle);
                return;
            }

            var sessionState = _activeSessionState ?? Volatile.Read(ref _runtimeState);
            var cleaned = sessionState.Formatter.Format(raw);
            var (corrected, _) = sessionState.Dictionary.Apply(cleaned);

            CurrentTranscript = corrected;
            TranscriptChanged?.Invoke(corrected);

            var completed = new CompletedDictation(
                DateTimeOffset.Now,
                _engine,
                audioDurationMs,
                inferenceMs,
                corrected);

            try
            {
                DictationCompleted?.Invoke(completed);
            }
            catch
            {
                // History observer failure must not block injection.
            }

            _textInjector.Insert(corrected);

            try
            {
                TextInjected?.Invoke(completed);
            }
            catch
            {
                // Feedback observer failure must not turn successful injection into a dictation error.
            }

            MetricsRecorded?.Invoke(new DictationMetrics(audioDurationMs, inferenceMs, rtf, corrected.Length));
            _activeSessionState = null;
            SetState(DictationState.Idle);
        }
        catch (Exception ex)
        {
            CancelStreamingSession();
            _activeSessionState = null;
            SetState(DictationState.ErrorState($"Transcription error: {ex.Message}"));
            await Task.Delay(3000).ConfigureAwait(false);
            if (State.Status == DictationStatus.Error)
            {
                SetState(DictationState.Idle);
            }
        }
    }

    private void OnPartialTranscriptChanged(string text)
    {
        if (State.Status is not (DictationStatus.Listening or DictationStatus.Finishing) ||
            string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        CurrentTranscript = text;
        TranscriptChanged?.Invoke(text);
    }

    private void CancelStreamingSession()
    {
        try
        {
            _streamingTranscriber?.CancelSession();
        }
        catch
        {
            // Preserve the original capture or transcription error.
        }
    }

    private void OnAudioChunkAvailable(ReadOnlyMemory<float> chunk)
    {
        if (State.Status is not (
            DictationStatus.Starting or
            DictationStatus.Listening or
            DictationStatus.Finishing))
        {
            return;
        }

        lock (_lock)
        {
            _audioBuffer.AddRange(chunk.Span);
        }

        _streamingTranscriber?.AcceptAudio(chunk);
    }

    private void OnAudioLevelChanged(float newLevel)
    {
        Level += (newLevel - Level) * 0.35f;
        LevelChanged?.Invoke(Level);
    }

    private void SetState(DictationState newState)
    {
        State = newState;
        StateChanged?.Invoke(newState);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        if (_streamingTranscriber != null)
        {
            _streamingTranscriber.PartialTranscriptChanged -= OnPartialTranscriptChanged;
        }
        _audioCapture.Dispose();
        _hotkeySource.Dispose();
        _transcriber.Dispose();
    }
}
