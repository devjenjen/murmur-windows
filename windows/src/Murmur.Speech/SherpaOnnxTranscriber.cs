using Murmur.Core.Abstractions;
using SherpaOnnx;

namespace Murmur.Speech;

/// <summary>
/// Speech transcriber powered by sherpa-onnx and the Parakeet TDT int8 model.
/// </summary>
public sealed class SherpaOnnxTranscriber : ITranscriber
{
    private readonly OfflineRecognizer _recognizer;
    private readonly Lock _resourceLock = new();
    private bool _disposed;

    /// <summary>Initializes a new instance of SherpaOnnxTranscriber.</summary>
    public SherpaOnnxTranscriber(string? modelDir = null)
    {
        var dir = modelDir ?? ModelLocator.GetDefaultModelDirectory(ModelManifest.ParakeetV3);
        var integrity = ModelLocator.GetIntegrityStatus(ModelManifest.ParakeetV3, dir);
        if (integrity == ModelIntegrityStatus.Missing)
        {
            throw new DirectoryNotFoundException($"Parakeet model files not found in: {dir}");
        }
        if (integrity == ModelIntegrityStatus.Corrupt)
            throw new InvalidDataException($"Parakeet model integrity verification failed in: {dir}");
        // Valid and LegacyUnverified retain the existing installation compatibility.

        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = 16000;
        config.FeatConfig.FeatureDim = 128;

        config.ModelConfig.Transducer.Encoder = Path.Combine(dir, "encoder.int8.onnx");
        config.ModelConfig.Transducer.Decoder = Path.Combine(dir, "decoder.int8.onnx");
        config.ModelConfig.Transducer.Joiner = Path.Combine(dir, "joiner.int8.onnx");
        config.ModelConfig.Tokens = Path.Combine(dir, "tokens.txt");
        config.ModelConfig.ModelType = "nemo_transducer";
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
