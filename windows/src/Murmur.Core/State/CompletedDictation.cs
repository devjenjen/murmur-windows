namespace Murmur.Core.State;

/// <summary>A final transcript ready for history persistence and injection.</summary>
/// <param name="Timestamp">Completion timestamp with local offset.</param>
/// <param name="Engine">Speech engine display name.</param>
/// <param name="AudioDurationMs">Captured audio duration in milliseconds.</param>
/// <param name="InferenceDurationMs">Recognition duration in milliseconds.</param>
/// <param name="Text">Final formatted and dictionary-corrected text.</param>
public sealed record CompletedDictation(
    DateTimeOffset Timestamp,
    string Engine,
    double AudioDurationMs,
    double InferenceDurationMs,
    string Text);
