namespace Murmur.Core.State;

/// <summary>
/// Dictation timing and performance metrics.
/// </summary>
/// <param name="AudioDurationMs">Audio duration in milliseconds.</param>
/// <param name="InferenceDurationMs">Offline inference or remaining streaming completion duration in milliseconds.</param>
/// <param name="RealTimeFactor">Inference duration / audio duration; not full inference RTF for streaming.</param>
/// <param name="TranscriptLength">Length of the resulting transcript in characters.</param>
public sealed record DictationMetrics(
    double AudioDurationMs,
    double InferenceDurationMs,
    double RealTimeFactor,
    int TranscriptLength)
{
    /// <summary>Time from hotkey release through successful text injector return; no target rendering guarantee.</summary>
    public double? ReleaseToInjectionMs { get; init; }

    /// <summary>Time from session start to the first nonempty partial, when one was received.</summary>
    public double? FirstPartialMs { get; init; }

    /// <summary>Whether recognition ran during capture and InferenceDurationMs measures only completion.</summary>
    public bool IsStreaming { get; init; }
}
