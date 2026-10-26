using System.Reflection;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Murmur.App.Controls;
using Murmur.App.Views;
using Murmur.Core.State;
using Shouldly;

namespace Murmur.App.Tests;

public class StudioHudTests
{
    private static readonly string[] DotNames = ["ProcessingDot1", "ProcessingDot2", "ProcessingDot3"];
    private static T Part<T>(HudWindow hud, string name) where T : Control => hud.FindControl<T>(name)!;

    private static object? Field(object instance, string name) => instance.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);

    [AvaloniaFact]
    public async Task LateLevelUpdates_CannotUndoLatestLifecycleState()
    {
        var hud = new HudWindow { IsReducedMotion = () => false };
        try
        {
            hud.UpdateState(DictationState.Listening, .6f, "words");
            hud.UpdateState(DictationState.ErrorState("lost"), 0, "words");
            hud.UpdateLevel(DictationStatus.Listening, .9f);
            var meter = Part<VUMeterControl>(hud, "HudMeter");
            meter.IsActive.ShouldBeFalse();
            meter.IsAnimationRunning.ShouldBeFalse();
            await Task.Run(() => hud.UpdateLevel(DictationStatus.Listening, .9f));
            Dispatcher.UIThread.RunJobs();
            meter.IsActive.ShouldBeFalse();
            meter.IsAnimationRunning.ShouldBeFalse();
            hud.UpdateState(DictationState.Listening, .8f, "again");
            hud.UpdateLevel(DictationStatus.Finishing, 0);
            meter.IsActive.ShouldBeTrue();
            meter.Level.ShouldBe(.8f);
            hud.Hide();
            hud.UpdateLevel(DictationStatus.Listening, .9f);
            meter.IsAnimationRunning.ShouldBeFalse();
            meter.CurrentPosition.ShouldBe(0);
        }
        finally { hud.Close(); }
    }

    [AvaloniaFact]
    public void RapidEntryAndRestart_RetainLatestTextPlacementAndCancelOldHide()
    {
        var hud = new HudWindow { IsReducedMotion = () => false };
        try
        {
            hud.SetPlacement("bottom-right");
            hud.UpdateState(DictationState.Starting, 0, "");
            hud.UpdateState(DictationState.Listening, .6f, "first");
            hud.UpdateState(DictationState.Finishing, 0, "first");
            hud.UpdateState(DictationState.Idle, 0, "");
            hud.UpdateState(DictationState.Listening, .8f, "latest");
            Dispatcher.UIThread.RunJobs();
            hud.IsVisible.ShouldBeTrue();
            Part<TextBlock>(hud, "TranscriptText").Text.ShouldBe("latest");
            Part<Panel>(hud, "RecordingRim").Classes.ShouldContain("active");
            Part<Panel>(hud, "RainbowRim").Classes.ShouldNotContain("active");
            Part<Border>(hud, "HudSurface").HorizontalAlignment.ShouldBe(Avalonia.Layout.HorizontalAlignment.Right);
            Part<Border>(hud, "HudContentSurface").VerticalAlignment.ShouldBe(Avalonia.Layout.VerticalAlignment.Bottom);
            Field(hud, "_hideTimer").ShouldBeNull();
            Field(hud, "_processingTimer").ShouldBeNull();
            hud.IsRainbowAnimating.ShouldBeFalse();
        }
        finally { hud.Close(); }
    }

    // Avalonia's clock is internal; drive its real transition engine without wall-clock sleeps.
    private sealed class ManualClock
    {
        private readonly object _clock = Activator.CreateInstance(
            typeof(Animatable).Assembly.GetType("Avalonia.Animation.Clock")!, nonPublic: true)!;
        public void Attach(Animatable target) => typeof(Animatable)
            .GetProperty("Clock", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, _clock);
        public void Advance(double seconds) => _clock.GetType().BaseType!
            .GetMethod("Pulse", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .Invoke(_clock, [TimeSpan.FromSeconds(seconds)]);
    }

    [AvaloniaFact]
    public void RimFade_ReversesFromCurrentOpacityAndErrorAbortsImmediately()
    {
        var hud = new HudWindow { IsReducedMotion = () => false };
        try
        {
            hud.UpdateState(DictationState.Starting, 0, "");
            Dispatcher.UIThread.RunJobs();
            var recording = Part<Panel>(hud, "RecordingRim");
            var rainbow = Part<Panel>(hud, "RainbowRim");
            var clock = new ManualClock();
            clock.Attach(recording);
            clock.Attach(rainbow);
            hud.UpdateState(DictationState.Listening, .5f, "words");
            clock.Advance(0);
            clock.Advance(.2);
            recording.Opacity.ShouldBe(1, .0001);
            hud.UpdateState(DictationState.Finishing, 0, "words");
            clock.Advance(.2);
            clock.Advance(.25);
            recording.Opacity.ShouldBeInRange(.01, .99);
            rainbow.Opacity.ShouldBeInRange(.01, .99);
            (recording.Opacity + rainbow.Opacity).ShouldBe(1, .0001);
            var recOpacity = recording.Opacity;
            var rainbowOpacity = rainbow.Opacity;
            hud.UpdateState(DictationState.Listening, .8f, "again");
            recording.Opacity.ShouldBe(recOpacity, .0001);
            rainbow.Opacity.ShouldBe(rainbowOpacity, .0001);
            clock.Advance(.25);
            clock.Advance(.45);
            recording.Opacity.ShouldBe(1, .0001);
            rainbow.Opacity.ShouldBe(0, .0001);
            hud.UpdateState(DictationState.Finishing, 0, "again");
            hud.UpdateState(DictationState.ErrorState("device lost"), 0, "again");
            Dispatcher.UIThread.RunJobs();
            recording.Opacity.ShouldBe(0);
            rainbow.Opacity.ShouldBe(0);
            hud.IsRainbowAnimating.ShouldBeFalse();
            Field(hud, "_processingTimer").ShouldBeNull();
            Field(hud, "_contentRevealTimer").ShouldBeNull();
            Part<TextBlock>(hud, "TranscriptText").Text!.ShouldContain("device lost");
            Part<Border>(hud, "HudContentSurface").Opacity.ShouldBeGreaterThan(0);
        }
        finally { hud.Close(); }
    }

    [AvaloniaFact]
    public void Lifecycle_StopsLoopsOnStartingIdleErrorHiddenAndClose()
    {
        foreach (var ending in new[] { "starting", "idle", "error", "hidden", "close" })
        {
            var hud = new HudWindow { IsReducedMotion = () => false };
            try
            {
                hud.UpdateState(DictationState.Listening, .8f, "words");
                Dispatcher.UIThread.RunJobs();
                var meter = Part<VUMeterControl>(hud, "HudMeter");
                meter.StepBallistics(.09);
                var peak = meter.CurrentPosition;
                hud.UpdateState(DictationState.Finishing, 0, "words");
                meter.IsActive.ShouldBeFalse();
                meter.StepBallistics(.4);
                meter.CurrentPosition.ShouldBeLessThan(peak * .06);
                Field(hud, "_processingTimer").ShouldBeOfType<DispatcherTimer>().IsEnabled.ShouldBeTrue();
                hud.IsRainbowAnimating.ShouldBeTrue();
                switch (ending)
                {
                    case "starting": hud.UpdateState(DictationState.Starting, 0, ""); break;
                    case "idle": hud.UpdateState(DictationState.Idle, 0, ""); break;
                    case "error": hud.UpdateState(DictationState.ErrorState("failed"), 0, ""); break;
                    case "hidden": hud.Hide(); break;
                    case "close": hud.Close(); break;
                }
                var angle = ((ConicGradientBrush)Part<Panel>(hud, "RainbowRim").Resources["HudRainbowBrush"]!).Angle;
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                Dispatcher.UIThread.RunJobs();
                hud.IsRainbowAnimating.ShouldBeFalse();
                Field(hud, "_rainbowFrameRequested").ShouldBe(false);
                Field(hud, "_processingTimer").ShouldBeNull();
                meter.IsAnimationRunning.ShouldBeFalse();
                meter.CurrentPosition.ShouldBe(0);
                Part<StackPanel>(hud, "ProcessingDots").IsVisible.ShouldBeFalse();
                ((ConicGradientBrush)Part<Panel>(hud, "RainbowRim").Resources["HudRainbowBrush"]!).Angle.ShouldBe(angle);
                if (ending is "error" or "hidden" or "close")
                {
                    Field(hud, "_contentRevealTimer").ShouldBeNull();
                    Field(hud, "_hideTimer").ShouldBeNull();
                }
            }
            finally { hud.Close(); }
        }
    }

    [AvaloniaFact]
    public void MotionPreferenceChange_DuringFinishingStopsDotsRotationAndTransitions()
    {
        var reduced = false;
        var reads = 0;
        var hud = new HudWindow { IsReducedMotion = () => { reads++; return reduced; } };
        try
        {
            hud.UpdateState(DictationState.Finishing, 0, "words");
            Dispatcher.UIThread.RunJobs();
            var initialReads = reads;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            Dispatcher.UIThread.RunJobs();
            reads.ShouldBe(initialReads);
            reduced = true;
            hud.UpdateState(DictationState.Finishing, 0, "words");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            Dispatcher.UIThread.RunJobs();
            var rim = Part<Panel>(hud, "RainbowRim");
            rim.Opacity.ShouldBe(1);
            rim.Transitions.ShouldBeNull();
            hud.IsRainbowAnimating.ShouldBeFalse();
            Field(hud, "_rainbowFrameRequested").ShouldBe(false);
            Field(hud, "_processingTimer").ShouldBeNull();
            Field(hud, "_contentRevealTimer").ShouldBeNull();
            Part<Border>(hud, "HudContentSurface").Opacity.ShouldBe(1);
            ((ConicGradientBrush)rim.Resources["HudRainbowBrush"]!).Angle.ShouldBe(-90);
            reduced = false;
            hud.UpdateState(DictationState.Finishing, 0, "words");
            rim.Transitions.ShouldNotBeNull();
            hud.IsRainbowAnimating.ShouldBeTrue();
            Field(hud, "_processingTimer").ShouldBeOfType<DispatcherTimer>().IsEnabled.ShouldBeTrue();
        }
        finally { hud.Close(); }
    }

    [AvaloniaFact]
    public void SettingsMeter_KeepsLegacyBallisticsAndTimerWhileHudReducedMotionSnaps()
    {
        var settings = new VUMeterControl { ReducedMotion = true, IsActive = true, Level = .75f };
        var window = new Window { Content = settings };
        try
        {
            window.Show();
            settings.IsAnimationRunning.ShouldBeTrue();
            settings.CurrentPosition.ShouldBe(0);
            typeof(VUMeterControl).GetMethod("AdvanceNeedle", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(settings, null);
            settings.CurrentPosition.ShouldBe(.288, .000001);
            settings.HoldRemainingSeconds.ShouldBe(0);
            var hud = new VUMeterControl { UseHudPalette = true, ReducedMotion = true, IsActive = true, Level = .75f };
            hud.CurrentPosition.ShouldBe(.75);
            hud.Level = .25f;
            hud.CurrentPosition.ShouldBe(.25);
            hud.HoldRemainingSeconds.ShouldBe(0);
            hud.IsAnimationRunning.ShouldBeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RecordingAndFinishing_ShareHeightRadiusAndRimGeometry()
    {
        var hud = new HudWindow { IsReducedMotion = () => true };
        try
        {
            hud.UpdateState(DictationState.Listening, .7f, "words");
            var surface = Part<Border>(hud, "HudSurface");
            var radius = surface.CornerRadius;
            surface.Height.ShouldBe(44);
            hud.UpdateState(DictationState.Finishing, 0, "words");
            surface.Height.ShouldBe(44);
            surface.CornerRadius.ShouldBe(radius);
            Part<Border>(hud, "HudContentSurface").Height.ShouldBe(44);
            var recording = Part<Panel>(hud, "RecordingRim");
            var rainbow = Part<Panel>(hud, "RainbowRim");
            recording.ShouldNotBeNull();
            recording.Margin.ShouldBe(rainbow.Margin);
            recording.Children.OfType<Border>().Select(b => b.BorderThickness)
                .ShouldBe(rainbow.Children.OfType<Border>().Select(b => b.BorderThickness));
        }
        finally { hud.Close(); }
    }

    [AvaloniaFact]
    public void ReducedMotion_UsesImmediatePropertiesAndNoMeterDotsOrRimLoops()
    {
        var hud = new HudWindow { IsReducedMotion = () => true };
        try
        {
            foreach (var state in new[] { DictationState.Starting, DictationState.Listening, DictationState.Finishing })
            {
                hud.UpdateState(state, .75f, "words");
                Dispatcher.UIThread.RunJobs();
                var meter = Part<VUMeterControl>(hud, "HudMeter");
                meter.IsAnimationRunning.ShouldBeFalse();
                meter.CurrentPosition.ShouldBe(state.Status == DictationStatus.Listening ? .75 : 0);
                meter.HoldRemainingSeconds.ShouldBe(0);
                Part<Panel>(hud, "RecordingRim").Opacity.ShouldBe(state.Status == DictationStatus.Listening ? 1 : 0);
                Part<Panel>(hud, "RainbowRim").Opacity.ShouldBe(state.Status == DictationStatus.Finishing ? 1 : 0);
                Part<Border>(hud, "HudSurface").Opacity.ShouldBe(1);
                Part<Border>(hud, "HudContentSurface").Opacity.ShouldBe(1);
                Part<Border>(hud, "HudSurface").Transitions.ShouldBeNull();
                Part<Border>(hud, "HudContentSurface").Transitions.ShouldBeNull();
                Part<TextBlock>(hud, "TranscriptText").Transitions.ShouldBeNull();
                Part<Panel>(hud, "RecordingRim").Transitions.ShouldBeNull();
                Part<Panel>(hud, "RainbowRim").Transitions.ShouldBeNull();
                Field(hud, "_processingTimer").ShouldBeNull();
                Field(hud, "_contentRevealTimer").ShouldBeNull();
                Field(hud, "_rainbowFrameRequested").ShouldBe(false);
            }
            var brush = (ConicGradientBrush)Part<Panel>(hud, "RainbowRim").Resources["HudRainbowBrush"]!;
            var dots = DotNames
                .Select(name => Part<Ellipse>(hud, name)).ToArray();
            var opacities = dots.Select(d => d.Opacity).ToArray();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(5);
            Dispatcher.UIThread.RunJobs();
            brush.Angle.ShouldBe(-90);
            dots.Select(d => d.Opacity).ShouldBe(opacities);
            hud.UpdateState(DictationState.Idle, 0, "");
            hud.IsVisible.ShouldBeFalse();
            Field(hud, "_hideTimer").ShouldBeNull();
        }
        finally { hud.Close(); }
    }

    [AvaloniaFact]
    public void HudBallistics_AttackHoldReleaseAreMonotoneAndTimeStepIndependent()
    {
        var meter = new VUMeterControl { UseHudPalette = true, IsActive = true, Level = .8f };
        meter.StepBallistics(.09);
        meter.CurrentPosition.ShouldBeInRange(.75, .8);
        var peak = meter.CurrentPosition;
        meter.Level = 0;
        meter.StepBallistics(.10);
        meter.CurrentPosition.ShouldBe(peak);
        meter.HoldRemainingSeconds.ShouldBe(.08, .000001);
        var split = new VUMeterControl { UseHudPalette = true, IsActive = true, Level = .8f };
        split.StepBallistics(.09);
        split.Level = 0;
        split.StepBallistics(.18);
        split.StepBallistics(.02);
        meter.StepBallistics(.10);
        meter.CurrentPosition.ShouldBe(split.CurrentPosition, .000001);
        var previous = meter.CurrentPosition;
        for (var i = 0; i < 100; i++)
        {
            meter.StepBallistics(.016);
            meter.CurrentPosition.ShouldBeInRange(0, previous);
            previous = meter.CurrentPosition;
        }
        meter.CurrentPosition.ShouldBe(0);
    }

    [AvaloniaFact]
    public void HudBallistics_NewPeakInterruptsHoldAndInactiveReleasesWithoutSecondHold()
    {
        var meter = new VUMeterControl { UseHudPalette = true, IsActive = true, Level = .6f };
        meter.StepBallistics(.09);
        meter.Level = 0;
        meter.StepBallistics(.05);
        var held = meter.CurrentPosition;
        meter.Level = 1;
        meter.StepBallistics(.03);
        meter.CurrentPosition.ShouldBeGreaterThan(held);
        var peak = meter.CurrentPosition;
        meter.IsActive = false;
        meter.StepBallistics(.4);
        meter.CurrentPosition.ShouldBeLessThan(peak * .06);
        meter.HoldRemainingSeconds.ShouldBe(0);
        meter.StepBallistics(double.NaN);
        meter.StepBallistics(-1);
        double.IsFinite(meter.CurrentPosition).ShouldBeTrue();
    }

    [AvaloniaFact]
    public void StudioTheme_ReusesRecordRedAndPreservesAmberError()
    {
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            var hud = new HudWindow { RequestedThemeVariant = theme, IsReducedMotion = () => true };
            try
            {
                hud.UpdateState(DictationState.Listening, .5f, "words");
                var lamp = Part<Ellipse>(hud, "RecordLamp");
                lamp.Parent.ShouldBeOfType<Border>().BorderThickness.Left.ShouldBeGreaterThan(0);
                var rim = Part<Panel>(hud, "RecordingRim");
                rim.Children.OfType<Border>().Last().BorderBrush.ShouldBe(lamp.Fill);
                hud.TryFindResource("HudSpecularHairlineBrush", theme, out var edge).ShouldBeTrue();
                Part<Border>(hud, "HudSurface").BorderBrush.ShouldBe(edge);
                var gradient = edge.ShouldBeAssignableTo<ILinearGradientBrush>();
                gradient!.GradientStops![0].Color.A.ShouldBeGreaterThan(gradient.GradientStops[^1].Color.A);
                hud.TryFindResource("HudSolidFallbackBrush", theme, out var fallback).ShouldBeTrue();
                var background = fallback.ShouldBeAssignableTo<ISolidColorBrush>()!.Color;
                var foreground = Part<TextBlock>(hud, "TranscriptText").Foreground.ShouldBeAssignableTo<ISolidColorBrush>()!.Color;
                var bg = Luminance(background);
                var fg = Luminance(foreground);
                ((Math.Max(bg, fg) + .05) / (Math.Min(bg, fg) + .05)).ShouldBeGreaterThan(4.5);
                hud.UpdateState(DictationState.Finishing, 0, "words");
                Part<Border>(hud, "HudSurface").BorderBrush.ShouldBe(edge);
                hud.UpdateState(DictationState.ErrorState("device lost"), 0, "words");
                hud.TryFindResource("HudErrorBrush", theme, out var amber).ShouldBeTrue();
                Part<Border>(hud, "HudSurface").BorderBrush.ShouldBe(amber);
                lamp.Fill.ShouldBe(amber);
                Part<Panel>(hud, "RainbowRim").Opacity.ShouldBe(0);
            }
            finally { hud.Close(); }
        }
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
}
