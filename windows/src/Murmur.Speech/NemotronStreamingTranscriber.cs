using System.Threading.Channels;
using Murmur.Core.Abstractions;
using SherpaOnnx;

namespace Murmur.Speech;

/// <summary>
/// Speech transcriber powered by sherpa-onnx and the Nemotron 3.5 streaming int8 model.
/// </summary>
public sealed class NemotronStreamingTranscriber : IStreamingTranscriber
{
    internal sealed record NativeSession(Action<float[]> Accept, Func<bool> IsReady, Action Decode,
        Func<string> GetText, Action InputFinished, Action Dispose);
    private readonly Func<NativeSession> _createStream;
    private readonly Action _disposeRecognizer;
    private readonly Lock _lock = new();
    private const int MaximumPendingSamples = 144000;
    private const int MaximumPendingItems = 1024;
    private sealed class StreamingSession(NativeSession stream, Channel<float[]> channel, Action<Exception>? onFailure)
    {
        public NativeSession Stream { get; } = stream;
        public Channel<float[]> Channel { get; } = channel;
        public Task Worker { get; set; } = Task.CompletedTask;
        public int Cancelled;
        public Action<Exception>? OnFailure { get; } = onFailure;
        public Exception? Failure;
        public int PendingSamples;
        public int PendingItems;
        public TaskCompletionSource? Drained;
    }

    private StreamingSession? _session;
    private Task<string>? _closeTask;
    private Task? _disposeTask;
    private bool _disposed;
    internal (int Samples, int Items) PendingAudio
    {
        get { lock (_lock) return _session is { } s ? (s.PendingSamples, s.PendingItems) : (0, 0); }
    }

    /// <inheritdoc />
    public event Action<string>? PartialTranscriptChanged;

    /// <summary>Initializes a new instance of NemotronStreamingTranscriber.</summary>
    public NemotronStreamingTranscriber(string? modelDir = null)
    {
        var model = ModelManifest.NemotronStreaming160Ms;
        var dir = modelDir ?? ModelLocator.GetDefaultModelDirectory(model);
        var integrity = ModelLocator.GetIntegrityStatus(model, dir);
        if (integrity == ModelIntegrityStatus.Missing)
        {
            throw new DirectoryNotFoundException($"Nemotron streaming model files not found in: {dir}");
        }
        if (integrity == ModelIntegrityStatus.Corrupt)
            throw new InvalidDataException($"Nemotron streaming model integrity verification failed in: {dir}");
        // Valid and LegacyUnverified retain the existing installation compatibility.

        var config = new OnlineRecognizerConfig();
        config.FeatConfig.SampleRate = 16000;
        config.FeatConfig.FeatureDim = 80;

        config.ModelConfig.Transducer.Encoder = Path.Combine(dir, "encoder.int8.onnx");
        config.ModelConfig.Transducer.Decoder = Path.Combine(dir, "decoder.int8.onnx");
        config.ModelConfig.Transducer.Joiner = Path.Combine(dir, "joiner.int8.onnx");
        config.ModelConfig.Tokens = Path.Combine(dir, "tokens.txt");
        config.ModelConfig.NumThreads = 4;
        config.ModelConfig.Provider = "cpu";
        config.DecodingMethod = "greedy_search";

        var recognizer = new OnlineRecognizer(config);
        _createStream = () =>
        {
            var stream = recognizer.CreateStream();
            try { stream.SetOption("language", "auto"); }
            catch { stream.Dispose(); throw; }
            return new NativeSession(samples => stream.AcceptWaveform(16000, samples),
                () => recognizer.IsReady(stream), () => recognizer.Decode(stream),
                () => recognizer.GetResult(stream).Text ?? string.Empty, stream.InputFinished, stream.Dispose);
        };
        _disposeRecognizer = recognizer.Dispose;
    }

    internal NemotronStreamingTranscriber(Func<NativeSession> createStream, Action disposeRecognizer)
    {
        _createStream = createStream;
        _disposeRecognizer = disposeRecognizer;
    }

    /// <inheritdoc />
    public void BeginSession(Action<Exception>? onFailure = null)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session != null || _closeTask is { IsCompleted: false })
            {
                throw new InvalidOperationException("A streaming session is already active.");
            }

            _closeTask = null;

            var sessionStream = _createStream();
            try
            {
                var sessionChannel = Channel.CreateBounded<float[]>(new BoundedChannelOptions(MaximumPendingItems)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait
                });
                var session = new StreamingSession(sessionStream, sessionChannel, onFailure);
                _session = session;
                session.Worker = Task.Run(() => ProcessAudioLoopAsync(session));
            }
            catch
            {
                sessionStream.Dispose();
                throw;
            }
        }
    }

    /// <inheritdoc />
    public void AcceptAudio(ReadOnlyMemory<float> audioSamples)
    {
        if (audioSamples.Length == 0) return;

        lock (_lock)
        {
            if (_disposed || _closeTask != null || _session is not { } session ||
                Volatile.Read(ref session.Cancelled) != 0) return;
            // Include the item being decoded; check before allocating or adding to the budget.
            if (audioSamples.Length > MaximumPendingSamples - session.PendingSamples ||
                session.PendingItems >= MaximumPendingItems)
            {
                FailSessionLocked(session, new InvalidOperationException("StreamingBacklogExceeded"));
                return;
            }
            var copy = audioSamples.ToArray();
            session.PendingSamples += copy.Length;
            session.PendingItems++;
            if (!session.Channel.Writer.TryWrite(copy))
            {
                session.PendingSamples -= copy.Length;
                session.PendingItems--;
                FailSessionLocked(session, new InvalidOperationException("StreamingBacklogExceeded"));
            }
        }
    }

    private static void FailSessionLocked(StreamingSession session, Exception failure)
    {
        if (session.Failure != null) return;
        session.Failure = failure;
        Volatile.Write(ref session.Cancelled, 1);
        session.Channel.Writer.TryComplete();
        session.Drained?.TrySetResult();
        // Controller capture and native locks have opposite entry paths: never notify under this lock.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { session.OnFailure?.Invoke(failure); }
            catch { /* A notification must not escape into the capture/worker thread. */ }
        });
    }

    private async Task ProcessAudioLoopAsync(StreamingSession session)
    {
        var stream = session.Stream;
        var lastPartial = string.Empty;
        try
        {
            await foreach (var samples in session.Channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    if (Volatile.Read(ref session.Cancelled) != 0) return;
                    stream.Accept(samples);
                    while (Volatile.Read(ref session.Cancelled) == 0 && stream.IsReady())
                    {
                        stream.Decode();
                        if (Volatile.Read(ref session.Cancelled) != 0) return;
                        var text = stream.GetText();
                        if (text.Length > 0 && !string.Equals(text, lastPartial, StringComparison.Ordinal) &&
                            Volatile.Read(ref session.Cancelled) == 0)
                        {
                            lastPartial = text;
                            PartialTranscriptChanged?.Invoke(text);
                        }
                    }
                }
                finally
                {
                    lock (_lock)
                    {
                        session.PendingSamples -= samples.Length;
                        session.PendingItems--;
                        if (session.PendingItems == 0)
                        {
                            session.Drained?.TrySetResult();
                            session.Drained = null;
                        }
                    }
                }
            }
        }
        catch (ChannelClosedException)
        {
            // Normal channel completion
        }
        catch (Exception ex)
        {
            lock (_lock) FailSessionLocked(session, new InvalidOperationException("StreamingRecognitionFailed", ex));
        }
    }

    /// <inheritdoc />
    public async Task<string> CompleteSessionAsync(CancellationToken cancellationToken = default)
    {
        var closeTask = StartClosingSession(decodeFinal: true);
        return await closeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void CancelSession()
    {
        try
        {
            CancelSessionAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Cancellation is cleanup; the original capture or shutdown error wins.
        }
    }

    /// <inheritdoc />
    public async Task CancelSessionAsync()
    {
        await StartClosingSession(decodeFinal: false).ConfigureAwait(false);
    }

    private Task<string> StartClosingSession(bool decodeFinal)
    {
        lock (_lock)
        {
            if (!decodeFinal && _session is { } cancelling)
            {
                Volatile.Write(ref cancelling.Cancelled, 1);
                cancelling.Drained?.TrySetResult();
            }
            if (_closeTask != null)
            {
                return _closeTask;
            }

            if (_session is not { } session)
            {
                return Task.FromResult(string.Empty);
            }

            session.Channel.Writer.TryComplete();
            _closeTask = Task.Run(() => FinishClosingSessionAsync(session, decodeFinal));
            return _closeTask;
        }
    }

    private async Task<string> FinishClosingSessionAsync(StreamingSession session, bool decodeFinal)
    {
        var stream = session.Stream;
        try
        {
            await session.Worker.ConfigureAwait(false);
            if (session.Failure is { } failure) throw failure;

            if (decodeFinal && Volatile.Read(ref session.Cancelled) == 0)
            {
                stream.InputFinished();
                while (Volatile.Read(ref session.Cancelled) == 0 && stream.IsReady())
                {
                    stream.Decode();
                }
                if (Volatile.Read(ref session.Cancelled) == 0)
                    return stream.GetText();
            }
            return string.Empty;
        }
        finally
        {
            try { stream.Dispose(); }
            finally
            {
                lock (_lock)
                {
                    if (ReferenceEquals(_session, session)) _session = null;
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task<string> TranscribeAsync(ReadOnlyMemory<float> audioSamples, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StreamingSession session;
        lock (_lock)
        {
            BeginSession();
            session = _session!;
        }
        try
        {
            for (var offset = 0; offset < audioSamples.Length;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(MaximumPendingSamples, audioSamples.Length - offset);
                Task? drained;
                lock (_lock)
                {
                    if (session.Failure is { } failure) throw failure;
                    if (_disposed || !ReferenceEquals(_session, session) || _closeTask != null || session.Cancelled != 0)
                        throw new OperationCanceledException("Streaming session ended before batch completion.");
                    AcceptAudio(audioSamples.Slice(offset, count));
                    if (session.Failure is { } acceptFailure) throw acceptFailure;
                    drained = session.PendingItems == 0 ? null :
                        (session.Drained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
                }
                offset += count;
                if (drained != null) await drained.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            Task<string> completion;
            lock (_lock)
            {
                if (session.Failure is { } failure) throw failure;
                if (_disposed || !ReferenceEquals(_session, session) || _closeTask != null || session.Cancelled != 0)
                    throw new OperationCanceledException("Streaming session ended before batch completion.");
                completion = StartClosingSession(decodeFinal: true);
            }
            return await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Task cleanup;
            lock (_lock) cleanup = ReferenceEquals(_session, session) ? StartClosingSession(decodeFinal: false) : Task.CompletedTask;
            try { await cleanup.ConfigureAwait(false); }
            catch when (cancellationToken.IsCancellationRequested) { cancellationToken.ThrowIfCancellationRequested(); }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Task disposing;
        lock (_lock)
        {
            _disposed = true;
            _disposeTask ??= Task.Run(async () =>
            {
                try { await CancelSessionAsync().ConfigureAwait(false); }
                catch { /* Cleanup observes the session failure already delivered to the caller. */ }
                finally { _disposeRecognizer(); }
            });
            disposing = _disposeTask;
        }
        disposing.GetAwaiter().GetResult();
    }
}
