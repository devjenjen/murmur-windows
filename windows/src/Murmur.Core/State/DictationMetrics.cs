namespace Murmur.Core.State;

/// <summary>
/// Dictation timing and performance metrics.
/// </summary>
/// <param name="AudioDurationMs">Audio duration in milliseconds.</param>
/// <param name="InferenceDurationMs">Inference duration in milliseconds.</param>
/// <param name="RealTimeFactor">Real-time factor (inference duration / audio duration).</param>
/// <param name="TranscriptLength">Length of the resulting transcript in characters.</param>
public sealed record DictationMetrics(
    double AudioDurationMs,
    double InferenceDurationMs,
    double RealTimeFactor,
    int TranscriptLength);
