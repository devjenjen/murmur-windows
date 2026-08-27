using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Murmur.App.Localization;
using Murmur.App.Views;
using Murmur.Core.Settings;
using Murmur.Core.State;
using Murmur.Speech;
using Shouldly;
using Xunit;

namespace Murmur.App.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void LanguageSwitch_UsesContextualGermanAndEnglishText()
    {
        UiText.SetLanguage(UiLanguage.German);
        UiText.TraySettings.ShouldBe("Einstellungen...");
        UiText.HudListening.ShouldBe("Hört zu…");

        UiText.SetLanguage(UiLanguage.English);
        UiText.TraySettings.ShouldBe("Settings...");
        UiText.HudListening.ShouldBe("Listening…");
    }

    [Fact]
    public void Resources_Localize_Hud_Badges_And_Use_LicenseNeutral_Setup_Note()
    {
        UiText.SetLanguage(UiLanguage.German);
        UiText.Text("HudRecording").ShouldBe("AUF");
        UiText.Text("HudErrorBadge").ShouldBe("FEHL");
        UiText.SetupNote.ShouldBe("Ausgewählte Taste gedrückt halten, sprechen und zum Einfügen loslassen. Parakeet- und Nemotron-Modelle von NVIDIA.");

        UiText.SetLanguage(UiLanguage.English);
        UiText.Text("HudRecording").ShouldBe("REC");
        UiText.Text("HudErrorBadge").ShouldBe("ERR");
        UiText.SetupNote.ShouldBe("Hold selected key to speak; release to type. Parakeet and Nemotron models by NVIDIA.");
    }

    [AvaloniaFact]
    public void German_Settings_Localize_Default_Hotkey_Without_Changing_Model_Names()
    {
        UiText.SetLanguage(UiLanguage.German);
        var window = new SettingsWindow();
        window.SetChoices(AppSettings.Default with { Language = UiLanguage.German });
        Dispatcher.UIThread.RunJobs();

        window.FindControl<TextBox>("HotkeyTextBox")!.Text.ShouldBe("rechte Strg-Taste");
        window.FindControl<ComboBox>("SpeechModelComboBox")!.ItemsSource.ShouldBe(
            ModelManifest.All.Select(model => model.DisplayName).ToList());

        UiText.SetLanguage(UiLanguage.English);
    }

    [AvaloniaFact]
    public void German_Hud_Uses_Localized_Recording_And_Error_Badges()
    {
        UiText.SetLanguage(UiLanguage.German);
        var hud = new HudWindow();

        hud.UpdateState(new DictationState(DictationStatus.Listening), 0, string.Empty);
        Dispatcher.UIThread.RunJobs();
        hud.FindControl<TextBlock>("TelemetryBadge")!.Text.ShouldBe("AUF");

        hud.UpdateState(new DictationState(DictationStatus.Error), 0, string.Empty);
        Dispatcher.UIThread.RunJobs();
        hud.FindControl<TextBlock>("TelemetryBadge")!.Text.ShouldBe("FEHL");

        UiText.SetLanguage(UiLanguage.English);
    }

    [Fact]
    public void InterfaceLanguage_And_LensTokens_ResolveInEnglishAndGerman()
    {
        UiText.SetLanguage(UiLanguage.German);
        UiText.InterfaceLanguage.ShouldBe("Oberflächensprache");
        UiText.SettingsTabs.ShouldBe("Einstellungsbereiche");
        UiText.SystemStatus.ShouldBe("Systemstatus");

        UiText.SetLanguage(UiLanguage.English);
        UiText.InterfaceLanguage.ShouldBe("Interface language");
        UiText.SettingsTabs.ShouldBe("Settings sections");
        UiText.SystemStatus.ShouldBe("System status");
    }
}
