using System.Text.Json.Serialization;
using Murmur.Core.Formatting;

namespace Murmur.Core.Settings;

/// <summary>
/// Application settings persisted to disk.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Default hotkey ID.</summary>
    public const string DefaultHotkeyId = "right-control";

    /// <summary>Default profile toggle hotkey ID.</summary>
    public const string DefaultProfileToggleHotkeyId = "f8";

    /// <summary>Default HUD placement.</summary>
    public const string DefaultHudPlacement = "bottom-center";

    /// <summary>Default speech model ID.</summary>
    public const string DefaultSpeechModelId = "parakeet-v3";

    /// <summary>Default interface language.</summary>
    public const string DefaultLanguage = "en";

    /// <summary>Default retention policy for newly created settings.</summary>
    public const int DefaultHistoryRetentionDays = 30;

    /// <summary>Default entry limit for newly created settings.</summary>
    public const int DefaultHistoryMaxEntries = 1000;

    /// <summary>Initializes a new instance of AppSettings.</summary>
    /// <param name="microphoneEndpointId">The selected microphone endpoint ID, or null for default.</param>
    /// <param name="startWithWindows">Whether to start the application with Windows.</param>
    /// <param name="setupCompleted">Whether initial setup has completed.</param>
    /// <param name="removeFillers">Whether filler words are removed.</param>
    public AppSettings(string? microphoneEndpointId, bool startWithWindows, bool setupCompleted, bool removeFillers = true)
    {
        MicrophoneEndpointId = microphoneEndpointId;
        StartWithWindows = startWithWindows;
        SetupCompleted = setupCompleted;
        RemoveFillers = removeFillers;
    }

    /// <summary>The selected microphone endpoint ID, or null for default.</summary>
    public string? MicrophoneEndpointId { get; init; }

    /// <summary>Whether to start the application with Windows.</summary>
    public bool StartWithWindows { get; init; }

    /// <summary>Whether initial setup has completed.</summary>
    public bool SetupCompleted { get; init; }

    /// <summary>The configured push-to-talk hotkey identifier.</summary>
    public string HotkeyId { get; init; } = DefaultHotkeyId;

    /// <summary>The configured quick-toggle hotkey identifier for switching profiles.</summary>
    public string ProfileToggleHotkeyId { get; init; } = DefaultProfileToggleHotkeyId;

    /// <summary>The configured HUD screen placement.</summary>
    public string HudPlacement { get; init; } = DefaultHudPlacement;

    /// <summary>The configured speech model identifier.</summary>
    public string SpeechModelId { get; init; } = DefaultSpeechModelId;

    /// <summary>Persisted interface language code.</summary>
    public string Language { get; init; } = DefaultLanguage;

    /// <summary>Whether filler words are removed.</summary>
    public bool RemoveFillers { get; init; } = true;

    /// <summary>Whether conversational self-corrections are repaired in speech output.</summary>
    public bool EnableSelfCorrection { get; init; } = true;

    /// <summary>Whether a short sound plays after successful text injection.</summary>
    public bool AudioFeedbackEnabled { get; init; }

    /// <summary>Where completed dictation text is inserted.</summary>
    public TextInsertionMode TextInsertionMode { get; init; } = TextInsertionMode.AtCaret;

    /// <summary>Whether to restore focus to the previously active window after injection.</summary>
    public bool RestorePreviousWindowAfterInjection { get; set; }

    /// <summary>Whether completed dictations are persisted in local history.</summary>
    public bool HistoryEnabled { get; init; }

    /// <summary>Maximum age of new history entries in days; zero means unlimited.</summary>
    public int HistoryRetentionDays { get; init; } = DefaultHistoryRetentionDays;

    /// <summary>Maximum number of retained history entries; zero means unlimited.</summary>
    public int HistoryMaxEntries { get; init; } = DefaultHistoryMaxEntries;

    /// <summary>Available dictation profiles.</summary>
    public IReadOnlyList<DictationProfile> Profiles { get; init; } = [];

    /// <summary>The currently active profile identifier.</summary>
    public string ActiveProfileId { get; init; } = "standard";

    /// <summary>Gets the currently active dictation profile.</summary>
    [JsonIgnore]
    public DictationProfile ActiveProfile
    {
        get
        {
            if (Profiles is { Count: > 0 })
            {
                for (var i = 0; i < Profiles.Count; i++)
                {
                    if (string.Equals(Profiles[i].Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase))
                    {
                        return Profiles[i];
                    }
                }
                return Profiles[0];
            }
            return new DictationProfile();
        }
    }

    /// <summary>Gets the default application settings.</summary>
    public static AppSettings Default { get; } = new AppSettings(null, false, false).Normalize();

    /// <summary>
    /// Returns a normalized copy of these settings with defaults and profile structure guaranteed.
    /// </summary>
    public AppSettings Normalize()
    {
        var normalizedHotkeyId = string.IsNullOrWhiteSpace(HotkeyId) ? DefaultHotkeyId : HotkeyId;
        var normalizedHud = string.IsNullOrWhiteSpace(HudPlacement) ? DefaultHudPlacement : HudPlacement;
        var normalizedModel = string.IsNullOrWhiteSpace(SpeechModelId) ? DefaultSpeechModelId : SpeechModelId;
        var normalizedLanguage = string.Equals(Language, "de", StringComparison.OrdinalIgnoreCase) ? "de" : DefaultLanguage;

        // Resolve toggle hotkey collision with push-to-talk
        var toggleCandidate = string.IsNullOrWhiteSpace(ProfileToggleHotkeyId) ? DefaultProfileToggleHotkeyId : ProfileToggleHotkeyId;
        var fKeys = new[] { "f8", "f9", "f10", "f11", "f12" };
        if (string.Equals(toggleCandidate, normalizedHotkeyId, StringComparison.OrdinalIgnoreCase))
        {
            toggleCandidate = fKeys.FirstOrDefault(k => !string.Equals(k, normalizedHotkeyId, StringComparison.OrdinalIgnoreCase)) ?? "f9";
        }

        // Build/normalize profiles
        DictationProfile? existingStandard = null;
        DictationProfile? existingMath = null;
        if (Profiles != null)
        {
            for (var i = 0; i < Profiles.Count; i++)
            {
                var p = Profiles[i];
                if (string.Equals(p.Id, "standard", StringComparison.OrdinalIgnoreCase) && existingStandard is null)
                {
                    existingStandard = p with { Id = "standard", FormatterKind = ProfileFormatterKind.Standard };
                }
                else if (string.Equals(p.Id, "math", StringComparison.OrdinalIgnoreCase) && existingMath is null)
                {
                    existingMath = p with { Id = "math", FormatterKind = ProfileFormatterKind.Math };
                }
            }
        }

        var standard = existingStandard ?? new DictationProfile
        {
            Id = "standard",
            Name = "Standard",
            FormatterKind = ProfileFormatterKind.Standard,
            RemoveFillers = RemoveFillers
        };

        var math = existingMath ?? new DictationProfile
        {
            Id = "math",
            Name = "Mathe",
            FormatterKind = ProfileFormatterKind.Math,
            RemoveFillers = RemoveFillers,
            MathMode = MathFormatMode.Unicode
        };

        var normalizedProfiles = new[] { standard, math };

        var normalizedActiveId = string.Equals(ActiveProfileId, "math", StringComparison.OrdinalIgnoreCase)
            ? "math"
            : "standard";

        return this with
        {
            HotkeyId = normalizedHotkeyId,
            HudPlacement = normalizedHud,
            SpeechModelId = normalizedModel,
            Language = normalizedLanguage,
            TextInsertionMode = NormalizeTextInsertionMode(TextInsertionMode),
            HistoryRetentionDays = NormalizeHistoryRetentionDays(HistoryRetentionDays),
            HistoryMaxEntries = NormalizeHistoryMaxEntries(HistoryMaxEntries),
            ProfileToggleHotkeyId = toggleCandidate,
            Profiles = normalizedProfiles,
            ActiveProfileId = normalizedActiveId
        };
    }

    /// <summary>Determines whether this instance equals another AppSettings instance.</summary>
    public bool Equals(AppSettings? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return string.Equals(MicrophoneEndpointId, other.MicrophoneEndpointId, StringComparison.Ordinal)
            && StartWithWindows == other.StartWithWindows
            && SetupCompleted == other.SetupCompleted
            && string.Equals(HotkeyId, other.HotkeyId, StringComparison.Ordinal)
            && string.Equals(ProfileToggleHotkeyId, other.ProfileToggleHotkeyId, StringComparison.Ordinal)
            && string.Equals(HudPlacement, other.HudPlacement, StringComparison.Ordinal)
            && string.Equals(SpeechModelId, other.SpeechModelId, StringComparison.Ordinal)
            && string.Equals(Language, other.Language, StringComparison.Ordinal)
            && RemoveFillers == other.RemoveFillers
            && EnableSelfCorrection == other.EnableSelfCorrection
            && AudioFeedbackEnabled == other.AudioFeedbackEnabled
            && TextInsertionMode == other.TextInsertionMode
            && HistoryEnabled == other.HistoryEnabled
            && HistoryRetentionDays == other.HistoryRetentionDays
            && HistoryMaxEntries == other.HistoryMaxEntries
            && RestorePreviousWindowAfterInjection == other.RestorePreviousWindowAfterInjection
            && string.Equals(ActiveProfileId, other.ActiveProfileId, StringComparison.Ordinal)
            && Profiles.SequenceEqual(other.Profiles);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(MicrophoneEndpointId);
        hash.Add(StartWithWindows);
        hash.Add(SetupCompleted);
        hash.Add(HotkeyId);
        hash.Add(ProfileToggleHotkeyId);
        hash.Add(HudPlacement);
        hash.Add(SpeechModelId);
        hash.Add(Language);
        hash.Add(RemoveFillers);
        hash.Add(EnableSelfCorrection);
        hash.Add(AudioFeedbackEnabled);
        hash.Add(TextInsertionMode);
        hash.Add(HistoryEnabled);
        hash.Add(HistoryRetentionDays);
        hash.Add(HistoryMaxEntries);
        hash.Add(RestorePreviousWindowAfterInjection);
        hash.Add(ActiveProfileId);
        for (var i = 0; i < Profiles.Count; i++)
        {
            hash.Add(Profiles[i]);
        }
        return hash.ToHashCode();
    }

    private static TextInsertionMode NormalizeTextInsertionMode(TextInsertionMode mode) =>
        mode is TextInsertionMode.AtCaret or TextInsertionMode.DocumentEnd
            ? mode
            : TextInsertionMode.DocumentEnd;

    private static int NormalizeHistoryRetentionDays(int days) =>
        days is 0 or 7 or 30 or 90 ? days : DefaultHistoryRetentionDays;

    private static int NormalizeHistoryMaxEntries(int maxEntries) =>
        maxEntries is 0 or 1000 ? maxEntries : DefaultHistoryMaxEntries;
}
