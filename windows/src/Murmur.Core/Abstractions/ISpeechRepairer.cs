namespace Murmur.Core.Abstractions;

/// <summary>
/// Repairs conversational disfluencies and spoken self-corrections in transcribed speech.
/// </summary>
public interface ISpeechRepairer
{
    /// <summary>
    /// Processes raw speech transcript to eliminate conversational self-corrections and repair speech errors.
    /// </summary>
    /// <param name="rawTranscript">The raw transcript output from speech recognition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The repaired transcript.</returns>
    Task<string> RepairAsync(string rawTranscript, CancellationToken cancellationToken = default);
}
