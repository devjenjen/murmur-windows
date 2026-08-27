using Avalonia.Automation;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Murmur.App.Controls;
using Shouldly;
using Xunit;

namespace Murmur.App.Tests;

public class InstrumentationControlTests
{
    [AvaloniaFact]
    public void VUMeter_WhenDetached_DoesNotRunTimer()
    {
        var meter = new VUMeterControl();
        meter.IsAnimationRunning.ShouldBeFalse();

        meter.Level = 0.5f;
        meter.IsActive = true;
        Dispatcher.UIThread.RunJobs();

        // While detached, timer should remain stopped
        meter.IsAnimationRunning.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void VUMeter_WhenAttachedAndActive_RunsTimerAndSettles()
    {
        var window = new Window();
        var meter = new VUMeterControl();
        window.Content = meter;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Initially inactive at 0 -> not running
        meter.IsAnimationRunning.ShouldBeFalse();

        // Activate with signal -> timer starts
        meter.IsActive = true;
        meter.Level = 0.8f;
        Dispatcher.UIThread.RunJobs();
        meter.IsAnimationRunning.ShouldBeTrue();

        // Accessible item status updated
        AutomationProperties.GetItemStatus(meter).ShouldBe("80%");

        // Detach -> timer stops
        window.Content = null;
        Dispatcher.UIThread.RunJobs();
        meter.IsAnimationRunning.ShouldBeFalse();
        window.Close();
    }

    [AvaloniaFact]
    public void VUMeter_AccessibleNameAndQuantizedItemStatus()
    {
        var meter = new VUMeterControl();
        AutomationProperties.GetName(meter).ShouldBe("Microphone level");

        meter.IsActive = true;
        meter.Level = 0.44f;
        Dispatcher.UIThread.RunJobs();
        AutomationProperties.GetItemStatus(meter).ShouldBe("40%");

        meter.Level = 0.48f;
        Dispatcher.UIThread.RunJobs();
        AutomationProperties.GetItemStatus(meter).ShouldBe("50%");
    }

    [AvaloniaFact]
    public void Waveform_WhenDetachedOrIdle_DoesNotRunTimer()
    {
        var waveform = new WaveformControl();
        waveform.IsAnimationRunning.ShouldBeFalse();

        var window = new Window();
        window.Content = waveform;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Zero level -> timer should not run
        waveform.Level = 0.0f;
        waveform.IsActive = false;
        Dispatcher.UIThread.RunJobs();
        waveform.IsAnimationRunning.ShouldBeFalse();

        // Active with level > 0 -> timer runs
        waveform.IsActive = true;
        waveform.Level = 0.5f;
        Dispatcher.UIThread.RunJobs();
        waveform.IsAnimationRunning.ShouldBeTrue();

        // Deactivate -> timer stops
        waveform.IsActive = false;
        Dispatcher.UIThread.RunJobs();
        waveform.IsAnimationRunning.ShouldBeFalse();

        window.Close();
    }

    [Fact]
    public void VUMeter_UsesSixteenSegments()
    {
        VUMeterControl.SegmentCount.ShouldBe(16);
    }

    [AvaloniaFact]
    public void VUMeter_DefaultsToStandardPalette()
    {
        var meter = new VUMeterControl();

        meter.UseHudPalette.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void VUMeter_SettingsMode_UsesSettingsGeometryResources()
    {
        var window = new Window();
        var meter = new VUMeterControl
        {
            Width = 200,
            Height = 30,
            IsActive = true,
            Level = 0.5f
        };
        meter.Resources["SettingsMeterPaddingX"] = 12.0;
        meter.Resources["SettingsMeterPaddingY"] = 7.0;
        meter.Resources["SettingsMeterSegmentGap"] = 5.0;
        meter.Resources["SettingsMeterSegmentRadius"] = 4.0;
        meter.Resources["SettingsMeterWellRadius"] = 11.0;
        meter.Resources["SettingsMeterBorderThickness"] = 2.0;
        window.Content = meter;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(VUMeterControl).GetField("_padX", flags)!.GetValue(meter).ShouldBe(12.0);
        typeof(VUMeterControl).GetField("_padY", flags)!.GetValue(meter).ShouldBe(7.0);
        typeof(VUMeterControl).GetField("_segGap", flags)!.GetValue(meter).ShouldBe(5.0);
        typeof(VUMeterControl).GetField("_segRadius", flags)!.GetValue(meter).ShouldBe(4.0);
        typeof(VUMeterControl).GetField("_wellRadius", flags)!.GetValue(meter).ShouldBe(11.0);
        typeof(VUMeterControl).GetField("_borderThickness", flags)!.GetValue(meter).ShouldBe(2.0);

        window.Close();
    }

    [AvaloniaFact]
    public void VUMeter_SettingsPalette_PeakResourceIsNotRedFallback()
    {
        var window = new Window();
        var meter = new VUMeterControl();
        window.Content = meter;

        window.TryFindResource("MeterPeakBrush", window.ActualThemeVariant, out var peakRes).ShouldBeTrue();
        var peakBrush = peakRes.ShouldBeAssignableTo<ISolidColorBrush>();
        peakBrush.ShouldNotBeNull();
        peakBrush.Color.G.ShouldBeGreaterThan((byte)80);
    }

    [AvaloniaFact]
    public void VUMeter_RendersBothHudAndSettingsModesWithoutError()
    {
        var window = new Window();
        var settingsMeter = new VUMeterControl { UseHudPalette = false, Height = 30, Width = 200, Level = 0.6f, IsActive = true };
        var hudMeter = new VUMeterControl { UseHudPalette = true, Height = 18, Width = 120, Level = 0.6f, IsActive = true };

        var panel = new StackPanel();
        panel.Children.Add(settingsMeter);
        panel.Children.Add(hudMeter);

        window.Content = panel;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        settingsMeter.UseHudPalette.ShouldBeFalse();
        hudMeter.UseHudPalette.ShouldBeTrue();
        window.Close();
    }
}
