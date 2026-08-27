namespace Murmur.Core.History;

/// <summary>A completed dictation retained in local history.</summary>
/// <param name="Id">Stable entry identifier.</param>
/// <param name="Timestamp">Completion timestamp with offset.</param>
/// <param name="Engine">Speech engine display name.</param>
/// <param name="AudioDurationMs">Captured audio duration in milliseconds.</param>
/// <param name="InferenceDurationMs">Recognition duration in milliseconds.</param>
/// <param name="Text">Final formatted and corrected transcript.</param>
public sealed record DictationHistoryEntry(
    Guid Id,
    DateTimeOffset Timestamp,
    string Engine,
    double AudioDurationMs,
    double InferenceDurationMs,
    string Text);
