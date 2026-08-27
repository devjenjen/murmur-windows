namespace Murmur.Core.Abstractions;

/// <summary>
/// Speech transcriber supporting streaming recognition with live partial results.
/// </summary>
public interface IStreamingTranscriber : ITranscriber
{
    /// <summary>Fired when a new intermediate partial transcript is available.</summary>
    event Action<string>? PartialTranscriptChanged;

    /// <summary>Begins a new live streaming recognition session.</summary>
    void BeginSession();

    /// <summary>Feeds audio samples into the active streaming session.</summary>
    void AcceptAudio(ReadOnlyMemory<float> audioSamples);

    /// <summary>Completes the active streaming session and returns final transcript text.</summary>
    Task<string> CompleteSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>Cancels and releases the active streaming session.</summary>
    void CancelSession();
}
