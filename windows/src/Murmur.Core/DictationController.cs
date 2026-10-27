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
        TextInsertionMode InsertionMode,
        bool EnableSelfCorrection = true,
        bool RestorePreviousWindow = false);

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
        public Task InFlight { get; set; } = Task.CompletedTask;
        public bool Ended { get; set; }
        public bool DurationLimited { get; set; }
    }

    private readonly IAudioCapture _audioCapture;
    private readonly IHotkeySource _hotkeySource;
    private readonly ITranscriber _transcriber;
    private readonly IStreamingTranscriber? _streamingTranscriber;
    private readonly ITextInjector _textInjector;
    private readonly string _engine;
    private volatile ISpeechRepairer? _speechRepairer;

    private RuntimeState _runtimeState;
    private ActiveSessionState? _activeSessionState;

    private readonly List<float> _audioBuffer = [];
    private const int RetainedAudioCapacity = 16000 * 30;
    private const int MaximumRecordingSamples = 16000 * 60 * 5;
    private readonly Lock _lock = new();
    private Task _sessionTask = Task.CompletedTask;
    private Task? _stopTask;
    private Task? _disposeTask;
    private Task? _stopCompletion;
    private Task? _disposeCompletion;
    private volatile bool _requiresRestart;
    private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(3);
    internal Func<Task, TimeSpan, CancellationToken, Task> WaitForWorkAsync { get; set; } =
        static (work, budget, cancellation) => work.WaitAsync(budget, cancellation);
    internal Task DisposalCompletion => _disposeCompletion ?? Task.CompletedTask;
    internal Task SessionCompletion => _sessionTask;

    /// <summary>True after a deadline leaves native/capture work in quarantine; restart the app before reuse.</summary>
    public bool RequiresRestart => _requiresRestart;
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
        TextInsertionMode insertionMode = TextInsertionMode.AtCaret,
        ISpeechRepairer? speechRepairer = null,
        bool enableSelfCorrection = true,
        bool restorePreviousWindow = false)
    {
        _audioCapture = audioCapture ?? throw new ArgumentNullException(nameof(audioCapture));
        _hotkeySource = hotkeySource ?? throw new ArgumentNullException(nameof(hotkeySource));
        _transcriber = transcriber ?? throw new ArgumentNullException(nameof(transcriber));
        _streamingTranscriber = transcriber as IStreamingTranscriber;
        _textInjector = textInjector ?? throw new ArgumentNullException(nameof(textInjector));
        _speechRepairer = speechRepairer;
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
            NormalizeInsertionMode(insertionMode),
            enableSelfCorrection,
            restorePreviousWindow);

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
            if (_requiresRestart) throw new InvalidOperationException("RecognitionRestartRequired");
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
        Volatile.Write(ref _runtimeState, current with { Profile = profile, Formatter = formatter });
        ProfileChanged?.Invoke(profile);
    }

    /// <summary>Replaces dictionary rules for the next and later dictations.</summary>
    public void SetDictionary(IEnumerable<DictionaryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var current = Volatile.Read(ref _runtimeState);
        Volatile.Write(ref _runtimeState, current with { Dictionary = new DictionaryCorrector(entries) });
    }

    /// <summary>Sets the insertion mode for the next dictation session.</summary>
    public void SetTextInsertionMode(TextInsertionMode mode)
    {
        var current = Volatile.Read(ref _runtimeState);
        Volatile.Write(ref _runtimeState, current with { InsertionMode = NormalizeInsertionMode(mode) });
    }

    /// <summary>Sets the speech repairer for conversational self-correction.</summary>
    public void SetSpeechRepairer(ISpeechRepairer? speechRepairer)
    {
        _speechRepairer = speechRepairer;
    }

    /// <summary>Enables or disables conversational self-correction for subsequent dictations.</summary>
    public void SetSelfCorrectionEnabled(bool enabled)
    {
        var current = Volatile.Read(ref _runtimeState);
        Volatile.Write(ref _runtimeState, current with { EnableSelfCorrection = enabled });
    }

    /// <summary>Sets whether focus should be restored to the previously active window after text injection.</summary>
    public void SetRestorePreviousWindow(bool restore)
    {
        var current = Volatile.Read(ref _runtimeState);
        Volatile.Write(ref _runtimeState, current with { RestorePreviousWindow = restore });
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
            _stopCompletion = Task.Run(() => StopCoreAsync(work, started));
            _stopTask = WaitForShutdownAsync(_stopCompletion);
            ObserveFailure(_stopTask);
            return _stopTask;
        }
    }

    private async Task StopCoreAsync(Task sessionWork, Task captureStarted)
    {
        try { _hotkeySource.Stop(); }
        catch { RequireRestart(null); }
        await captureStarted.ConfigureAwait(false);
        await CleanupCaptureAsync().ConfigureAwait(false);
        await sessionWork.ConfigureAwait(false);
        lock (_lock)
        {
            ClearAudioBuffer();
            SetState(_requiresRestart ? DictationState.ErrorState("RecognitionRestartRequired") : DictationState.Idle);
        }
    }

    private static void ObserveFailure(Task work) => _ = work.ContinueWith(
        static completed => { _ = completed.Exception; }, CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private void RequireRestart(ActiveSessionState? session)
    {
        lock (_lock)
        {
            _requiresRestart = true;
            if (session is { Ended: false, InsertionCommitted: false }) session.Cancellation.Cancel();
            SetState(DictationState.ErrorState("RecognitionRestartRequired"));
        }
    }

    private async Task WaitForShutdownAsync(Task work)
    {
        try { await WaitForWorkAsync(work, ShutdownBudget, CancellationToken.None).ConfigureAwait(false); }
        catch (TimeoutException) { RequireRestart(null); throw; }
    }

    private async Task WaitForOperationAsync(ActiveSessionState session, Task work, TimeSpan budget, CancellationToken cancellationToken)
    {
        session.InFlight = work;
        try { await WaitForWorkAsync(work, budget, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { RequireRestart(session); throw; }
    }

    private void OnHotkeyPressed()
    {
        ActiveSessionState session;
        lock (_lock)
        {
            if (_stopping || _disposed || _requiresRestart || !_sessionTask.IsCompleted ||
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
            _streamingTranscriber?.BeginSession(ex =>
            {
                lock (_lock)
                {
                    if (IsCurrent(session)) BeginAbort(session, ex);
                }
            });
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
            await WaitForOperationAsync(session, Task.Run(() => _audioCapture.StopAsync(CancellationToken.None), CancellationToken.None),
                ShutdownBudget, cancellationToken).ConfigureAwait(false);

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
            var recognition = _streamingTranscriber is null
                ? Task.Run(() => _transcriber.TranscribeAsync(audioCopy, cancellationToken), CancellationToken.None)
                : Task.Run(() => _streamingTranscriber.CompleteSessionAsync(cancellationToken), CancellationToken.None);
            var inferenceBudget = _streamingTranscriber is null
                ? TimeSpan.FromSeconds(Math.Max(60, sampleCount * 2.0 / 16000)) : TimeSpan.FromSeconds(30);
            await WaitForOperationAsync(session, recognition, inferenceBudget, cancellationToken).ConfigureAwait(false);
            var raw = await recognition.ConfigureAwait(false);
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

            lock (_lock)
            {
                if (!IsCurrent(session)) return;
                CurrentTranscript = raw;
                TranscriptChanged?.Invoke(raw);
            }
            var repaired = raw;
            var repairer = _speechRepairer;
            if (session.Runtime.EnableSelfCorrection && repairer != null)
            {
                try
                {
                    var repair = Task.Run(() => repairer.RepairAsync(raw, cancellationToken), CancellationToken.None);
                    await WaitForOperationAsync(session, repair, inferenceBudget, cancellationToken).ConfigureAwait(false);
                    var repairResult = await repair.ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!string.IsNullOrWhiteSpace(repairResult))
                    {
                        repaired = repairResult;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Graceful fallback to raw on repairer exception
                    repaired = raw;
                }
            }

            if (!IsCurrentSession(session)) return;

            var cleaned = session.Runtime.Formatter.Format(repaired);
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
                _textInjector.Insert(corrected, session.Target, session.Runtime.InsertionMode, session.Runtime.RestorePreviousWindow);
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
            await WaitForActualWorkAsync(session).ConfigureAwait(false);
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
        !_stopping && !_disposed && !_requiresRestart && ReferenceEquals(_activeSessionState, session) &&
        !session.Cancellation.IsCancellationRequested;

    private bool IsCurrentSession(ActiveSessionState session)
    {
        lock (_lock) return IsCurrent(session);
    }

    private void BeginAbort(ActiveSessionState session, Exception exception)
    {
        if (session.Ended) return;
        session.Failure ??= exception;
        session.Cancellation.Cancel();
        SetState(DictationState.ErrorState(GetErrorMessage(exception, "Microphone error")));
        if (!session.WorkStarted)
        {
            session.WorkStarted = true;
            _sessionTask = Task.Run(() => AbortSessionAsync(session));
        }
        _ = MonitorAbortAsync(_sessionTask);
    }

    private async Task MonitorAbortAsync(Task work)
    {
        try { await WaitForShutdownAsync(work).ConfigureAwait(false); }
        catch { /* The public error is already reported; actual cleanup retains resource ownership. */ }
    }

    private static async Task WaitForActualWorkAsync(ActiveSessionState session)
    {
        try { await session.InFlight.ConfigureAwait(false); }
        catch { /* Observe late native failures; never release before the actual task has finished. */ }
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
        catch
        {
            if (_audioCapture.IsRunning)
            {
                RequireRestart(null);
                // Capture did not confirm shutdown: retain ownership until process exit.
                await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
            }
        }
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
                error = _requiresRestart ? "RecognitionRestartRequired"
                    : session.Failure is { } failure ? GetErrorMessage(failure, "Microphone error") : error;
                error ??= session.DurationLimited ? "RecordingDurationLimit" : null;
                SetState(error == null ? DictationState.Idle : DictationState.ErrorState(error));
            }
            session.Ended = true;
            session.Cancellation.Dispose();
        }
    }

    private void OnAudioChunkAvailable(ReadOnlyMemory<float> chunk)
    {
        lock (_lock)
        {
            if (_activeSessionState is not { } session || !IsCurrent(session) || !State.IsActive || session.DurationLimited) return;
            try
            {
                var count = (int)Math.Min(chunk.Length, MaximumRecordingSamples - session.SampleCount);
                var accepted = chunk[..count];
                if (_streamingTranscriber is null) _audioBuffer.AddRange(accepted.Span);
                else _streamingTranscriber.AcceptAudio(accepted);
                session.SampleCount += count;
                if (session.SampleCount == MaximumRecordingSamples)
                {
                    session.DurationLimited = true;
                    if (!session.WorkStarted)
                    {
                        session.ReleasedAt = Stopwatch.GetTimestamp();
                        session.WorkStarted = true;
                        SetState(new DictationState(DictationStatus.Finishing, "RecordingDurationLimit"));
                        _sessionTask = Task.Run(() => FinishSessionAsync(session, session.Cancellation.Token));
                    }
                }
            }
            catch (Exception ex) { BeginAbort(session, ex); }
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
        exception.Message is "StreamingBacklogExceeded" or "StreamingRecognitionFailed"
            ? exception.Message
            : exception is TextInjectionException injection
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
            _ = StopAsync();
            _disposeCompletion = Task.Run(() => DisposeCoreAsync(_stopCompletion!));
            ObserveFailure(_disposeCompletion);
            _disposeTask = WaitForShutdownAsync(_disposeCompletion);
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
            finally
            {
                try { _transcriber.Dispose(); }
                finally { (_speechRepairer as IDisposable)?.Dispose(); }
            }
        }
    }
}
