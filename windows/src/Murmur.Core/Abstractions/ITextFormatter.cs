namespace Murmur.Core.Abstractions;

/// <summary>
/// Formats raw transcription text.
/// </summary>
public interface ITextFormatter
{
    /// <summary>Cleans up raw speech text.</summary>
    string Format(string raw);
}
