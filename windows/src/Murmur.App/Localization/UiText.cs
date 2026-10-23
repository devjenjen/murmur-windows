using System.Globalization;
using System.Resources;

namespace Murmur.App.Localization;

#pragma warning disable CS1591

/// <summary>Localized visible text used by the Murmur application.</summary>
public static class UiText
{
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly ResourceManager Resources =
        new("Murmur.App.Localization.UiTextResources", typeof(UiText).Assembly);
    private static CultureInfo _culture = EnglishCulture;

    public static string Language => Get(nameof(Language));
    public static string InterfaceLanguage => Get(nameof(InterfaceLanguage));
    public static string English => Get(nameof(English));
    public static string German => Get(nameof(German));

    public static string TrayReady => Get(nameof(TrayReady));
    public static string TraySettings => Get(nameof(TraySettings));
    public static string TrayOpenLogFolder => Get(nameof(TrayOpenLogFolder));
    public static string TrayUpdateAvailable => Get(nameof(TrayUpdateAvailable));
    public static string TrayCheckForUpdates => Get(nameof(TrayCheckForUpdates));
    public static string TrayStartWithWindows => Get(nameof(TrayStartWithWindows));
    public static string TrayExit => Get(nameof(TrayExit));

    public static string HudArm => Get(nameof(HudArm));
    public static string HudProc => Get(nameof(HudProc));
    public static string HudHoldToSpeak => Get(nameof(HudHoldToSpeak));
    public static string HudListening => Get(nameof(HudListening));
    public static string HudTranscribing => Get(nameof(HudTranscribing));
    public static string HudRecordingLevel => Get(nameof(HudRecordingLevel));
    public static string HudError => Get(nameof(HudError));
    public static string TextTargetUnavailable => Get(nameof(TextTargetUnavailable));
    public static string TextTargetChanged => Get(nameof(TextTargetChanged));
    public static string TextModifierHeld => Get(nameof(TextModifierHeld));

    public static string SettingsTitle => Get(nameof(SettingsTitle));
    public static string SettingsTabs => Get(nameof(SettingsTabs));
    public static string SetupTab => Get(nameof(SetupTab));
    public static string SystemReadiness => Get(nameof(SystemReadiness));
    public static string SetupIncomplete => Get(nameof(SetupIncomplete));
    public static string Monitor => Get(nameof(Monitor));
    public static string MicChecking => Get(nameof(MicChecking));
    public static string ModelChecking => Get(nameof(ModelChecking));
    public static string HotkeyRightCtrl => Get(nameof(HotkeyRightCtrl));
    public static string RequiredSignalPath => Get(nameof(RequiredSignalPath));
    public static string Microphone => Get(nameof(Microphone));
    public static string Refresh => Get(nameof(Refresh));
    public static string RefreshMicrophone => Get(nameof(RefreshMicrophone));
    public static string Test => Get(nameof(Test));
    public static string TestMicrophone => Get(nameof(TestMicrophone));
    public static string MicrophoneLevel => Get(nameof(MicrophoneLevel));
    public static string SpeechRecognitionModel => Get(nameof(SpeechRecognitionModel));
    public static string SpeechRecognitionModelAutomation => Get(nameof(SpeechRecognitionModelAutomation));
    public static string ModelStatus => Get(nameof(ModelStatus));
    public static string Download => Get(nameof(Download));
    public static string DownloadModel => Get(nameof(DownloadModel));
    public static string Cancel => Get(nameof(Cancel));
    public static string CancelDownload => Get(nameof(CancelDownload));
    public static string ModelProgress => Get(nameof(ModelProgress));
    public static string PushToTalkKey => Get(nameof(PushToTalkKey));
    public static string PushToTalkKeyAutomation => Get(nameof(PushToTalkKeyAutomation));
    public static string Change => Get(nameof(Change));
    public static string ChangePushToTalk => Get(nameof(ChangePushToTalk));
    public static string KeyNote => Get(nameof(KeyNote));
    public static string Preferences => Get(nameof(Preferences));
    public static string HudScreenPlacement => Get(nameof(HudScreenPlacement));
    public static string HudScreenPosition => Get(nameof(HudScreenPosition));
    public static string TextInsertionMode => Get(nameof(TextInsertionMode));
    public static string AtCurrentCursor => Get(nameof(AtCurrentCursor));
    public static string AtEndOfDocument => Get(nameof(AtEndOfDocument));
    public static string TextInsertionModeNote => Get(nameof(TextInsertionModeNote));
    public static string HistoryEnabled => Get(nameof(HistoryEnabled));
    public static string HistoryRetention => Get(nameof(HistoryRetention));
    public static string HistoryMaxEntries => Get(nameof(HistoryMaxEntries));
    public static string HistoryRetentionNote => Get(nameof(HistoryRetentionNote));
    public static string HistoryRetentionUnlimited => Get(nameof(HistoryRetentionUnlimited));
    public static string HistoryRetention7Days => Get(nameof(HistoryRetention7Days));
    public static string HistoryRetention30Days => Get(nameof(HistoryRetention30Days));
    public static string HistoryRetention90Days => Get(nameof(HistoryRetention90Days));
    public static string HistoryMaxEntriesUnlimited => Get(nameof(HistoryMaxEntriesUnlimited));
    public static string HistoryMaxEntries1000 => Get(nameof(HistoryMaxEntries1000));
    public static string RemoveFillers => Get(nameof(RemoveFillers));
    public static string RemoveFillersAutomation => Get(nameof(RemoveFillersAutomation));
    public static string EnableSelfCorrection => Get(nameof(EnableSelfCorrection));
    public static string RestorePreviousWindowAfterInjection => Get(nameof(RestorePreviousWindowAfterInjection));
    public static string AudioFeedback => Get(nameof(AudioFeedback));
    public static string AudioFeedbackAutomation => Get(nameof(AudioFeedbackAutomation));
    public static string StartOnLogin => Get(nameof(StartOnLogin));
    public static string StartOnLoginAutomation => Get(nameof(StartOnLoginAutomation));
    public static string SetupNote => Get(nameof(SetupNote));
    public static string StartMurmur => Get(nameof(StartMurmur));
    public static string Apply => Get(nameof(Apply));
    public static string SaveSettings => Get(nameof(SaveSettings));
    public static string LanguageAutomation => Get(nameof(LanguageAutomation));

    public static string Profile => Get(nameof(Profile));
    public static string ProfileStandard => Get(nameof(ProfileStandard));
    public static string ProfileMath => Get(nameof(ProfileMath));
    public static string ProfileBadgeStandard => Get(nameof(ProfileBadgeStandard));
    public static string ProfileBadgeMath => Get(nameof(ProfileBadgeMath));
    public static string ProfileDescriptionStandard => Get(nameof(ProfileDescriptionStandard));
    public static string ProfileDescriptionMath => Get(nameof(ProfileDescriptionMath));
    public static string MathOutputMode => Get(nameof(MathOutputMode));
    public static string MathOutputUnicode => Get(nameof(MathOutputUnicode));
    public static string MathOutputLatex => Get(nameof(MathOutputLatex));

    public static string Text(string key) => Get(key);

    public static string HistoryTab => Get(nameof(HistoryTab));
    public static string SearchTranscriptions => Get(nameof(SearchTranscriptions));
    public static string SearchTranscriptionsAutomation => Get(nameof(SearchTranscriptionsAutomation));
    public static string NoDictations => Get(nameof(NoDictations));
    public static string DictationHistory => Get(nameof(DictationHistory));
    public static string AudioDurationFormat => Get(nameof(AudioDurationFormat));
    public static string InferenceDurationFormat => Get(nameof(InferenceDurationFormat));
    public static string Copy => Get(nameof(Copy));
    public static string CopyTranscription => Get(nameof(CopyTranscription));
    public static string CopyTranscriptionTip => Get(nameof(CopyTranscriptionTip));
    public static string RecoveryTranscriptTitle => Get(nameof(RecoveryTranscriptTitle));
    public static string RecoveryTranscriptNote => Get(nameof(RecoveryTranscriptNote));
    public static string CopyRecoveryTranscript => Get(nameof(CopyRecoveryTranscript));
    public static string DiscardRecoveryTranscript => Get(nameof(DiscardRecoveryTranscript));
    public static string Delete => Get(nameof(Delete));
    public static string DeleteTranscription => Get(nameof(DeleteTranscription));
    public static string DeleteTranscriptionTip => Get(nameof(DeleteTranscriptionTip));
    public static string DeleteEntryConfirm => Get(nameof(DeleteEntryConfirm));
    public static string CancelDeleteEntry => Get(nameof(CancelDeleteEntry));
    public static string ConfirmDeleteEntry => Get(nameof(ConfirmDeleteEntry));
    public static string ZeroEntries => Get(nameof(ZeroEntries));
    public static string ClearAllHistory => Get(nameof(ClearAllHistory));
    public static string DeleteAllHistory => Get(nameof(DeleteAllHistory));
    public static string ClearHistoryConfirm => Get(nameof(ClearHistoryConfirm));
    public static string CancelDeleteHistory => Get(nameof(CancelDeleteHistory));
    public static string ConfirmDeleteHistory => Get(nameof(ConfirmDeleteHistory));

    public static string DictionaryTab => Get(nameof(DictionaryTab));
    public static string Corrections => Get(nameof(Corrections));
    public static string DictionaryDescription => Get(nameof(DictionaryDescription));
    public static string Add => Get(nameof(Add));
    public static string AddDictionary => Get(nameof(AddDictionary));
    public static string SearchDictionary => Get(nameof(SearchDictionary));
    public static string SearchDictionaryAutomation => Get(nameof(SearchDictionaryAutomation));
    public static string AddCorrection => Get(nameof(AddCorrection));
    public static string EditDictionaryEntry => Get(nameof(EditDictionaryEntry));
    public static string DictionaryEntryType => Get(nameof(DictionaryEntryType));
    public static string Term => Get(nameof(Term));
    public static string Correction => Get(nameof(Correction));
    public static string MisheardPhrase => Get(nameof(MisheardPhrase));
    public static string MisheardPhraseAutomation => Get(nameof(MisheardPhraseAutomation));
    public static string CorrectedPhrase => Get(nameof(CorrectedPhrase));
    public static string CorrectedPhraseAutomation => Get(nameof(CorrectedPhraseAutomation));
    public static string Write => Get(nameof(Write));
    public static string Save => Get(nameof(Save));
    public static string DictionaryEmpty => Get(nameof(DictionaryEmpty));
    public static string DictionaryEntries => Get(nameof(DictionaryEntries));
    public static string Edit => Get(nameof(Edit));
    public static string Toggle => Get(nameof(Toggle));
    public static string DictionaryPath => Get(nameof(DictionaryPath));
    public static string SystemStatus => Get(nameof(SystemStatus));
    public static string CloseWindow => Get(nameof(CloseWindow));
    public static string MinimizeWindow => Get(nameof(MinimizeWindow));
    public static string MaximizeWindow => Get(nameof(MaximizeWindow));
    public static string RestoreWindow => Get(nameof(RestoreWindow));
    public static string HistoryEmptyInstruction => Get(nameof(HistoryEmptyInstruction));
    public static string HistoryStoredLocally => Get(nameof(HistoryStoredLocally));
    public static string HistoryAudioDuration => Get(nameof(HistoryAudioDuration));
    public static string HistoryInferenceDuration => Get(nameof(HistoryInferenceDuration));
    public static string DictionaryTermExample => Get(nameof(DictionaryTermExample));
    public static string DictionaryCorrectionExample => Get(nameof(DictionaryCorrectionExample));
    public static string Confirm => Get(nameof(Confirm));

    public static void SetLanguage(string? language)
    {
        _culture = UiLanguage.Normalize(language) == UiLanguage.German
            ? CultureInfo.GetCultureInfo("de-DE")
            : EnglishCulture;
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(_culture, Get(key), args);

    private static string Get(string key) =>
        Resources.GetString(key, _culture)
        ?? Resources.GetString(key, EnglishCulture)
        ?? key;
}

#pragma warning restore CS1591
