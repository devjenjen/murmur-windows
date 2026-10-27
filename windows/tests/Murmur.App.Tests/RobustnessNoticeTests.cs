using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Murmur.App.Localization;
using Murmur.App.Views;
using Murmur.Core.State;
using Shouldly;
using Xunit;

namespace Murmur.App.Tests;

public class RobustnessNoticeTests
{
    [Theory]
    [InlineData("RecordingDurationLimit")]
    [InlineData("StreamingBacklogExceeded")]
    [InlineData("StreamingRecognitionFailed")]
    [InlineData("RecognitionRestartRequired")]
    public void SafetyNoticesResolveInBothLanguages(string key)
    {
        var method = typeof(App).GetMethod("LocalizeDictationState", BindingFlags.Static | BindingFlags.NonPublic)!;
        try
        {
            UiText.SetLanguage(UiLanguage.German);
            var german = (DictationState)method.Invoke(null, [DictationState.ErrorState(key)])!;
            german.Message.ShouldNotBe(key);
            UiText.SetLanguage(UiLanguage.English);
            var english = (DictationState)method.Invoke(null, [DictationState.ErrorState(key)])!;
            english.Message.ShouldNotBe(key);
            english.Message.ShouldNotBe(german.Message);
        }
        finally { UiText.SetLanguage(UiLanguage.English); }
    }

    [AvaloniaFact]
    public void DurationNoticeIsVisibleWhileFinishingThenInError()
    {
        var hud = new HudWindow();
        var message = UiText.Text("RecordingDurationLimit");
        try
        {
            hud.UpdateState(new DictationState(DictationStatus.Finishing, message), 0, "partial");
            Dispatcher.UIThread.RunJobs();
            hud.FindControl<TextBlock>("TranscriptText")!.Text.ShouldBe(message);
            hud.UpdateState(DictationState.ErrorState(message), 0, "final");
            Dispatcher.UIThread.RunJobs();
            hud.FindControl<TextBlock>("TranscriptText")!.Text.ShouldNotBeNull().ShouldContain(message);
        }
        finally { hud.Close(); }
    }
}
