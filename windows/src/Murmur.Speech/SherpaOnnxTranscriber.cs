using Murmur.Core.Abstractions;
using SherpaOnnx;

namespace Murmur.Speech;

/// <summary>
/// Speech transcriber powered by sherpa-onnx and the Parakeet TDT int8 model.
/// </summary>
public sealed class SherpaOnnxTranscriber : ITranscriber
{
    private readonly OfflineRecognizer _recognizer;

    /// <summary>Initializes a new instance of SherpaOnnxTranscriber.</summary>
    public SherpaOnnxTranscriber(string? modelDir = null)
    {
        var dir = modelDir ?? ModelLocator.GetDefaultModelDirectory(ModelManifest.ParakeetV3);
        if (!ModelLocator.IsModelDownloaded(ModelManifest.ParakeetV3, dir))
        {
            throw new DirectoryNotFoundException($"Parakeet model files not found in: {dir}");
        }

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
            using var stream = _recognizer.CreateStream();
            var floatArray = audioSamples.ToArray();
            stream.AcceptWaveform(16000, floatArray);

            _recognizer.Decode(stream);
            var result = stream.Result.Text;
            return result ?? string.Empty;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _recognizer.Dispose();
    }
}
