using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Murmur.App.Localization;
using Murmur.App.Views;
using Murmur.Platform.Windows.Audio;
using Murmur.Core.Settings;
using Murmur.Core.State;
using Murmur.Dictionary;
using Shouldly;
using Xunit;

namespace Murmur.App.Tests;

public sealed class HardeningAndResilienceTests : IDisposable
{
    public HardeningAndResilienceTests()
    {
        UiText.SetLanguage(UiLanguage.English);
    }

    public void Dispose()
    {
        UiText.SetLanguage(UiLanguage.English);
        GC.SuppressFinalize(this);
    }

    [AvaloniaFact]
    public void HudWindow_EmptyOrWhitespaceError_FallbacksToLocalizedErrorString()
    {
        var hud = new HudWindow();

        // Null message
        hud.UpdateState(new DictationState(DictationStatus.Error, Message: null), 0, "");
        Dispatcher.UIThread.RunJobs();
        var transcript = hud.FindControl<TextBlock>("TranscriptText");
        transcript.ShouldNotBeNull();
        transcript.Text.ShouldBe(UiText.HudError);

        // Whitespace message
        hud.UpdateState(new DictationState(DictationStatus.Error, Message: "    \t\n  "), 0, "");
        Dispatcher.UIThread.RunJobs();
        transcript.Text.ShouldBe(UiText.HudError);

        // German localization fallback
        try
        {
            UiText.SetLanguage(UiLanguage.German);
            hud.RefreshLocalizedLabels();
            hud.UpdateState(new DictationState(DictationStatus.Error, Message: ""), 0, "");
            Dispatcher.UIThread.RunJobs();
            transcript.Text.ShouldBe(UiText.HudError);
        }
        finally
        {
            UiText.SetLanguage(UiLanguage.English);
        }
    }

    [Fact]
    public void HudWindow_BuildTranscriptTail_ExtremeInputsAndEmojis()
    {
        // Null / empty / negative words
        HudWindow.BuildTranscriptTail(null, 15).ShouldBe(string.Empty);
        HudWindow.BuildTranscriptTail("", 15).ShouldBe(string.Empty);
        HudWindow.BuildTranscriptTail("   ", 15).ShouldBe(string.Empty);
        HudWindow.BuildTranscriptTail("hello world", -5).ShouldBe(string.Empty);

        // Extreme word count (1000 words)
        var manyWords = string.Join(' ', Enumerable.Range(1, 1000).Select(i => $"word{i}"));
        var tail = HudWindow.BuildTranscriptTail(manyWords, 5);
        tail.ShouldBe("… word996 word997 word998 word999 word1000");

        // Emojis and Unicode
        var emojiText = "🚀 Rocket 🌟 Star 🪐 Saturn 🎉 Party 💻 Code ☕ Coffee 🎨 Art ⚡ Lightning 🔮 Crystal 🛡️ Shield";
        var emojiTail = HudWindow.BuildTranscriptTail(emojiText, 4);
        emojiTail.ShouldStartWith("… ");
        emojiTail.ShouldContain("🛡️");

        // Single unbreakable 500-char string
        var unbreakable = new string('x', 500);
        HudWindow.BuildTranscriptTail(unbreakable, 15).ShouldBe(unbreakable);
    }

    [AvaloniaFact]
    public void SettingsWindow_ExtremeDeviceAndModelNames_TruncatesGracefully()
    {
        var window = new SettingsWindow();
        var extremeDeviceName = "USB Audio Interface Professional Studio Edition 32-Channel Direct Low-Latency Input (Super Extremely Long Name That Could Overflow Any Bounded Frame In Desktop UI)";
        var devices = new List<AudioInputDevice>
        {
            new("dev_extreme_1", extremeDeviceName, true)
        };

        window.SetDevices(devices, "dev_extreme_1");
        window.SetChoices(AppSettings.Default with { MicrophoneEndpointId = "dev_extreme_1" });
        Dispatcher.UIThread.RunJobs();

        var micText = window.FindControl<TextBlock>("MicReadinessText");
        micText.ShouldNotBeNull();
        micText.TextTrimming.ShouldBe(TextTrimming.CharacterEllipsis);

        var modelText = window.FindControl<TextBlock>("ModelReadinessText");
        modelText.ShouldNotBeNull();
        modelText.TextTrimming.ShouldBe(TextTrimming.CharacterEllipsis);

        var hotkeyText = window.FindControl<TextBlock>("HotkeyReadinessText");
        hotkeyText.ShouldNotBeNull();
        hotkeyText.TextTrimming.ShouldBe(TextTrimming.CharacterEllipsis);
    }

    [Fact]
    public void LocalizationResources_EnglishAndGerman_KeyParityAndPlaceholderMatching()
    {
        var enManager = new ResourceManager("Murmur.App.Localization.UiTextResources", typeof(UiText).Assembly);
        var enSet = enManager.GetResourceSet(CultureInfo.GetCultureInfo("en-US"), true, true);
        var deSet = enManager.GetResourceSet(CultureInfo.GetCultureInfo("de-DE"), true, true);

        enSet.ShouldNotBeNull();
        deSet.ShouldNotBeNull();

        var placeholderRegex = new Regex(@"\{(\d+)\}");

        foreach (System.Collections.DictionaryEntry entry in enSet)
        {
            var key = (string)entry.Key;
            var enValue = entry.Value?.ToString() ?? string.Empty;
            var deValue = deSet.GetString(key);

            // German translation exists for all keys
            deValue.ShouldNotBeNull($"Key '{key}' is missing in UiTextResources.de.resx");

            // Placeholders match
            var enMatches = placeholderRegex.Matches(enValue).Select(m => m.Value).OrderBy(x => x).ToList();
            var deMatches = placeholderRegex.Matches(deValue).Select(m => m.Value).OrderBy(x => x).ToList();

            deMatches.ShouldBe(enMatches, $"Placeholders in key '{key}' do not match between English ({enValue}) and German ({deValue})");
        }
    }
}
