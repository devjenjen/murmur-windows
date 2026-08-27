using Murmur.Core.Formatting;

namespace Murmur.Core.Settings;

/// <summary>
/// Supported formatter types for dictation profiles.
/// </summary>
public enum ProfileFormatterKind
{
    /// <summary>General prose rule-based formatting.</summary>
    Standard,

    /// <summary>Mathematical and scientific rule-based formatting.</summary>
    Math
}

/// <summary>
/// Configuration profile for dictation formatting.
/// </summary>
public sealed record DictationProfile
{
    /// <summary>Unique identifier for the profile.</summary>
    public string Id { get; init; } = "standard";

    /// <summary>Human-readable profile name.</summary>
    public string Name { get; init; } = "Standard";

    /// <summary>The formatter kind used by this profile.</summary>
    public ProfileFormatterKind FormatterKind { get; init; } = ProfileFormatterKind.Standard;

    /// <summary>Whether filler words are stripped.</summary>
    public bool RemoveFillers { get; init; } = true;

    /// <summary>Formula formatting mode (Unicode or LaTeX) when FormatterKind is Math.</summary>
    public MathFormatMode MathMode { get; init; } = MathFormatMode.Unicode;
}
