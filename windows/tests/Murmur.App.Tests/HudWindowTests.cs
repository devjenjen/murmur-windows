using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Murmur.App.Localization;
using Murmur.App.Views;
using Murmur.Core.Settings;
using Murmur.Core.State;
using Shouldly;
using Xunit;

namespace Murmur.App.Tests;

public class HudWindowTests
{
    public HudWindowTests() => UiText.SetLanguage(UiLanguage.English);

    [AvaloniaFact]
    public void HudWindow_SizesToContentAndHasNoDivider()
    {
        var hud = new HudWindow();
        var transcript = hud.FindControl<TextBlock>("TranscriptText");

        hud.SizeToContent.ShouldBe(SizeToContent.WidthAndHeight);
        transcript.ShouldNotBeNull();
        transcript.MaxWidth.ShouldBe(288);
        hud.GetVisualDescendants().OfType<Rectangle>().ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void HudTokens_ResolveOrbAndMotionTokens()
    {
        var hud = new HudWindow();

        hud.TryFindResource("HudViewportWidth", out var viewportWidth).ShouldBeTrue();
        ((double)viewportWidth!).ShouldBe(488);

        hud.TryFindResource("HudOrbDiameter", out var orbDiameter).ShouldBeTrue();
        ((double)orbDiameter!).ShouldBe(52);

        hud.TryFindResource("HudOrbRadius", out var orbRadius).ShouldBeTrue();
        ((CornerRadius)orbRadius!).TopLeft.ShouldBe(26);

        hud.TryFindResource("HudOrbScaleStart", out var scale).ShouldBeTrue();
        ((double)scale!).ShouldBe(0.92);
        hud.TryFindResource("HudContentRevealDelayMs", out var delay).ShouldBeTrue();
        ((double)delay!).ShouldBe(60);
        hud.TryFindResource("HudExitDurationMs", out var exit).ShouldBeTrue();
        ((double)exit!).ShouldBe(260);
        var contentBackground = hud.FindControl<Border>("HudContentSurface")!.Background.ShouldBeAssignableTo<ISolidColorBrush>();
        contentBackground!.Color.A.ShouldBe((byte)0);
    }

    [AvaloniaFact]
    public void HudTokens_PreservesRequiredHudResources()
    {
        var hud = new HudWindow();
        var theme = hud.ActualThemeVariant;

        string[] requiredResources =
        [
            "HudViewportWidth",
            "HudViewportHeight",
            "HudArmedWidth",
            "HudArmedHeight",
            "HudPadding",
            "HudMeterWidth",
            "HudMeterHeight",
            "HudOrbDiameter",
            "HudOrbRadius",
            "HudBorderThickness",
            "HudColumnSpacing",
            "HudLampBadgeSpacing",
            "HudProcessingDotSize",
            "HudProcessingDotSpacing",
            "HudTranscriptMaxWidth",
            "HudChassisBrush",
            "HudHairlineBrush",
            "HudIdleLampBrush",
            "HudProcessingBrush",
            "HudMeterPeakBrush",
            "InkPrimaryBrush",
            "LampDiameter"
        ];

        foreach (var key in requiredResources)
        {
            var found = hud.TryFindResource(key, theme, out var res) || hud.TryFindResource(key, out res);
            found.ShouldBeTrue($"Resource '{key}' should be resolvable");
            res.ShouldNotBeNull($"Resource '{key}' should not be null");
        }
    }

    [AvaloniaFact]
    public void HudWindow_ResolvesSolidFallbackBrush()
    {
        var hud = new HudWindow();

        hud.TryFindResource("HudSolidFallbackBrush", hud.ActualThemeVariant, out var fallbackBrush).ShouldBeTrue();
        fallbackBrush.ShouldNotBeNull();
        (fallbackBrush is IBrush).ShouldBeTrue();
    }

    [AvaloniaFact]
    public void HudWindow_UsesModerateAcrylicMaterial()
    {
        var hud = new HudWindow();

        hud.TransparencyLevelHint.ShouldContain(WindowTransparencyLevel.AcrylicBlur);
        hud.TryFindResource("HudChassisBrush", hud.ActualThemeVariant, out var chassisBrush).ShouldBeTrue();
        var solid = chassisBrush.ShouldBeOfType<SolidColorBrush>();
        solid.Color.A.ShouldBe((byte)0x55);
    }

    [AvaloniaFact]
    public void HudWindow_UsesLensMeter()
    {
        var hud = new HudWindow();
        var meter = hud.FindControl<Murmur.App.Controls.VUMeterControl>("HudMeter");

        meter.ShouldNotBeNull();
        meter.Width.ShouldBe(80);
        meter.Height.ShouldBe(18);
    }

    [AvaloniaFact]
    public void HudWindow_StartingState_ShowsArmBadgeAndHoldToSpeak()
    {
        var hud = new HudWindow();
        hud.UpdateState(new DictationState(DictationStatus.Starting), 0.0f, "");
        Dispatcher.UIThread.RunJobs();

        var badge = hud.FindControl<TextBlock>("TelemetryBadge");
        badge.ShouldNotBeNull();
        badge.Text.ShouldBe("ARM");

        var transcript = hud.FindControl<TextBlock>("TranscriptText");
        transcript.ShouldNotBeNull();
        transcript.Text.ShouldBe("Hold to speak");
        AutomationProperties.GetLiveSetting(transcript).ShouldBe(AutomationLiveSetting.Polite);

        var lamp = hud.FindControl<Ellipse>("RecordLamp");
        lamp.ShouldNotBeNull();
        var lampBrush = lamp.Fill as ISolidColorBrush;
        lampBrush.ShouldNotBeNull();
        (lampBrush.Color.R > 200 && lampBrush.Color.G < 100).ShouldBeFalse();

        var meter = hud.FindControl<Murmur.App.Controls.VUMeterControl>("HudMeter");
        meter.ShouldNotBeNull();
        meter.IsActive.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void HudWindow_ListeningState_ShowsRedLampAndPoliteLiveSetting()
    {
        var hud = new HudWindow();
        hud.UpdateState(new DictationState(DictationStatus.Listening), 0.5f, "Hello dictation");
        Dispatcher.UIThread.RunJobs();

        var lamp = hud.FindControl<Ellipse>("RecordLamp");
        lamp.ShouldNotBeNull();
        var fillBrush = lamp.Fill as ISolidColorBrush;
        fillBrush.ShouldNotBeNull();
        // Red hue
        fillBrush.Color.R.ShouldBeGreaterThan((byte)150);

        var transcript = hud.FindControl<TextBlock>("TranscriptText");
        transcript.ShouldNotBeNull();
        transcript.Text.ShouldBe("Hello dictation");
        AutomationProperties.GetLiveSetting(transcript).ShouldBe(AutomationLiveSetting.Polite);

        var badge = hud.FindControl<TextBlock>("TelemetryBadge");
        badge.ShouldNotBeNull();
        badge.Text.ShouldBe("REC");

        var meter = hud.FindControl<Murmur.App.Controls.VUMeterControl>("HudMeter");
        meter.ShouldNotBeNull();
        meter.IsActive.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void HudWindow_FinishingAndError_DoNotUseRedLamp()
    {
        var hud = new HudWindow();

        // Finishing
        hud.UpdateState(new DictationState(DictationStatus.Finishing), 0.0f, "");
        Dispatcher.UIThread.RunJobs();

        var lamp = hud.FindControl<Ellipse>("RecordLamp");
        lamp.ShouldNotBeNull();
        var finishingBrush = lamp.Fill as ISolidColorBrush;
        finishingBrush.ShouldNotBeNull();
        // Not active red
        (finishingBrush.Color.R > 200 && finishingBrush.Color.G < 100).ShouldBeFalse();

        var finishingBadge = hud.FindControl<TextBlock>("TelemetryBadge");
        finishingBadge.ShouldNotBeNull();
        finishingBadge.Text.ShouldBe("PROC");

        var transcript = hud.FindControl<TextBlock>("TranscriptText");
        transcript.ShouldNotBeNull();
        transcript.Text.ShouldBe("Transcribing…");

        var meter = hud.FindControl<Murmur.App.Controls.VUMeterControl>("HudMeter");
        meter.ShouldNotBeNull();
        meter.IsActive.ShouldBeFalse();

        // Error
        hud.UpdateState(new DictationState(DictationStatus.Error, "Audio device lost"), 0.0f, "");
        Dispatcher.UIThread.RunJobs();

        var errorBadge = hud.FindControl<TextBlock>("TelemetryBadge");
        errorBadge.ShouldNotBeNull();
        errorBadge.Text.ShouldBe("ERR");

        transcript.Text.ShouldBe("Audio device lost");
        AutomationProperties.GetLiveSetting(transcript).ShouldBe(AutomationLiveSetting.Assertive);
        meter.IsActive.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void HudWindow_RapidStates_RetargetToLatestState()
    {
        var hud = new HudWindow();

        hud.UpdateState(new DictationState(DictationStatus.Starting), 0, "");
        Dispatcher.UIThread.RunJobs();
        hud.UpdateState(new DictationState(DictationStatus.Listening), 0.4f, "Hello");
        Dispatcher.UIThread.RunJobs();
        hud.UpdateState(new DictationState(DictationStatus.Finishing), 0, "");
        Dispatcher.UIThread.RunJobs();
        hud.UpdateState(new DictationState(DictationStatus.Listening), 0.7f, "Again");
        Dispatcher.UIThread.RunJobs();

        hud.FindControl<TextBlock>("TelemetryBadge")!.Text.ShouldBe("REC");
        hud.FindControl<TextBlock>("TranscriptText")!.Text.ShouldBe("Again");
        hud.FindControl<Murmur.App.Controls.VUMeterControl>("HudMeter")!.IsActive.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void HudWindow_IdleState_DeactivatesMeter()
    {
        var hud = new HudWindow();

        hud.UpdateState(new DictationState(DictationStatus.Listening), 0.5f, "Speaking");
        Dispatcher.UIThread.RunJobs();
        hud.FindControl<Murmur.App.Controls.VUMeterControl>("HudMeter")!.IsActive.ShouldBeTrue();

        hud.UpdateState(new DictationState(DictationStatus.Idle), 0.0f, "");
        Dispatcher.UIThread.RunJobs();

        hud.FindControl<Murmur.App.Controls.VUMeterControl>("HudMeter")!.IsActive.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void HudWindow_UsesHudPaletteForMeter()
    {
        var hud = new HudWindow();
        var meter = hud.FindControl<Murmur.App.Controls.VUMeterControl>("HudMeter");

        meter.ShouldNotBeNull();
        meter.UseHudPalette.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void HudWindow_WindowProperties_PreserveNonActivatingToolWindowConfiguration()
    {
        var hud = new HudWindow();

        hud.Topmost.ShouldBeTrue();
        hud.ShowActivated.ShouldBeFalse();
        hud.ShowInTaskbar.ShouldBeFalse();
        hud.CanResize.ShouldBeFalse();
        hud.SystemDecorations.ShouldBe(SystemDecorations.None);
    }

    [AvaloniaFact]
    public void HudWindow_EntryUsesOrbSurfaceAndSeparateContentLayer()
    {
        var hud = new HudWindow();

        hud.UpdateState(new DictationState(DictationStatus.Starting), 0, "");
        Dispatcher.UIThread.RunJobs();

        var surface = hud.FindControl<Border>("HudSurface");
        var content = hud.FindControl<Border>("HudContentSurface");

        surface.ShouldNotBeNull();
        content.ShouldNotBeNull();
        surface.Width.ShouldBe(52);
        surface.Height.ShouldBe(52);
        surface.CornerRadius.TopLeft.ShouldBe(26);
        hud.FindControl<TextBlock>("TelemetryBadge")!.Text.ShouldBe("ARM");
    }

    [AvaloniaFact]
    public void HudWindow_ViewportIsStableAndNonActivating()
    {
        var hud = new HudWindow();
        var viewport = hud.FindControl<Grid>("HudViewport");

        viewport.ShouldNotBeNull();
        viewport.Width.ShouldBe(488);
        viewport.Height.ShouldBe(83);
        hud.ShowActivated.ShouldBeFalse();
        hud.Topmost.ShouldBeTrue();
    }

    [Fact]
    public void HudWindow_NativeRegionUsesScaledSurfaceBoundsAndCornerRadius()
    {
        var region = HudWindow.CalculateNativeRegion(
            new Rect(91.5, 19.5, 305, 44),
            new CornerRadius(22),
            1.25);

        region.Bounds.ShouldBe(new PixelRect(112, 22, 386, 60));
        region.CornerDiameter.ShouldBe(55);
    }

    [Fact]
    public void HudWindow_NativeRegionLeavesRoomForRightAndBottomBorderPixels()
    {
        var region = HudWindow.CalculateNativeRegion(
            new Rect(0, 0, 307, 44),
            new CornerRadius(22),
            1);

        region.Bounds.ShouldBe(new PixelRect(-2, -2, 311, 48));
    }

    [AvaloniaFact]
    public void HudWindow_UsesTransparentMeterWell()
    {
        var hud = new HudWindow();

        hud.TryFindResource("HudMeterWellBrush", hud.ActualThemeVariant, out var wellBrush)
            .ShouldBeTrue();
        wellBrush.ShouldBeOfType<SolidColorBrush>().Color.A.ShouldBe((byte)0);
    }

    [AvaloniaFact]
    public void HudWindow_FinishingPreservesLiveTranscriptAndShowsProcessingDots()
    {
        var hud = new HudWindow();

        hud.UpdateState(new DictationState(DictationStatus.Listening), 0.4f, "live words");
        Dispatcher.UIThread.RunJobs();
        hud.UpdateState(new DictationState(DictationStatus.Finishing), 0, "live words");
        Dispatcher.UIThread.RunJobs();

        hud.FindControl<TextBlock>("TelemetryBadge")!.Text.ShouldBe("PROC");
        hud.FindControl<TextBlock>("TranscriptText")!.Text.ShouldBe("live words");
        hud.FindControl<StackPanel>("ProcessingDots")!.IsVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void HudWindow_IdleStopsProcessingDots()
    {
        var hud = new HudWindow();
        hud.UpdateState(new DictationState(DictationStatus.Finishing), 0, "Transcribing…");
        Dispatcher.UIThread.RunJobs();
        hud.UpdateState(new DictationState(DictationStatus.Idle), 0, "");
        Dispatcher.UIThread.RunJobs();

        hud.FindControl<StackPanel>("ProcessingDots")!.IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task HudWindow_IdleState_HidesAfterExitTransition()
    {
        var hud = new HudWindow();

        hud.UpdateState(
            new DictationState(DictationStatus.Listening),
            0.5f,
            "Speaking");

        Dispatcher.UIThread.RunJobs();
        hud.IsVisible.ShouldBeTrue();

        hud.UpdateState(
            new DictationState(DictationStatus.Idle),
            0.0f,
            "");

        Dispatcher.UIThread.RunJobs();

        await Task.Delay(320);
        Dispatcher.UIThread.RunJobs();

        hud.IsVisible.ShouldBeFalse();
        var content = hud.FindControl<Border>("HudContentSurface");
        content.ShouldNotBeNull();
        content.Opacity.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task HudWindow_IdleFollowedByImmediateListening_RemainsVisible()
    {
        var hud = new HudWindow();

        hud.UpdateState(
            new DictationState(DictationStatus.Listening),
            0.5f,
            "Speaking");
        Dispatcher.UIThread.RunJobs();
        hud.IsVisible.ShouldBeTrue();

        hud.UpdateState(
            new DictationState(DictationStatus.Idle),
            0.0f,
            "");
        Dispatcher.UIThread.RunJobs();

        hud.UpdateState(
            new DictationState(DictationStatus.Listening),
            0.6f,
            "Speaking again");
        Dispatcher.UIThread.RunJobs();

        await Task.Delay(320);
        Dispatcher.UIThread.RunJobs();

        hud.IsVisible.ShouldBeTrue();
        hud.FindControl<TextBlock>("TelemetryBadge")!.Text.ShouldBe("REC");
        hud.FindControl<Murmur.App.Controls.VUMeterControl>("HudMeter")!.IsActive.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task HudWindow_EntryKeepsContentHiddenUntilRevealDelay()
    {
        var hud = new HudWindow { IsReducedMotion = () => false };
        hud.Resources["HudContentRevealDelayMs"] = 60000d;

        hud.UpdateState(
            new DictationState(DictationStatus.Starting),
            0,
            "");

        Dispatcher.UIThread.RunJobs();

        var surface = hud.FindControl<Border>("HudSurface");
        var content = hud.FindControl<Border>("HudContentSurface");

        surface.ShouldNotBeNull();
        content.ShouldNotBeNull();

        surface.Width.ShouldBe(52);
        surface.Height.ShouldBe(52);
        content.Opacity.ShouldBe(0);

        var timer = typeof(HudWindow).GetField("_contentRevealTimer",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(hud).ShouldBeOfType<DispatcherTimer>();
        timer.IsEnabled.ShouldBeTrue();
        timer.Interval.ShouldBe(TimeSpan.FromMinutes(1));
        timer.Interval = TimeSpan.FromMilliseconds(1);

        await Task.Delay(120);
        Dispatcher.UIThread.RunJobs();

        content.Opacity.ShouldBeGreaterThan(0);
    }

    [AvaloniaFact]
    public async Task HudWindow_IdleCancelsPendingContentReveal()
    {
        var hud = new HudWindow();

        hud.UpdateState(
            new DictationState(DictationStatus.Starting),
            0,
            "");

        Dispatcher.UIThread.RunJobs();

        hud.UpdateState(
            new DictationState(DictationStatus.Idle),
            0,
            "");

        Dispatcher.UIThread.RunJobs();

        await Task.Delay(80);
        Dispatcher.UIThread.RunJobs();

        var content = hud.FindControl<Border>("HudContentSurface");

        content.ShouldNotBeNull();
        content.Opacity.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task HudWindow_AudioLevelUpdatesDoNotRestartContentReveal()
    {
        var hud = new HudWindow { IsReducedMotion = () => false };

        hud.UpdateState(
            new DictationState(DictationStatus.Starting),
            0,
            "");

        Dispatcher.UIThread.RunJobs();

        await Task.Delay(20);

        hud.UpdateState(
            new DictationState(DictationStatus.Listening),
            0.2f,
            "Hello");

        Dispatcher.UIThread.RunJobs();

        await Task.Delay(20);

        hud.UpdateState(
            new DictationState(DictationStatus.Listening),
            0.8f,
            "Hello");

        Dispatcher.UIThread.RunJobs();

        await Task.Delay(60);
        Dispatcher.UIThread.RunJobs();

        var content = hud.FindControl<Border>("HudContentSurface");

        content.ShouldNotBeNull();
        content.Opacity.ShouldBeGreaterThan(0);
    }

    [AvaloniaFact]
    public void HudWindow_LevelUpdate_PreservesLatestTranscript()
    {
        var hud = new HudWindow();

        hud.UpdateState(
            new DictationState(DictationStatus.Listening),
            0,
            "Das ist ein Test");
        Dispatcher.UIThread.RunJobs();

        hud.UpdateLevel(DictationStatus.Listening, 0.5f);
        Dispatcher.UIThread.RunJobs();

        hud.FindControl<TextBlock>("TranscriptText")!.Text.ShouldBe("Das ist ein Test");
        hud.FindControl<Murmur.App.Controls.VUMeterControl>("HudMeter")!.Level.ShouldBe(0.5f);
    }

    [AvaloniaFact]
    public void HudWindow_RapidPartialUpdates_KeepNewestText()
    {
        var hud = new HudWindow();
        var listening = new DictationState(DictationStatus.Listening);

        hud.UpdateState(listening, 0.2f, "Das");
        hud.UpdateState(listening, 0.3f, "Das ist");
        hud.UpdateState(listening, 0.4f, "Das ist ein Test");
        Dispatcher.UIThread.RunJobs();

        hud.FindControl<TextBlock>("TranscriptText")!.Text.ShouldBe("Das ist ein Test");
    }

    [AvaloniaFact]
    public async Task HudWindow_RapidRestart_CannotBeHiddenByOldExitCallback()
    {
        var hud = new HudWindow();
        var listening = new DictationState(DictationStatus.Listening);

        hud.UpdateState(listening, 0.4f, "first");
        Dispatcher.UIThread.RunJobs();
        hud.UpdateState(DictationState.Idle, 0, "");
        Dispatcher.UIThread.RunJobs();
        hud.UpdateState(listening, 0.5f, "second");
        Dispatcher.UIThread.RunJobs();

        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();

        hud.IsVisible.ShouldBeTrue();
        hud.FindControl<TextBlock>("TranscriptText")!.Text.ShouldBe("second");
        hud.FindControl<TextBlock>("TelemetryBadge")!.Text.ShouldBe("REC");
    }

    [AvaloniaFact]
    public void HudWindow_UpdateProfileBadge_UpdatesBadgeText()
    {
        var hud = new HudWindow();
        var badge = hud.FindControl<TextBlock>("ProfileBadge");
        badge.ShouldNotBeNull();
        badge.Text.ShouldBe("STD");

        hud.UpdateProfileBadge(new DictationProfile { FormatterKind = ProfileFormatterKind.Math });
        Dispatcher.UIThread.RunJobs();
        badge.Text.ShouldBe("MATH");

        hud.UpdateProfileBadge(new DictationProfile { FormatterKind = ProfileFormatterKind.Standard });
        Dispatcher.UIThread.RunJobs();
        badge.Text.ShouldBe("STD");
    }

    [Fact]
    public void HudWindow_BuildTranscriptTail_Contracts()
    {
        HudWindow.BuildTranscriptTail(null, 15).ShouldBe("");
        HudWindow.BuildTranscriptTail("  one   two  ", 15).ShouldBe("one two");
        HudWindow.BuildTranscriptTail("one two three", 3).ShouldBe("one two three");
        HudWindow.BuildTranscriptTail("one two three four", 3).ShouldBe("… two three four");
        HudWindow.BuildTranscriptTail("word", 0).ShouldBe("");
    }

    [AvaloniaFact]
    public void HudWindow_ListeningShowsLastFifteenWordsInPreview()
    {
        var hud = new HudWindow();
        var longTranscript = "word1 word2 word3 word4 word5 word6 word7 word8 word9 word10 word11 word12 word13 word14 word15 word16 word17";
        hud.UpdateState(new DictationState(DictationStatus.Listening), 0.5f, longTranscript);
        Dispatcher.UIThread.RunJobs();

        var transcript = hud.FindControl<TextBlock>("TranscriptText");
        transcript.ShouldNotBeNull();
        transcript.Text.ShouldStartWith("… word3");
        transcript.Text.ShouldEndWith("word17");
    }

    [AvaloniaFact]
    public void HudWindow_TranscriptPreviewUsesAtMostTwoLines()
    {
        var hud = new HudWindow();
        var transcript = hud.FindControl<TextBlock>("TranscriptText");
        transcript.ShouldNotBeNull();
        transcript.MaxLines.ShouldBe(2);
        transcript.TextWrapping.ShouldBe(TextWrapping.Wrap);
    }

    [AvaloniaFact]
    public async Task HudWindow_RecordingSurfaceShrinksAndGrowsByWidthBuckets()
    {
        var hud = new HudWindow();
        var content = hud.FindControl<Border>("HudContentSurface");
        content.ShouldNotBeNull();

        hud.UpdateState(new DictationState(DictationStatus.Listening), 0.5f, "Hi");
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        var shortWidth = content.Width;

        hud.UpdateState(new DictationState(DictationStatus.Listening), 0.5f, "This is a much longer sentence that expands the width of the recording HUD significantly");
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        var longWidth = content.Width;

        longWidth.ShouldBeGreaterThan(shortWidth);

        hud.UpdateState(new DictationState(DictationStatus.Listening), 0.5f, "Hi");
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        content.Width.ShouldBeLessThan(longWidth);
    }

    [AvaloniaFact]
    public void HudWindow_FinishingUsesBoundedTranscriptPreview()
    {
        var hud = new HudWindow();
        var longTranscript = "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen";
        hud.UpdateState(new DictationState(DictationStatus.Finishing), 0.0f, longTranscript);
        Dispatcher.UIThread.RunJobs();

        var transcript = hud.FindControl<TextBlock>("TranscriptText");
        transcript.ShouldNotBeNull();
        transcript.Text.ShouldStartWith("… ");
    }

    [AvaloniaFact]
    public void HudWindow_RainbowRim_OnlyDuringFinishing()
    {
        var hud = new HudWindow { IsReducedMotion = () => false };
        var rim = hud.FindControl<Panel>("RainbowRim")!;

        hud.UpdateState(new DictationState(DictationStatus.Starting), 0, "");
        Dispatcher.UIThread.RunJobs();
        rim.Classes.ShouldNotContain("active");

        hud.UpdateState(new DictationState(DictationStatus.Listening), 0.5f, "words");
        Dispatcher.UIThread.RunJobs();
        rim.Classes.ShouldNotContain("active");
        hud.IsRainbowAnimating.ShouldBeFalse();

        hud.UpdateState(new DictationState(DictationStatus.Finishing), 0, "words");
        Dispatcher.UIThread.RunJobs();
        rim.Classes.ShouldContain("active");
        hud.IsRainbowAnimating.ShouldBeTrue();

        hud.UpdateState(new DictationState(DictationStatus.Error, "boom"), 0, "");
        Dispatcher.UIThread.RunJobs();
        rim.Classes.ShouldNotContain("active");
        hud.IsRainbowAnimating.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void HudWindow_RainbowRim_StopsOnIdleAndClose()
    {
        var hud = new HudWindow { IsReducedMotion = () => false };
        var rim = hud.FindControl<Panel>("RainbowRim")!;

        hud.UpdateState(new DictationState(DictationStatus.Finishing), 0, "");
        Dispatcher.UIThread.RunJobs();
        hud.IsRainbowAnimating.ShouldBeTrue();

        hud.UpdateState(DictationState.Idle, 0, "");
        Dispatcher.UIThread.RunJobs();
        rim.Classes.ShouldNotContain("active");
        hud.IsRainbowAnimating.ShouldBeFalse();

        hud.UpdateState(new DictationState(DictationStatus.Finishing), 0, "");
        Dispatcher.UIThread.RunJobs();
        hud.IsRainbowAnimating.ShouldBeTrue();

        hud.Close();
        Dispatcher.UIThread.RunJobs();
        rim.Classes.ShouldNotContain("active");
        hud.IsRainbowAnimating.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void HudWindow_RainbowRim_ReducedMotionIsStatic()
    {
        var hud = new HudWindow { IsReducedMotion = () => true };
        var rim = hud.FindControl<Panel>("RainbowRim")!;
        var brush = (ConicGradientBrush)rim.Resources["HudRainbowBrush"]!;
        brush.Angle = 42;

        hud.UpdateState(new DictationState(DictationStatus.Finishing), 0, "");
        Dispatcher.UIThread.RunJobs();

        rim.Classes.ShouldContain("active");
        hud.IsRainbowAnimating.ShouldBeFalse();
        brush.Angle.ShouldBe(-90);
    }

    [AvaloniaFact]
    public void HudWindow_LongSingleWordDoesNotOverflowSurface()
    {
        var hud = new HudWindow();
        var superLongWord = new string('a', 200);
        hud.UpdateState(new DictationState(DictationStatus.Listening), 0.5f, superLongWord);
        Dispatcher.UIThread.RunJobs();

        var surface = hud.FindControl<Border>("HudSurface");
        surface.ShouldNotBeNull();
        surface.Width.ShouldBeLessThanOrEqualTo(416);
    }
}
