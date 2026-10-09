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
public sealed class DictationController : IDisposable, IAsyncDisposable
{
    private sealed record RuntimeState(
        DictationProfile Profile,
        ITextFormatter Formatter,
        DictionaryCorrector Dictionary,
        TextInsertionMode InsertionMode);

    private sealed class ActiveSessionState(RuntimeState runtime, TextInjectionTarget target)
    {
        public RuntimeState Runtime { get; } = runtime;
        public TextInjectionTarget Target { get; } = target;
        public CancellationTokenSource Cancellation { get; } = new();
        public TaskCompletionSource CaptureStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? Failure { get; set; }
        public bool WorkStarted { get; set; }
        public bool InsertionCommitted { get; set; }
        public long StartedAt { get; } = Stopwatch.GetTimestamp();
        public long? FirstPartialAt { get; set; }
        public long? ReleasedAt { get; set; }
        public long? InjectedAt { get; set; }
        public long SampleCount { get; set; }
    }

    private readonly IAudioCapture _audioCapture;
    private readonly IHotkeySource _hotkeySource;
    private readonly ITranscriber _transcriber;
    private readonly IStreamingTranscriber? _streamingTranscriber;
    private readonly ITextInjector _textInjector;
    private readonly string _engine;

    private RuntimeState _runtimeState;
    private ActiveSessionState? _activeSessionState;

    private readonly List<float> _audioBuffer = [];
    private const int RetainedAudioCapacity = 16000 * 30;
    private readonly Lock _lock = new();
    private Task _sessionTask = Task.CompletedTask;
    private Task? _stopTask;
    private Task? _disposeTask;
    private bool _stopping;
    private bool _disposed;

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

    /// <summary>Fired once for valid final text after its insertion attempt, including insertion failure.
    /// Sessions cancelled before insertion starts do not publish completion.</summary>
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
        DictationProfile? profile = null,
        TextInsertionMode insertionMode = TextInsertionMode.AtCaret)
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
        _runtimeState = new RuntimeState(
            initialProfile,
            initialFormatter,
            initialDictionary,
            NormalizeInsertionMode(insertionMode));

        _hotkeySource.Pressed += OnHotkeyPressed;
        _hotkeySource.Released += OnHotkeyReleased;

        _audioCapture.AudioChunkAvailable += OnAudioChunkAvailable;
        _audioCapture.LevelChanged += OnAudioLevelChanged;
        _audioCapture.CaptureFailed += OnCaptureFailed;

        if (_streamingTranscriber != null)
        {
            _streamingTranscriber.PartialTranscriptChanged += OnPartialTranscriptChanged;
        }
    }

    /// <summary>Starts listening for hotkey events.</summary>
    public void Start()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopTask is { IsCompleted: false })
                throw new InvalidOperationException("Controller is still stopping.");
            _stopping = false;
            _stopTask = null;
            _hotkeySource.Start();
        }
    }

    /// <summary>Updates the profile and its formatter atomically for subsequent dictations.</summary>
    public void UpdateProfile(DictationProfile profile, ITextFormatter formatter)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(formatter);

        var current = Volatile.Read(ref _runtimeState);
        Volatile.Write(ref _runtimeState, new RuntimeState(profile, formatter, current.Dictionary, current.InsertionMode));
        ProfileChanged?.Invoke(profile);
    }

    /// <summary>Replaces dictionary rules for the next and later dictations.</summary>
    public void SetDictionary(IEnumerable<DictionaryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var current = Volatile.Read(ref _runtimeState);
        Volatile.Write(ref _runtimeState, new RuntimeState(
            current.Profile,
            current.Formatter,
            new DictionaryCorrector(entries),
            current.InsertionMode));
    }

    /// <summary>Sets the insertion mode for the next dictation session.</summary>
    public void SetTextInsertionMode(TextInsertionMode mode)
    {
        var current = Volatile.Read(ref _runtimeState);
        Volatile.Write(ref _runtimeState, new RuntimeState(
            current.Profile,
            current.Formatter,
            current.Dictionary,
            NormalizeInsertionMode(mode)));
    }

    /// <summary>Stops capture and listeners, waiting for session work. Use StopAsync on UI threads.</summary>
    public void Stop() => StopAsync().GetAwaiter().GetResult();

    /// <summary>Invalidates the session and waits for capture and recognition to finish.</summary>
    public Task StopAsync()
    {
        lock (_lock)
        {
            if (_stopTask != null) return _stopTask;
            _stopping = true;
            var session = _activeSessionState;
            _activeSessionState = null;
            if (session != null)
            {
                // An insertion already committed to Windows cannot be revoked; finish its history as well.
                if (!session.InsertionCommitted) session.Cancellation.Cancel();
                if (!session.WorkStarted)
                {
                    session.WorkStarted = true;
                    _sessionTask = Task.Run(() => AbortSessionAsync(session));
                }
            }
            var work = _sessionTask;
            var started = session?.CaptureStarted.Task ?? Task.CompletedTask;
            _stopTask = Task.Run(() => StopCoreAsync(work, started));
            return _stopTask;
        }
    }

    private async Task StopCoreAsync(Task sessionWork, Task captureStarted)
    {
        _hotkeySource.Stop();
        await captureStarted.ConfigureAwait(false);
        await CleanupCaptureAsync().ConfigureAwait(false);
        await sessionWork.ConfigureAwait(false);
        lock (_lock)
        {
            ClearAudioBuffer();
            SetState(DictationState.Idle);
        }
    }

    private void OnHotkeyPressed()
    {
        ActiveSessionState session;
        lock (_lock)
        {
            if (_stopping || _disposed || !_sessionTask.IsCompleted ||
                State.Status is not (DictationStatus.Idle or DictationStatus.Error)) return;

            try
            {
                var runtime = Volatile.Read(ref _runtimeState);
                var target = _textInjector.CaptureTarget();
                session = new ActiveSessionState(runtime, target);
                _activeSessionState = session;
                ClearAudioBuffer();

                CurrentTranscript = string.Empty;
                TranscriptChanged?.Invoke(string.Empty);
                SetState(DictationState.Starting);

            }
            catch (Exception ex)
            {
                SetState(DictationState.ErrorState(GetErrorMessage(ex, "Microphone error")));
                return;
            }
        }
        // Capture invokes callbacks under its own lock. Never enter it holding the session lock.
        try
        {
            if (!IsCurrentSession(session)) return;
            _streamingTranscriber?.BeginSession();
            _audioCapture.Start();
            lock (_lock)
            {
                if (IsCurrent(session) && !session.WorkStarted) SetState(DictationState.Listening);
            }
        }
        catch (Exception ex)
        {
            lock (_lock) BeginAbort(session, ex);
        }
        finally { session.CaptureStarted.TrySetResult(); }
    }

    private void OnHotkeyReleased()
    {
        lock (_lock)
        {
            if (_activeSessionState is not { WorkStarted: false } session || !IsCurrent(session)) return;
            session.ReleasedAt = Stopwatch.GetTimestamp();
            session.WorkStarted = true;
            SetState(DictationState.Finishing);
            _sessionTask = Task.Run(() => FinishSessionAsync(session, session.Cancellation.Token));
        }
    }

    private async Task FinishSessionAsync(ActiveSessionState session, CancellationToken cancellationToken)
    {
        string? error = null;
        try
        {
            await session.CaptureStarted.Task.ConfigureAwait(false);
            await Task.Delay(40, cancellationToken).ConfigureAwait(false);
            if (!IsCurrentSession(session)) return;
            // A cancelled wait must not let native resources outlive capture shutdown.
            await _audioCapture.StopAsync(CancellationToken.None).ConfigureAwait(false);

            float[] audioCopy;
            long sampleCount;
            lock (_lock)
            {
                if (!IsCurrent(session)) return;
                Level = 0;
                LevelChanged?.Invoke(0);
                sampleCount = session.SampleCount;
                audioCopy = _streamingTranscriber is null ? [.. _audioBuffer] : [];
                ClearAudioBuffer();
            }

            if (audioCopy.Length == 0 && _streamingTranscriber is null)
                return;

            var audioDurationMs = sampleCount * 1000.0 / 16000.0;
            var sw = Stopwatch.StartNew();
            var raw = _streamingTranscriber is null
                ? await _transcriber.TranscribeAsync(audioCopy, cancellationToken).ConfigureAwait(false)
                : await _streamingTranscriber.CompleteSessionAsync(cancellationToken).ConfigureAwait(false);
            audioCopy = [];
            if (!IsCurrentSession(session)) return;
            sw.Stop();
            var inferenceMs = sw.Elapsed.TotalMilliseconds;
            var rtf = audioDurationMs > 0 ? (inferenceMs / audioDurationMs) : 0.0;

            if (string.IsNullOrWhiteSpace(raw))
            {
                if (IsCurrentSession(session)) RecordMetrics(session, audioDurationMs, inferenceMs, rtf, 0);
                return;
            }

            var cleaned = session.Runtime.Formatter.Format(raw);
            var (corrected, _) = session.Runtime.Dictionary.Apply(cleaned);
            if (string.IsNullOrWhiteSpace(corrected))
            {
                if (IsCurrentSession(session)) RecordMetrics(session, audioDurationMs, inferenceMs, rtf, 0);
                return;
            }
            var completed = new CompletedDictation(
                DateTimeOffset.Now, _engine, audioDurationMs, inferenceMs, corrected);
            lock (_lock)
            {
                if (!IsCurrent(session)) return;
                CurrentTranscript = corrected;
                TranscriptChanged?.Invoke(corrected);
                if (!IsCurrent(session)) return;
                session.InsertionCommitted = true;
            }
            // SendInput invokes keyboard hooks. Holding the session lock here deadlocks our own hook.
            try
            {
                _textInjector.Insert(corrected, session.Target, session.Runtime.InsertionMode);
                session.InjectedAt = Stopwatch.GetTimestamp();
            }
            catch (Exception ex) { error = GetErrorMessage(ex, "Text insertion error"); }

            lock (_lock)
            {
                if (IsCurrent(session))
                {
                    if (error != null) SetState(DictationState.ErrorState(error));
                    else
                    {
                        try { TextInjected?.Invoke(completed); }
                        catch { /* Feedback failure must not turn successful injection into an error. */ }
                    }
                }
            }
            RecordMetrics(session, audioDurationMs, inferenceMs, rtf, corrected.Length);
            try { DictationCompleted?.Invoke(completed); }
            catch { /* History failure cannot change the completed insertion attempt. */ }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stop/capture failure owns cancellation; native work still needs cleanup below.
        }
        catch (Exception ex)
        {
            error = GetErrorMessage(ex, "Transcription error");
        }
        finally
        {
            await CleanupCaptureAsync().ConfigureAwait(false);
            EndSession(session, error);
        }
    }

    private void OnPartialTranscriptChanged(string text)
    {
        lock (_lock)
        {
            if (_activeSessionState is not { } session || !IsCurrent(session) ||
                State.Status is not (DictationStatus.Listening or DictationStatus.Finishing) ||
                string.IsNullOrWhiteSpace(text)) return;
            session.FirstPartialAt ??= Stopwatch.GetTimestamp();
            CurrentTranscript = text;
            TranscriptChanged?.Invoke(text);
        }
    }

    private void RecordMetrics(ActiveSessionState session, double audioMs, double inferenceMs, double rtf, int length)
    {
        var metrics = new DictationMetrics(audioMs, inferenceMs, rtf, length)
        {
            IsStreaming = _streamingTranscriber != null,
            FirstPartialMs = session.FirstPartialAt is { } first
                ? Stopwatch.GetElapsedTime(session.StartedAt, first).TotalMilliseconds : null,
            ReleaseToInjectionMs = session.ReleasedAt is { } released && session.InjectedAt is { } injected
                ? Stopwatch.GetElapsedTime(released, injected).TotalMilliseconds : null
        };
        try { MetricsRecorded?.Invoke(metrics); }
        catch { /* Diagnostics must not alter the result of text insertion. */ }
    }

    private bool IsCurrent(ActiveSessionState session) =>
        !_stopping && !_disposed && ReferenceEquals(_activeSessionState, session) &&
        !session.Cancellation.IsCancellationRequested;

    private bool IsCurrentSession(ActiveSessionState session)
    {
        lock (_lock) return IsCurrent(session);
    }

    private void BeginAbort(ActiveSessionState session, Exception exception)
    {
        session.Failure ??= exception;
        session.Cancellation.Cancel();
        if (session.WorkStarted) return;
        session.WorkStarted = true;
        _sessionTask = Task.Run(() => AbortSessionAsync(session));
    }

    private async Task AbortSessionAsync(ActiveSessionState session)
    {
        await session.CaptureStarted.Task.ConfigureAwait(false);
        await CleanupCaptureAsync().ConfigureAwait(false);
        EndSession(session, null);
    }

    private async Task CleanupCaptureAsync()
    {
        try { await _audioCapture.StopAsync().ConfigureAwait(false); }
        catch { /* Preserve the original capture or transcription failure. */ }
        try
        {
            if (_streamingTranscriber != null)
                await _streamingTranscriber.CancelSessionAsync().ConfigureAwait(false);
        }
        catch { /* Cancellation waits for native cleanup, even when decoding failed. */ }
    }

    private void EndSession(ActiveSessionState session, string? error)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_activeSessionState, session))
            {
                _activeSessionState = null;
                ClearAudioBuffer();
                Level = 0;
                LevelChanged?.Invoke(0);
                error = session.Failure is { } failure ? GetErrorMessage(failure, "Microphone error") : error;
                SetState(error == null ? DictationState.Idle : DictationState.ErrorState(error));
            }
            session.Cancellation.Dispose();
        }
    }

    private void OnAudioChunkAvailable(ReadOnlyMemory<float> chunk)
    {
        lock (_lock)
        {
            if (_activeSessionState is not { } session || !IsCurrent(session) || !State.IsActive) return;
            session.SampleCount += chunk.Length;
            if (_streamingTranscriber is null) _audioBuffer.AddRange(chunk.Span);
            _streamingTranscriber?.AcceptAudio(chunk);
        }
    }

    private void OnAudioLevelChanged(float newLevel)
    {
        lock (_lock)
        {
            if (_activeSessionState is not { } session || !IsCurrent(session)) return;
            Level += (newLevel - Level) * 0.35f;
            LevelChanged?.Invoke(Level);
        }
    }

    private void ClearAudioBuffer()
    {
        _audioBuffer.Clear();
        if (_audioBuffer.Capacity > RetainedAudioCapacity) _audioBuffer.Capacity = 0;
    }

    private void OnCaptureFailed(Exception exception)
    {
        lock (_lock)
        {
            if (_activeSessionState is { } session && !_stopping)
                BeginAbort(session, exception);
        }
    }

    private void SetState(DictationState newState)
    {
        State = newState;
        StateChanged?.Invoke(newState);
    }

    private static TextInsertionMode NormalizeInsertionMode(TextInsertionMode mode) =>
        mode is TextInsertionMode.AtCaret or TextInsertionMode.DocumentEnd
            ? mode
            : TextInsertionMode.DocumentEnd;

    private static string GetErrorMessage(Exception exception, string fallbackPrefix) =>
        exception is TextInjectionException injection
            ? injection.UserMessageKey
            : $"{fallbackPrefix}: {exception.Message}";

    /// <inheritdoc />
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            if (_disposeTask != null) return new ValueTask(_disposeTask);
            _disposed = true;
            var stopped = StopAsync();
            _disposeTask = Task.Run(() => DisposeCoreAsync(stopped));
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync(Task stopped)
    {
        await stopped.ConfigureAwait(false);
        _hotkeySource.Pressed -= OnHotkeyPressed;
        _hotkeySource.Released -= OnHotkeyReleased;
        _audioCapture.AudioChunkAvailable -= OnAudioChunkAvailable;
        _audioCapture.LevelChanged -= OnAudioLevelChanged;
        if (_streamingTranscriber != null)
        {
            _streamingTranscriber.PartialTranscriptChanged -= OnPartialTranscriptChanged;
        }
        _audioCapture.CaptureFailed -= OnCaptureFailed;
        try { _audioCapture.Dispose(); }
        finally
        {
            try { _hotkeySource.Dispose(); }
            finally { _transcriber.Dispose(); }
        }
    }
}
