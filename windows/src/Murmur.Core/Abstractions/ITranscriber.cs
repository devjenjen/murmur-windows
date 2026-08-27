namespace Murmur.Core.Abstractions;

/// <summary>
/// Transcribes 16kHz mono float32 audio samples into text.
/// </summary>
public interface ITranscriber : IDisposable
{
    /// <summary>Transcribes the given audio samples.</summary>
    Task<string> TranscribeAsync(ReadOnlyMemory<float> audioSamples, CancellationToken cancellationToken = default);
}
