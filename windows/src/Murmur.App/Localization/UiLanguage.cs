namespace Murmur.App.Localization;

/// <summary>Supported Murmur interface languages.</summary>
public static class UiLanguage
{
    /// <summary>English language code.</summary>
    public const string English = "en";

    /// <summary>German language code.</summary>
    public const string German = "de";

    /// <summary>Normalizes a persisted language value to a supported code.</summary>
    public static string Normalize(string? language) =>
        string.Equals(language, German, StringComparison.OrdinalIgnoreCase)
            ? German
            : English;
}
