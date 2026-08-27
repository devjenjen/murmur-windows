using System.Threading.Channels;
using Murmur.Core.Abstractions;
using SherpaOnnx;

namespace Murmur.Speech;

/// <summary>
/// Speech transcriber powered by sherpa-onnx and the Nemotron 3.5 streaming int8 model.
/// </summary>
public sealed class NemotronStreamingTranscriber : IStreamingTranscriber
{
    private readonly OnlineRecognizer _recognizer;
    private readonly Lock _lock = new();
    private OnlineStream? _stream;
    private Channel<float[]>? _channel;
    private Task? _workerTask;
    private Task<string>? _closeTask;
    private string _lastPartial = string.Empty;

    /// <inheritdoc />
    public event Action<string>? PartialTranscriptChanged;

    /// <summary>Initializes a new instance of NemotronStreamingTranscriber.</summary>
    public NemotronStreamingTranscriber(string? modelDir = null)
    {
        var model = ModelManifest.NemotronStreaming160Ms;
        var dir = modelDir ?? ModelLocator.GetDefaultModelDirectory(model);
        if (!ModelLocator.IsModelDownloaded(model, dir))
        {
            throw new DirectoryNotFoundException($"Nemotron streaming model files not found in: {dir}");
        }

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

        _recognizer = new OnlineRecognizer(config);
    }

    /// <inheritdoc />
    public void BeginSession()
    {
        lock (_lock)
        {
            if (_stream != null || _closeTask is { IsCompleted: false })
            {
                throw new InvalidOperationException("A streaming session is already active.");
            }

            _closeTask = null;

            _stream = _recognizer.CreateStream();
            _stream.SetOption("language", "auto");
            _lastPartial = string.Empty;
            _channel = Channel.CreateUnbounded<float[]>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            _workerTask = Task.Run(ProcessAudioLoopAsync);
        }
    }

    /// <inheritdoc />
    public void AcceptAudio(ReadOnlyMemory<float> audioSamples)
    {
        if (audioSamples.Length == 0) return;

        var copy = audioSamples.ToArray();
        lock (_lock)
        {
            _channel?.Writer.TryWrite(copy);
        }
    }

    private async Task ProcessAudioLoopAsync()
    {
        Channel<float[]>? channel;
        OnlineStream? stream;

        lock (_lock)
        {
            channel = _channel;
            stream = _stream;
        }

        if (channel == null || stream == null) return;

        try
        {
            await foreach (var samples in channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                stream.AcceptWaveform(16000, samples);
                while (_recognizer.IsReady(stream))
                {
                    _recognizer.Decode(stream);
                    var text = _recognizer.GetResult(stream).Text ?? string.Empty;
                    if (text.Length > 0 && !string.Equals(text, _lastPartial, StringComparison.Ordinal))
                    {
                        _lastPartial = text;
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
            StartClosingSession(decodeFinal: false).GetAwaiter().GetResult();
        }
        catch
        {
            // Cancellation is cleanup; the original capture or shutdown error wins.
        }
    }

    private Task<string> StartClosingSession(bool decodeFinal)
    {
        lock (_lock)
        {
            if (_closeTask != null)
            {
                return _closeTask;
            }

            if (_stream == null)
            {
                return Task.FromResult(string.Empty);
            }

            _channel?.Writer.TryComplete();
            var stream = _stream;
            var worker = _workerTask;
            _stream = null;
            _channel = null;
            _workerTask = null;
            _lastPartial = string.Empty;

            var completion = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _closeTask = completion.Task;
            _ = FinishClosingSessionAsync(stream, worker, decodeFinal, completion);
            return _closeTask;
        }
    }

    private async Task FinishClosingSessionAsync(
        OnlineStream stream,
        Task? worker,
        bool decodeFinal,
        TaskCompletionSource<string> completion)
    {
        var resultText = string.Empty;
        Exception? failure = null;
        try
        {
            if (worker != null)
            {
                await worker.ConfigureAwait(false);
            }

            if (decodeFinal)
            {
                stream.InputFinished();
                while (_recognizer.IsReady(stream))
                {
                    _recognizer.Decode(stream);
                }
                resultText = _recognizer.GetResult(stream).Text ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        try
        {
            stream.Dispose();
        }
        catch (Exception ex)
        {
            failure ??= ex;
        }

        if (failure == null)
        {
            completion.TrySetResult(resultText);
        }
        else
        {
            completion.TrySetException(failure);
        }
    }

    /// <inheritdoc />
    public async Task<string> TranscribeAsync(ReadOnlyMemory<float> audioSamples, CancellationToken cancellationToken = default)
    {
        BeginSession();
        AcceptAudio(audioSamples);
        return await CompleteSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CancelSession();
        _recognizer.Dispose();
    }
}
