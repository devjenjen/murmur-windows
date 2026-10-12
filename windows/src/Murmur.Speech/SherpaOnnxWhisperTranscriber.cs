using Murmur.Core.Abstractions;
using SherpaOnnx;

namespace Murmur.Speech;

/// <summary>
/// Speech transcriber powered by sherpa-onnx and the Whisper Large-v3-Turbo int8 model.
/// </summary>
public sealed class SherpaOnnxWhisperTranscriber : ITranscriber
{
    private readonly OfflineRecognizer _recognizer;
    private readonly Lock _resourceLock = new();
    private bool _disposed;

    /// <summary>Initializes a new instance of SherpaOnnxWhisperTranscriber.</summary>
    public SherpaOnnxWhisperTranscriber(string? modelDir = null)
    {
        var model = ModelManifest.WhisperV3Turbo;
        var dir = modelDir ?? ModelLocator.GetDefaultModelDirectory(model);
        var integrity = ModelLocator.GetIntegrityStatus(model, dir);
        if (integrity == ModelIntegrityStatus.Missing)
        {
            throw new DirectoryNotFoundException($"Whisper model files not found in: {dir}");
        }
        if (integrity == ModelIntegrityStatus.Corrupt)
        {
            throw new InvalidDataException($"Whisper model integrity verification failed in: {dir}");
        }

        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = 16000;
        config.FeatConfig.FeatureDim = 128;

        config.ModelConfig.Whisper.Encoder = Path.Combine(dir, "turbo-encoder.int8.onnx");
        config.ModelConfig.Whisper.Decoder = Path.Combine(dir, "turbo-decoder.int8.onnx");
        config.ModelConfig.Whisper.Language = "";
        config.ModelConfig.Whisper.Task = "transcribe";
        config.ModelConfig.Tokens = Path.Combine(dir, "tokens.txt");
        config.ModelConfig.NumThreads = 4;
        config.ModelConfig.Provider = "cpu";
        config.DecodingMethod = "greedy_search";

        _recognizer = new OfflineRecognizer(config);
    }

    /// <inheritdoc />
    public Task<string> TranscribeAsync(ReadOnlyMemory<float> audioSamples, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_resourceLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = _recognizer.CreateStream();
                var floatArray = audioSamples.ToArray();
                stream.AcceptWaveform(16000, floatArray);

                _recognizer.Decode(stream);
                cancellationToken.ThrowIfCancellationRequested();
                return stream.Result.Text ?? string.Empty;
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_resourceLock)
        {
            if (_disposed) return;
            _disposed = true;
            _recognizer.Dispose();
        }
    }
}
