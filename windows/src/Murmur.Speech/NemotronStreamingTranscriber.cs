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
    private sealed class StreamingSession(NativeSession stream, Channel<float[]> channel)
    {
        public NativeSession Stream { get; } = stream;
        public Channel<float[]> Channel { get; } = channel;
        public Task Worker { get; set; } = Task.CompletedTask;
        public int Cancelled;
    }

    private StreamingSession? _session;
    private Task<string>? _closeTask;
    private Task? _disposeTask;
    private bool _disposed;

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
    public void BeginSession()
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
                var sessionChannel = Channel.CreateUnbounded<float[]>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
                var session = new StreamingSession(sessionStream, sessionChannel);
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
            session.Channel.Writer.TryWrite(audioSamples.ToArray());
        }
    }

    private async Task ProcessAudioLoopAsync(StreamingSession session)
    {
        var stream = session.Stream;
        var lastPartial = string.Empty;
        try
        {
            await foreach (var samples in session.Channel.Reader.ReadAllAsync().ConfigureAwait(false))
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
        }
        catch (ChannelClosedException)
        {
            // Normal channel completion
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
                Volatile.Write(ref cancelling.Cancelled, 1);
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
        BeginSession();
        try
        {
            AcceptAudio(audioSamples);
            return await CompleteSessionAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await CancelSessionAsync().ConfigureAwait(false);
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
                finally { _disposeRecognizer(); }
            });
            disposing = _disposeTask;
        }
        disposing.GetAwaiter().GetResult();
    }
}
