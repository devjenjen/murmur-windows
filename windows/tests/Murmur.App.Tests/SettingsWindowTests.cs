using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Murmur.App.Controls;
using Murmur.App.Converters;
using Murmur.App.Localization;
using Murmur.App.Views;
using Murmur.Core.Formatting;
using Murmur.Core.History;
using Murmur.Core.Settings;
using Murmur.Dictionary;
using Murmur.Platform.Windows.Audio;
using Shouldly;
using Xunit;

namespace Murmur.App.Tests;

public class SettingsWindowTests
{
    public SettingsWindowTests() => UiText.SetLanguage(UiLanguage.English);

    [AvaloniaFact]
    public void Save_EmitsSelectedGermanInterfaceLanguage()
    {
        var window = new SettingsWindow();
        window.SetChoices(AppSettings.Default);
        Dispatcher.UIThread.RunJobs();

        SettingsSaveRequest? savedRequest = null;
        window.SaveRequested += request => savedRequest = request;

        window.FindControl<ComboBox>("LanguageComboBox")!.SelectedIndex = 1;
        window.FindControl<Button>("SaveButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        savedRequest.ShouldNotBeNull();
        savedRequest!.Language.ShouldBe(UiLanguage.German);
    }

    [Fact]
    public void InterfaceLanguage_DoesNotConfigureSpeechLanguage()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Murmur.sln")))
        {
            root = root.Parent;
        }

        root.ShouldNotBeNull();
        var appSource = File.ReadAllText(Path.Combine(root!.FullName, "src", "Murmur.App", "App.axaml.cs"));
        var sherpaSource = File.ReadAllText(Path.Combine(root.FullName, "src", "Murmur.Speech", "SherpaOnnxTranscriber.cs"));
        var nemotronSource = File.ReadAllText(Path.Combine(root.FullName, "src", "Murmur.Speech", "NemotronStreamingTranscriber.cs"));

        appSource.ShouldContain("new SherpaOnnxTranscriber(ModelLocator.GetDefaultModelDirectory(model))");
        sherpaSource.ShouldNotContain("SetOption(\"language\"");
        nemotronSource.ShouldContain("SetOption(\"language\", \"auto\")");
    }

    [AvaloniaFact]
    public void SettingsWindow_TabsUseLensSelectorAndAccessibleName()
    {
        var window = new SettingsWindow();
        window.Show();
        var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();

        tabs.Classes.Contains("lensTabs").ShouldBeTrue();
        AutomationProperties.GetName(tabs).ShouldBe(UiText.SettingsTabs);
        window.FindControl<TabItem>("HistoryTab").ShouldNotBeNull();
        window.FindControl<TabItem>("DictionaryTab").ShouldNotBeNull();
    }

    [AvaloniaFact]
    public void SettingsWindow_LensTabs_HideSelectionIndicator()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Murmur.sln")))
        {
            root = root.Parent;
        }

        root.ShouldNotBeNull();
        var source = File.ReadAllText(Path.Combine(root!.FullName, "src", "Murmur.App", "Styles", "MurmurTokens.axaml"));
        const string selector = "<Style Selector=\"TabControl.lensTabs > TabItem:selected /template/ Border#PART_Indicator\">";

        var styleStart = source.IndexOf(selector, StringComparison.Ordinal);
        styleStart.ShouldBeGreaterThanOrEqualTo(0);
        var styleEnd = source.IndexOf("</Style>", styleStart, StringComparison.Ordinal);
        styleEnd.ShouldBeGreaterThan(styleStart);

        source[styleStart..(styleEnd + "</Style>".Length)]
            .ShouldContain("<Setter Property=\"IsVisible\" Value=\"False\" />");
    }

    [AvaloniaFact]
    public void SettingsWindow_ShellContracts_ArePreservedAndConfigured()
    {
        var window = new SettingsWindow();
        window.TransparencyLevelHint.ShouldContain(WindowTransparencyLevel.AcrylicBlur);
        window.Background.ShouldBe(Brushes.Transparent);
        window.SystemDecorations.ShouldBe(SystemDecorations.None);
        window.CanResize.ShouldBeTrue();

        var surface = window.FindControl<Border>("SettingsSurface");
        surface.ShouldNotBeNull();
        surface.Classes.ShouldContain("lensWindow");

        var chrome = window.FindControl<Grid>("WindowChrome");
        chrome.ShouldNotBeNull();

        var closeBtn = window.FindControl<Button>("CloseButton");
        closeBtn.ShouldNotBeNull();
        AutomationProperties.GetName(closeBtn).ShouldNotBeNullOrWhiteSpace();

        var minBtn = window.FindControl<Button>("MinimizeButton");
        minBtn.ShouldNotBeNull();
        AutomationProperties.GetName(minBtn).ShouldNotBeNullOrWhiteSpace();

        var maxBtn = window.FindControl<Button>("MaximizeButton");
        maxBtn.ShouldNotBeNull();
        AutomationProperties.GetName(maxBtn).ShouldNotBeNullOrWhiteSpace();
    }

    [AvaloniaFact]
    public void SettingsWindow_SetupComposition_FollowsLensContracts()
    {
        var window = new SettingsWindow();

        // Control presence
        window.FindControl<Avalonia.Controls.Shapes.Shape>("StatusLamp").ShouldNotBeNull();
        window.FindControl<TextBlock>("StatusText").ShouldNotBeNull();
        window.FindControl<TextBlock>("StatusCounterBadgeText").ShouldNotBeNull();
        window.FindControl<Avalonia.Controls.Shapes.Ellipse>("MicStatusDot").ShouldNotBeNull();
        window.FindControl<TextBlock>("MicReadinessText").ShouldNotBeNull();
        window.FindControl<Avalonia.Controls.Shapes.Ellipse>("ModelStatusDot").ShouldNotBeNull();
        window.FindControl<TextBlock>("ModelReadinessText").ShouldNotBeNull();
        window.FindControl<Avalonia.Controls.Shapes.Ellipse>("HotkeyStatusDot").ShouldNotBeNull();
        window.FindControl<TextBlock>("HotkeyReadinessText").ShouldNotBeNull();

        window.FindControl<ComboBox>("MicrophoneComboBox").ShouldNotBeNull();
        var meter = window.FindControl<VUMeterControl>("MicrophoneVUMeter");
        meter.ShouldNotBeNull();
        meter.UseHudPalette.ShouldBeFalse();
        meter.Height.ShouldBe(30.0);

        window.FindControl<TextBlock>("MicrophoneDbText").ShouldNotBeNull();
        window.FindControl<ComboBox>("SpeechModelComboBox").ShouldNotBeNull();
        window.FindControl<ProgressBar>("ModelProgressBar").ShouldNotBeNull();
        window.FindControl<TextBox>("HotkeyTextBox").ShouldNotBeNull();
        window.FindControl<ComboBox>("ProfileComboBox").ShouldNotBeNull();
        window.FindControl<ComboBox>("MathModeComboBox").ShouldNotBeNull();

        var removeFillers = window.FindControl<ToggleSwitch>("RemoveFillersCheckBox");
        removeFillers.ShouldNotBeNull();

        var selfCorrection = window.FindControl<ToggleSwitch>("EnableSelfCorrectionCheckBox");
        selfCorrection.ShouldNotBeNull();

        var restoreWindow = window.FindControl<ToggleSwitch>("RestorePreviousWindowCheckBox");
        restoreWindow.ShouldNotBeNull();

        var audioFeedback = window.FindControl<ToggleSwitch>("AudioFeedbackCheckBox");
        audioFeedback.ShouldNotBeNull();

        var startWithWindows = window.FindControl<ToggleSwitch>("StartWithWindowsCheckBox");
        startWithWindows.ShouldNotBeNull();

        window.FindControl<Button>("SaveButton").ShouldNotBeNull();
    }

    [AvaloniaFact]
    public void SettingsWindow_ReadinessSurface_UsesCompactLensHierarchy()
    {
        var window = new SettingsWindow();
        var surface = window.FindControl<Border>("ReadinessSurface");
        surface.ShouldNotBeNull();
        surface!.Classes.Contains("lensWell").ShouldBeTrue();

        var lamp = window.FindControl<Avalonia.Controls.Shapes.Rectangle>("StatusLamp");
        lamp.ShouldNotBeNull();
        lamp!.Width.ShouldBe(8.0);
        lamp.Height.ShouldBe(8.0);
        lamp.RadiusX.ShouldBe(4.0);
        lamp.RadiusY.ShouldBe(4.0);

        window.FindControl<TextBlock>("StatusText").ShouldNotBeNull();
        window.FindControl<TextBlock>("StatusCounterBadgeText").ShouldNotBeNull();
        window.FindControl<Panel>("ReadinessDetails").ShouldNotBeNull();
    }

    [AvaloniaFact]
    public void SettingsWindow_StatusUpdatesUseLensBrushesAndAssertiveLiveSetting()
    {
        UiText.SetLanguage("en");
        var window = new SettingsWindow();
        var theme = window.ActualThemeVariant;
        window.TryFindResource("LensMeterGreenBrush", theme, out var greenResource).ShouldBeTrue();
        window.TryFindResource("LensMeterAmberBrush", theme, out var amberResource).ShouldBeTrue();
        window.TryFindResource("LensWarningBrush", theme, out var warningResource).ShouldBeTrue();
        window.TryFindResource("MeterGreenBrush", theme, out var genericGreenResource).ShouldBeTrue();
        window.TryFindResource("MeterAmberBrush", theme, out var genericAmberResource).ShouldBeTrue();

        var greenBrush = greenResource!.ShouldBeAssignableTo<IBrush>();
        var amberBrush = amberResource!.ShouldBeAssignableTo<IBrush>();
        var warningBrush = warningResource!.ShouldBeAssignableTo<IBrush>();
        var genericGreenBrush = genericGreenResource!.ShouldBeAssignableTo<IBrush>();
        var genericAmberBrush = genericAmberResource!.ShouldBeAssignableTo<IBrush>();

        var lensGreenColor = greenBrush.ShouldBeAssignableTo<ISolidColorBrush>().Color;
        var lensAmberColor = amberBrush.ShouldBeAssignableTo<ISolidColorBrush>().Color;
        var warningColor = warningBrush.ShouldBeAssignableTo<ISolidColorBrush>().Color;
        var genericGreenColor = genericGreenBrush.ShouldBeAssignableTo<ISolidColorBrush>().Color;
        var genericAmberColor = genericAmberBrush.ShouldBeAssignableTo<ISolidColorBrush>().Color;

        window.SetDevices([], null);
        window.SetChoices(AppSettings.Default with { SpeechModelId = "parakeet-v3", HotkeyId = "" });
        window.SetModelProgress(0.0);
        Dispatcher.UIThread.RunJobs();

        var lamp = window.FindControl<Avalonia.Controls.Shapes.Rectangle>("StatusLamp")!;
        var micDot = window.FindControl<Avalonia.Controls.Shapes.Ellipse>("MicStatusDot")!;
        var modelDot = window.FindControl<Avalonia.Controls.Shapes.Ellipse>("ModelStatusDot")!;
        var hotkeyDot = window.FindControl<Avalonia.Controls.Shapes.Ellipse>("HotkeyStatusDot")!;
        foreach (var fill in new[] { lamp.Fill, micDot.Fill, modelDot.Fill, hotkeyDot.Fill })
        {
            var color = fill!.ShouldBeAssignableTo<ISolidColorBrush>().Color;
            color.ShouldBeOneOf(lensGreenColor, lensAmberColor);
            color.ShouldNotBe(genericGreenColor);
            color.ShouldNotBe(genericAmberColor);
        }

        window.SetDevices([new AudioInputDevice("dev-1", "Default Mic", true)], "dev-1");
        window.SetChoices(AppSettings.Default with { SpeechModelId = "parakeet-v3", HotkeyId = "vk:162" });
        window.SetModelProgress(1.0);
        Dispatcher.UIThread.RunJobs();

        window.FindControl<TextBlock>("StatusCounterBadgeText")!.Text.ShouldBe("3/3");
        var statusText = window.FindControl<TextBlock>("StatusText")!;
        statusText.Text.ShouldBe(UiText.Text("StatusModelInstalled"));
        AutomationProperties.GetLiveSetting(statusText).ShouldNotBe(AutomationLiveSetting.Off);
        var readinessDetails = window.FindControl<Panel>("ReadinessDetails")!;
        readinessDetails.IsVisible.ShouldBeFalse();
        foreach (var fill in new[] { lamp.Fill, micDot.Fill, modelDot.Fill, hotkeyDot.Fill })
        {
            var color = fill!.ShouldBeAssignableTo<ISolidColorBrush>().Color;
            color.ShouldBe(lensGreenColor);
            color.ShouldNotBe(genericGreenColor);
        }

        window.SetStatus("failure", true);
        Dispatcher.UIThread.RunJobs();
        statusText.Foreground!.ShouldBeAssignableTo<ISolidColorBrush>().Color.ShouldBe(warningColor);
        AutomationProperties.GetLiveSetting(statusText).ShouldBe(AutomationLiveSetting.Assertive);
        readinessDetails.IsVisible.ShouldBeTrue();

        window.SetStatus("Ready", false);
        Dispatcher.UIThread.RunJobs();
        readinessDetails.IsVisible.ShouldBeFalse();

        window.SetDevices([], null);
        Dispatcher.UIThread.RunJobs();
        readinessDetails.IsVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void HistoryTab_CompositionAndEmptyState_FollowsLensContracts()
    {
        var window = new SettingsWindow();
        window.SetHistory([]);
        Dispatcher.UIThread.RunJobs();

        var emptyPanel = window.FindControl<StackPanel>("HistoryEmptyPanel");
        emptyPanel.ShouldNotBeNull();
        emptyPanel.IsVisible.ShouldBeTrue();

        var emptyText = window.FindControl<TextBlock>("HistoryEmptyText");
        emptyText.ShouldNotBeNull();
        emptyText.Text.ShouldBe(UiText.NoDictations);

        var emptyMeter = window.FindControl<VUMeterControl>("HistoryEmptyMeter");
        emptyMeter.ShouldNotBeNull();
        emptyMeter.Height.ShouldBe(20.0);
        emptyMeter.UseHudPalette.ShouldBeFalse();

        var listBox = window.FindControl<ListBox>("HistoryListBox");
        listBox.ShouldNotBeNull();
        listBox.IsVisible.ShouldBeFalse();

        var countText = window.FindControl<TextBlock>("HistoryCountText");
        countText.ShouldNotBeNull();

        var deleteAllBtn = window.FindControl<Button>("DeleteAllHistoryButton");
        deleteAllBtn.ShouldNotBeNull();
        deleteAllBtn.IsEnabled.ShouldBeFalse();

        // Populate with 1 entry
        window.SetHistory([
            new DictationHistoryEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, "Parakeet", 1200, 150, "Hello Lens Settings")
        ]);
        Dispatcher.UIThread.RunJobs();

        emptyPanel.IsVisible.ShouldBeFalse();
        listBox.IsVisible.ShouldBeTrue();
        deleteAllBtn.IsEnabled.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void DictionaryTab_ExposesEditorAndAcceptsEntries()
    {
        var window = new SettingsWindow();

        var tab = window.FindControl<TabItem>("DictionaryTab");
        tab.ShouldNotBeNull();
        window.FindControl<Button>("AddDictionaryButton").ShouldNotBeNull();
        window.FindControl<ListBox>("DictionaryListBox").ShouldNotBeNull();

        window.SetDictionary([
            DictionaryEntry.Correction("cloud code", "Claude Code"),
            DictionaryEntry.Term("GitHub")
        ]);
        Dispatcher.UIThread.RunJobs();

        var countText = window.FindControl<TextBlock>("DictionaryCountText");
        countText.ShouldNotBeNull();
        countText.Text.ShouldNotBeNull();
        countText.Text.ShouldContain("2");
    }

    [AvaloniaFact]
    public void DictionaryTab_CompositionAndEditor_FollowsLensContracts()
    {
        var window = new SettingsWindow();
        window.SetDictionary([]);
        Dispatcher.UIThread.RunJobs();

        var tab = window.FindControl<TabItem>("DictionaryTab");
        tab.ShouldNotBeNull();

        var addBtn = window.FindControl<Button>("AddDictionaryButton");
        addBtn.ShouldNotBeNull();

        var searchBox = window.FindControl<TextBox>("DictionarySearchTextBox");
        searchBox.ShouldNotBeNull();

        var editorPanel = window.FindControl<Border>("DictionaryEditorPanel");
        editorPanel.ShouldNotBeNull();
        editorPanel.IsVisible.ShouldBeFalse();

        var emptyText = window.FindControl<TextBlock>("DictionaryEmptyText");
        emptyText.ShouldNotBeNull();
        emptyText.IsVisible.ShouldBeTrue();

        var kindCombo = window.FindControl<ComboBox>("DictionaryKindComboBox");
        kindCombo.ShouldNotBeNull();

        var hearBox = window.FindControl<TextBox>("DictionaryHearTextBox");
        hearBox.ShouldNotBeNull();

        var writeBox = window.FindControl<TextBox>("DictionaryWriteTextBox");
        writeBox.ShouldNotBeNull();

        var countText = window.FindControl<TextBlock>("DictionaryCountText");
        countText.ShouldNotBeNull();
    }

    [AvaloniaFact]
    public void IncompleteSetup_NeverExposesTextReady()
    {
        var window = new SettingsWindow();
        window.SetDevices([], null);
        Dispatcher.UIThread.RunJobs();

        var statusText = window.FindControl<TextBlock>("StatusText");
        statusText.ShouldNotBeNull();
        statusText.Text.ShouldNotBe("Ready");
    }

    [AvaloniaFact]
    public void CompleteSetup_ShowsInstalledUntilModelLoaded()
    {
        var window = new SettingsWindow();
        window.SetDevices([new AudioInputDevice("dev-1", "Default Mic", true)], "dev-1");
        window.SetChoices(AppSettings.Default with { SpeechModelId = "parakeet-v3", HotkeyId = "vk:162" });
        window.SetModelProgress(1.0);
        Dispatcher.UIThread.RunJobs();

        var statusText = window.FindControl<TextBlock>("StatusText");
        statusText.ShouldNotBeNull();
        statusText.Text.ShouldBe(UiText.Text("StatusModelInstalled"));

        window.SetModelLoaded("parakeet-v3");
        window.SetModelProgress(1.0);
        Dispatcher.UIThread.RunJobs();
        statusText.Text.ShouldBe("Ready");
    }

    [AvaloniaFact]
    public void FirstRun_PrimaryActionReadsStartMurmur_SteadyStateReadsApply()
    {
        var window = new SettingsWindow();
        window.SetChoices(AppSettings.Default with { SetupCompleted = false }, isFirstRun: true);
        Dispatcher.UIThread.RunJobs();

        var saveButton = window.FindControl<Button>("SaveButton");
        saveButton.ShouldNotBeNull();
        saveButton.Content.ShouldBe("Start Murmur");

        window.SetChoices(AppSettings.Default with { SetupCompleted = true }, isFirstRun: false);
        Dispatcher.UIThread.RunJobs();
        saveButton.Content.ShouldBe("Apply");
    }

    [AvaloniaFact]
    public void StatusAndProgressBar_HaveNonOffLiveSetting()
    {
        var window = new SettingsWindow();
        var statusText = window.FindControl<TextBlock>("StatusText");
        statusText.ShouldNotBeNull();
        AutomationProperties.GetLiveSetting(statusText).ShouldNotBe(AutomationLiveSetting.Off);

        var progressBar = window.FindControl<ProgressBar>("ModelProgressBar");
        progressBar.ShouldNotBeNull();
        AutomationProperties.GetLiveSetting(progressBar).ShouldNotBe(AutomationLiveSetting.Off);
    }

    [AvaloniaFact]
    public void SetupPermutations_MissingMicrophone_DisablesSaveAndPreferences()
    {
        var window = new SettingsWindow();
        window.SetDevices([], null);
        window.SetChoices(AppSettings.Default with { SpeechModelId = "parakeet-v3", HotkeyId = "vk:162" });
        Dispatcher.UIThread.RunJobs();

        var saveButton = window.FindControl<Button>("SaveButton");
        saveButton.ShouldNotBeNull();
        saveButton.IsEnabled.ShouldBeFalse();

        var preferences = window.FindControl<Border>("PreferencesPanel");
        preferences.ShouldNotBeNull();
        preferences.IsEnabled.ShouldBeFalse();

        var statusText = window.FindControl<TextBlock>("StatusText");
        statusText.ShouldNotBeNull();
        statusText.Text.ShouldNotBeNull();
        statusText.Text.ShouldContain("Microphone", Case.Insensitive);
    }

    [AvaloniaFact]
    public void SetupPermutations_Downloading_DisablesSaveAndShowsCancel()
    {
        var window = new SettingsWindow();
        window.SetDevices([new AudioInputDevice("dev-1", "Default Mic", true)], "dev-1");
        window.SetChoices(AppSettings.Default with { SpeechModelId = "parakeet-v3", HotkeyId = "vk:162" });
        window.SetModelProgress(0.42);
        Dispatcher.UIThread.RunJobs();

        var saveButton = window.FindControl<Button>("SaveButton");
        saveButton.ShouldNotBeNull();
        saveButton.IsEnabled.ShouldBeFalse();

        var cancelBtn = window.FindControl<Button>("CancelDownloadButton");
        cancelBtn.ShouldNotBeNull();
        cancelBtn.IsVisible.ShouldBeTrue();
        cancelBtn.IsEnabled.ShouldBeTrue();

        var statusText = window.FindControl<TextBlock>("StatusText");
        statusText.ShouldNotBeNull();
        statusText.Text.ShouldNotBeNull();
        statusText.Text.ShouldContain("Downloading", Case.Insensitive);
    }

    [AvaloniaFact]
    public void SetupPermutations_AllConfigured_EnablesSaveAndPreferences()
    {
        var window = new SettingsWindow();
        window.SetDevices([new AudioInputDevice("dev-1", "Default Mic", true)], "dev-1");
        window.SetChoices(AppSettings.Default with { SpeechModelId = "parakeet-v3", HotkeyId = "vk:162" });
        window.SetModelProgress(1.0);
        Dispatcher.UIThread.RunJobs();

        var saveButton = window.FindControl<Button>("SaveButton");
        saveButton.ShouldNotBeNull();
        saveButton.IsEnabled.ShouldBeTrue();

        var preferences = window.FindControl<Border>("PreferencesPanel");
        preferences.ShouldNotBeNull();
        preferences.IsEnabled.ShouldBeTrue();
        window.FindControl<ToggleSwitch>("AudioFeedbackCheckBox").ShouldNotBeNull();

        var statusText = window.FindControl<TextBlock>("StatusText");
        statusText.ShouldNotBeNull();
        statusText.Text.ShouldBe(UiText.Text("StatusModelInstalled"));
    }

    [AvaloniaFact]
    public void DownloadCancel_RaisesEventOnceAndDisablesButton()
    {
        var window = new SettingsWindow();
        var cancelCount = 0;
        window.DownloadCancelRequested += (s, e) => cancelCount++;

        window.SetModelProgress(0.5);
        Dispatcher.UIThread.RunJobs();

        var cancelBtn = window.FindControl<Button>("CancelDownloadButton");
        cancelBtn.ShouldNotBeNull();
        cancelBtn.IsEnabled.ShouldBeTrue();

        // Simulate click
        cancelBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        cancelCount.ShouldBe(1);
        cancelBtn.IsEnabled.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void DeleteAllHistory_CancelDoesNotRaiseEvent_ConfirmRaisesOnce()
    {
        var window = new SettingsWindow();
        var clearCount = 0;
        window.HistoryClearRequested += () => clearCount++;

        var deleteAllBtn = window.FindControl<Button>("DeleteAllHistoryButton");
        deleteAllBtn.ShouldNotBeNull();
        var confirmPanel = window.FindControl<Border>("DeleteAllConfirmationPanel");
        confirmPanel.ShouldNotBeNull();
        confirmPanel.IsVisible.ShouldBeFalse();

        // Click delete all
        deleteAllBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        confirmPanel.IsVisible.ShouldBeTrue();
        clearCount.ShouldBe(0);

        // Click cancel
        var cancelBtn = window.FindControl<Button>("CancelDeleteAllButton");
        cancelBtn.ShouldNotBeNull();
        cancelBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        confirmPanel.IsVisible.ShouldBeFalse();
        clearCount.ShouldBe(0);

        // Click delete all again, then confirm
        deleteAllBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        confirmPanel.IsVisible.ShouldBeTrue();

        var confirmBtn = window.FindControl<Button>("ConfirmDeleteAllButton");
        confirmBtn.ShouldNotBeNull();
        confirmBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        confirmPanel.IsVisible.ShouldBeFalse();
        clearCount.ShouldBe(1);
    }

    [Fact]
    public void FilterHistory_PureFiltering_BehavesCorrectly()
    {
        var records = new List<DictationHistoryEntry>
        {
            new(Guid.NewGuid(), DateTimeOffset.UtcNow, "Parakeet", 1000, 200, "Hello World"),
            new(Guid.NewGuid(), DateTimeOffset.UtcNow, "Parakeet", 1500, 250, "Testing speech to text"),
            new(Guid.NewGuid(), DateTimeOffset.UtcNow, "Nemotron", 2000, 300, "Another dictation sample")
        };

        // Empty query returns all
        SettingsWindow.FilterHistory(records, null).Count.ShouldBe(3);
        SettingsWindow.FilterHistory(records, "").Count.ShouldBe(3);
        SettingsWindow.FilterHistory(records, "   ").Count.ShouldBe(3);

        // Case-insensitive match
        var match = SettingsWindow.FilterHistory(records, "hello");
        match.Count.ShouldBe(1);
        match[0].Text.ShouldBe("Hello World");

        // No match
        SettingsWindow.FilterHistory(records, "nonexistent").Count.ShouldBe(0);

        // Long transcript match
        var longText = new string('a', 5000) + " target keyword " + new string('b', 5000);
        var longRecords = new List<DictationHistoryEntry>
        {
            new(Guid.NewGuid(), DateTimeOffset.UtcNow, "Parakeet", 5000, 400, longText)
        };
        var longMatch = SettingsWindow.FilterHistory(longRecords, "TARGET KEYWORD");
        longMatch.Count.ShouldBe(1);
        longMatch[0].Text.ShouldBe(longText);

        // Source list unchanged
        records.Count.ShouldBe(3);
        longRecords.Count.ShouldBe(1);
    }

    [Fact]
    public void FilterHistory_TenThousandRecords_PerformsUnder100Ms()
    {
        var records = Enumerable.Range(0, 10000)
            .Select(i => new DictationHistoryEntry(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                "Parakeet",
                1000 + i,
                200,
                $"Dictation record number {i} with some transcript text content."))
            .ToList();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var filtered = SettingsWindow.FilterHistory(records, "number 500");
        sw.Stop();

        filtered.Count.ShouldBeGreaterThanOrEqualTo(1);
        // Note: measured in memory filter time
        sw.ElapsedMilliseconds.ShouldBeLessThan(100);
    }

    [Fact]
    public void Axaml_ContainsNoGradientsPurpleOrIllegalRed()
    {
        var appDir = Path.Combine(Directory.GetCurrentDirectory(), "windows", "src", "Murmur.App");
        if (!Directory.Exists(appDir))
        {
            var baseDir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(baseDir) && !Directory.Exists(Path.Combine(baseDir, "windows", "src", "Murmur.App")))
            {
                var parent = Directory.GetParent(baseDir);
                if (parent == null) break;
                baseDir = parent.FullName;
            }
            appDir = Path.Combine(baseDir, "windows", "src", "Murmur.App");
        }

        var axamlFiles = Directory.GetFiles(appDir, "*.axaml", SearchOption.AllDirectories);
        axamlFiles.Length.ShouldBeGreaterThan(0);

        string[] purplePinkKeywords = ["#FF00FF", "#800080", "#8A2BE2", "Magenta", "Purple", "Fuchsia", "#D8005A", "#B00040", "#5C0020"];
        string[] illegalRedFallbacks = ["#DC2626", "#EF4444", "#B91C1C", "#991B1B"];

        foreach (var file in axamlFiles)
        {
            var content = File.ReadAllText(file);
            var relativeName = Path.GetRelativePath(appDir, file);

            if (relativeName.Equals("Styles\\MurmurTokens.axaml", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var key in new[] { "LensPrimaryBrush", "LensProgressBrush", "LensSpecularBrush" })
                {
                    content.ShouldContain($"x:Key=\"{key}\"");
                }

                content.Split("<LinearGradientBrush", StringSplitOptions.None).Length.ShouldBe(7);
            }
            else
            {
                content.ShouldNotContain("LinearGradientBrush", customMessage: $"{relativeName} must not contain LinearGradientBrush");
            }
            content.ShouldNotContain("RadialGradientBrush", customMessage: $"{relativeName} must not contain RadialGradientBrush");

            foreach (var kw in purplePinkKeywords)
            {
                content.ShouldNotContain(kw, customMessage: $"{relativeName} contains purple/pink '{kw}'");
            }

            foreach (var red in illegalRedFallbacks)
            {
                content.ShouldNotContain(red, customMessage: $"{relativeName} contains non-standard red '{red}'");
            }
        }
    }

    [AvaloniaFact]
    public void SetChoices_ReflectsAudioFeedbackSetting()
    {
        var window = new SettingsWindow();

        window.SetChoices(AppSettings.Default with { AudioFeedbackEnabled = true });
        Dispatcher.UIThread.RunJobs();

        window.FindControl<ToggleSwitch>("AudioFeedbackCheckBox")!.IsChecked.ShouldBe(true);
    }

    [AvaloniaFact]
    public void LensTokens_ResolvesAllLensResources()
    {
        var window = new SettingsWindow();
        var theme = window.ActualThemeVariant;

        string[] brushKeys =
        [
            "LensChassisBrush",
            "LensSolidFallbackBrush",
            "LensPanelBrush",
            "LensFieldBrush",
            "LensWellBrush",
            "LensHairlineBrush",
            "LensSpecularBrush",
            "LensInkPrimaryBrush",
            "LensInkSecondaryBrush",
            "LensSelectionBrush",
            "LensFocusBrush",
            "LensRecordBrush",
            "LensMeterGreenBrush",
            "LensMeterAmberBrush",
            "LensMeterPeakBrush",
            "LensMeterUnlitBrush",
            "LensPrimaryBrush",
            "LensProgressBrush"
        ];

        foreach (var key in brushKeys)
        {
            window.TryFindResource(key, theme, out var res).ShouldBeTrue($"Resource {key} should resolve");
            res.ShouldBeAssignableTo<IBrush>($"Resource {key} should be IBrush");
        }

        foreach (var key in new[] { "LensPrimaryBrush", "LensProgressBrush", "LensSpecularBrush" })
        {
            window.TryFindResource(key, theme, out var res).ShouldBeTrue($"Resource {key} should resolve");
            res.ShouldBeAssignableTo<LinearGradientBrush>($"Resource {key} should be a named gradient");
        }

        window.TryFindResource("SettingsWindowChromeHeight", theme, out var chromeH).ShouldBeTrue();
        chromeH.ShouldBeOfType<double>();

        window.TryFindResource("SettingsMeterHeight", theme, out var meterH).ShouldBeTrue();
        ((double)meterH!).ShouldBe(30.0);

        window.TryFindResource("SettingsMeterPaddingX", theme, out var padX).ShouldBeTrue();
        padX.ShouldBeOfType<double>();

        window.TryFindResource("SettingsMeterPaddingY", theme, out var padY).ShouldBeTrue();
        padY.ShouldBeOfType<double>();

        window.TryFindResource("SettingsMeterSegmentGap", theme, out var gap).ShouldBeTrue();
        ((double)gap!).ShouldBe(3.0);

        window.TryFindResource("SettingsMeterSegmentRadius", theme, out var segRad).ShouldBeTrue();
        ((double)segRad!).ShouldBe(2.0);

        window.TryFindResource("SettingsMeterWellRadius", theme, out var wellRad).ShouldBeTrue();
        wellRad.ShouldBeOfType<CornerRadius>();
    }

    [AvaloniaFact]
    public void LensTokens_ResolvesRequiredBrushesAndOrangePeak()
    {
        var window = new SettingsWindow();
        var theme = window.ActualThemeVariant;

        window.TryFindResource("LensRecordBrush", theme, out var recordRes).ShouldBeTrue();
        var recordBrush = recordRes.ShouldBeAssignableTo<ISolidColorBrush>();
        recordBrush.ShouldNotBeNull();
        var expectedRecord = theme == ThemeVariant.Light ? "#FFFF3B30" : "#FFFF453A";
        recordBrush.Color.ToString().ToUpperInvariant().ShouldBe(expectedRecord);

        window.TryFindResource("LensMeterPeakBrush", theme, out var peakRes).ShouldBeTrue();
        var peakBrush = peakRes.ShouldBeAssignableTo<ISolidColorBrush>();
        peakBrush.ShouldNotBeNull();
        var expectedPeak = theme == ThemeVariant.Light ? "#FFC25E00" : "#FFFF9F0A";
        peakBrush.Color.ToString().ToUpperInvariant().ShouldBe(expectedPeak);

        window.TryFindResource("LensWellBrush", theme, out var wellRes).ShouldBeTrue();
        wellRes.ShouldBeAssignableTo<IBrush>().ShouldNotBeNull();
    }

    [AvaloniaFact]
    public void StatusIndicators_ReflectReadinessState()
    {
        var window = new SettingsWindow();
        var counterText = window.FindControl<TextBlock>("StatusCounterBadgeText");
        counterText.ShouldNotBeNull();

        var micDot = window.FindControl<Avalonia.Controls.Shapes.Ellipse>("MicStatusDot");
        micDot.ShouldNotBeNull();
        var modelDot = window.FindControl<Avalonia.Controls.Shapes.Ellipse>("ModelStatusDot");
        modelDot.ShouldNotBeNull();
        var hotkeyDot = window.FindControl<Avalonia.Controls.Shapes.Ellipse>("HotkeyStatusDot");
        hotkeyDot.ShouldNotBeNull();

        // Incomplete state initially
        window.SetDevices([], null);
        window.SetChoices(AppSettings.Default with { SpeechModelId = "parakeet-v3", HotkeyId = "" });
        window.SetModelProgress(0.0);
        Dispatcher.UIThread.RunJobs();

        counterText.Text.ShouldNotBe("3/3");

        // Complete state
        window.SetDevices([new AudioInputDevice("dev-1", "Default Mic", true)], "dev-1");
        window.SetChoices(AppSettings.Default with { SpeechModelId = "parakeet-v3", HotkeyId = "vk:162" });
        window.SetModelProgress(1.0);
        Dispatcher.UIThread.RunJobs();

        counterText.Text.ShouldBe("3/3");
    }

    [AvaloniaFact]
    public void SetChoices_PopulatesModelHotkeyAndLanguage()
    {
        var window = new SettingsWindow();
        var modelCombo = window.FindControl<ComboBox>("SpeechModelComboBox");
        var languageCombo = window.FindControl<ComboBox>("LanguageComboBox");
        var hotkeyBox = window.FindControl<TextBox>("HotkeyTextBox");

        modelCombo.ShouldNotBeNull();
        languageCombo.ShouldNotBeNull();
        hotkeyBox.ShouldNotBeNull();

        window.SetChoices(AppSettings.Default with { SpeechModelId = "parakeet-v3", Language = "en", HotkeyId = "vk:162" });
        Dispatcher.UIThread.RunJobs();

        modelCombo.SelectedIndex.ShouldBeGreaterThanOrEqualTo(0);
        languageCombo.SelectedIndex.ShouldBe(0); // English
        hotkeyBox.Text.ShouldNotBeNullOrWhiteSpace();
    }

    [AvaloniaFact]
    public void SetChoices_PopulatesTextInsertionMode_AndSaveRequest()
    {
        var window = new SettingsWindow();
        window.SetDevices([new AudioInputDevice("dev-1", "Default Mic", true)], "dev-1");
        window.SetChoices(AppSettings.Default with
        {
            SpeechModelId = "parakeet-v3",
            HotkeyId = "vk:162",
            TextInsertionMode = TextInsertionMode.DocumentEnd
        });
        window.SetModelProgress(1.0);
        Dispatcher.UIThread.RunJobs();

        var modeCombo = window.FindControl<ComboBox>("TextInsertionModeComboBox");
        modeCombo.ShouldNotBeNull();
        modeCombo.SelectedIndex.ShouldBe(1);
        modeCombo.Items.Cast<string>().ShouldContain(UiText.AtCurrentCursor);
        modeCombo.Items.Cast<string>().ShouldContain(UiText.AtEndOfDocument);

        SettingsSaveRequest? saved = null;
        window.SaveRequested += request => saved = request;
        var saveButton = window.FindControl<Button>("SaveButton");
        saveButton.ShouldNotBeNull();
        saveButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        saved.ShouldNotBeNull();
        saved!.TextInsertionMode.ShouldBe(TextInsertionMode.DocumentEnd);
    }

    [AvaloniaFact]
    public void SetChoices_PopulatesHistoryPolicy_AndSaveRequest()
    {
        var window = new SettingsWindow();
        window.SetDevices([new AudioInputDevice("dev-1", "Default Mic", true)], "dev-1");
        window.SetChoices(AppSettings.Default with
        {
            SpeechModelId = "parakeet-v3",
            HotkeyId = "vk:162",
            HistoryEnabled = true,
            HistoryRetentionDays = 7,
            HistoryMaxEntries = 0
        });
        window.SetModelProgress(1.0);
        Dispatcher.UIThread.RunJobs();

        var enabled = window.FindControl<ToggleSwitch>("HistoryEnabledCheckBox");
        var retention = window.FindControl<ComboBox>("HistoryRetentionComboBox");
        var maxEntries = window.FindControl<ComboBox>("HistoryMaxEntriesComboBox");
        enabled.ShouldNotBeNull();
        retention.ShouldNotBeNull();
        maxEntries.ShouldNotBeNull();

        enabled.IsChecked.ShouldBe(true);
        retention.SelectedIndex.ShouldBe(1);
        maxEntries.SelectedIndex.ShouldBe(0);

        SettingsSaveRequest? saved = null;
        window.SaveRequested += request => saved = request;
        window.FindControl<Button>("SaveButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        saved.ShouldNotBeNull();
        saved!.HistoryEnabled.ShouldBeTrue();
        saved.HistoryRetentionDays.ShouldBe(7);
        saved.HistoryMaxEntries.ShouldBe(0);
    }

    [AvaloniaFact]
    public void RecoveryTranscript_Shows_AndExplicitDiscardClearsIt()
    {
        var window = new SettingsWindow();
        var panel = window.FindControl<Border>("RecoveryTranscriptPanel");
        var text = window.FindControl<TextBox>("RecoveryTranscriptText");
        var discard = window.FindControl<Button>("DiscardRecoveryTranscriptButton");

        panel.ShouldNotBeNull();
        text.ShouldNotBeNull();
        discard.ShouldNotBeNull();

        var cleared = false;
        window.RecoveryTranscriptCleared += () => cleared = true;
        window.SetRecoveryTranscript("recover this text");
        Dispatcher.UIThread.RunJobs();

        panel.IsVisible.ShouldBeTrue();
        text.Text.ShouldBe("recover this text");
        text.IsReadOnly.ShouldBeTrue();

        discard.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        cleared.ShouldBeTrue();
        panel.IsVisible.ShouldBeFalse();
        text.Text.ShouldBe(string.Empty);
    }

    [AvaloniaFact]
    public void DictationProfile_Math_SwitchesOutputModeAndControls()
    {
        var window = new SettingsWindow();
        window.SetDevices([new AudioInputDevice("dev-1", "Default Mic", true)], "dev-1");
        window.SetChoices(AppSettings.Default with
        {
            SpeechModelId = "parakeet-v3",
            HotkeyId = "vk:162",
            ActiveProfileId = "math"
        });
        window.SetModelProgress(1.0);
        Dispatcher.UIThread.RunJobs();

        SettingsSaveRequest? savedRequest = null;
        window.SaveRequested += req => savedRequest = req;

        var mathModeCombo = window.FindControl<ComboBox>("MathModeComboBox");
        var removeFillersCheck = window.FindControl<ToggleSwitch>("RemoveFillersCheckBox");
        var saveButton = window.FindControl<Button>("SaveButton");

        mathModeCombo.ShouldNotBeNull();
        removeFillersCheck.ShouldNotBeNull();
        saveButton.ShouldNotBeNull();

        // Change MathMode to LaTeX (index 1) and uncheck remove fillers
        mathModeCombo.SelectedIndex = 1;
        removeFillersCheck.IsChecked = false;
        Dispatcher.UIThread.RunJobs();

        saveButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        savedRequest.ShouldNotBeNull();
        savedRequest!.ActiveProfileId.ShouldBe("math");
        savedRequest.RemoveFillers.ShouldBeFalse();
        savedRequest.Profiles.ShouldNotBeNull();
        savedRequest.Profiles!.Count.ShouldBe(2);

        var mathProf = savedRequest.Profiles.First(p => p.Id == "math");
        mathProf.MathMode.ShouldBe(MathFormatMode.Latex);
        mathProf.RemoveFillers.ShouldBeFalse();

        // Standard profile retained its own defaults
        var stdProf = savedRequest.Profiles.First(p => p.Id == "standard");
        stdProf.RemoveFillers.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void SettingsWindow_UsesNativeLensShell()
    {
        var window = new SettingsWindow();

        window.FindControl<Border>("SettingsSurface").ShouldNotBeNull();
        window.FindControl<Grid>("WindowChrome").ShouldNotBeNull();
        window.FindControl<Button>("CloseButton").ShouldNotBeNull();
        window.FindControl<Button>("MinimizeButton").ShouldNotBeNull();
        window.FindControl<Button>("MaximizeButton").ShouldNotBeNull();
    }

    [Fact]
    public void FeedbackSound_IsNotBundled()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Murmur.sln")))
        {
            dir = dir.Parent;
        }
        dir.ShouldNotBeNull("Solution root directory should be found");

        var soundsDir = Path.Combine(dir.FullName, "src", "Murmur.App", "Assets", "Sounds");
        foreach (var fileName in new[] { "minimal-digital-click.wav", "feedback.wav" })
        {
            var path = Path.Combine(soundsDir, fileName);
            File.Exists(path).ShouldBeFalse($"Sound asset must not exist at {path}");
        }
    }

    [Fact]
    public void ClickSoundPlayer_AbsentFile_IsSilent()
    {
        using var player = new ClickSoundPlayer();

        player.Play(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.wav"));
    }

    [AvaloniaFact]
    public void SetMicrophoneLevel_FormatsDecibelsCorrectly()
    {
        var window = new SettingsWindow();
        var dbText = window.FindControl<TextBlock>("MicrophoneDbText");
        dbText.ShouldNotBeNull();

        window.SetMicrophoneLevel(0f);
        Dispatcher.UIThread.RunJobs();
        (dbText.Text == "−∞ dB" || dbText.Text == "-∞ dB").ShouldBeTrue($"Actual: {dbText.Text}");

        window.SetMicrophoneLevel(1f);
        Dispatcher.UIThread.RunJobs();
        dbText.Text.ShouldBe("0 dB");

        window.SetMicrophoneLevel(0.5f);
        Dispatcher.UIThread.RunJobs();
        (dbText.Text == "-25 dB" || dbText.Text == "−25 dB").ShouldBeTrue($"Actual: {dbText.Text}");
    }

    [AvaloniaFact]
    public void ReadinessIndicators_UpdateDotsAndCounter()
    {
        var window = new SettingsWindow();
        var counterText = window.FindControl<TextBlock>("StatusCounterBadgeText");
        counterText.ShouldNotBeNull();

        var micDot = window.FindControl<Avalonia.Controls.Shapes.Ellipse>("MicStatusDot");
        micDot.ShouldNotBeNull();
        var modelDot = window.FindControl<Avalonia.Controls.Shapes.Ellipse>("ModelStatusDot");
        modelDot.ShouldNotBeNull();
        var hotkeyDot = window.FindControl<Avalonia.Controls.Shapes.Ellipse>("HotkeyStatusDot");
        hotkeyDot.ShouldNotBeNull();

        // Incomplete state initially
        window.SetDevices([], null);
        window.SetChoices(AppSettings.Default with { SpeechModelId = "parakeet-v3", HotkeyId = "" });
        window.SetModelProgress(0.0);
        Dispatcher.UIThread.RunJobs();

        counterText.Text.ShouldNotBe("3/3");

        // Complete state
        window.SetDevices([new AudioInputDevice("dev-1", "Default Mic", true)], "dev-1");
        window.SetChoices(AppSettings.Default with { SpeechModelId = "parakeet-v3", HotkeyId = "vk:162" });
        window.SetModelProgress(1.0);
        Dispatcher.UIThread.RunJobs();

        counterText.Text.ShouldBe("3/3");
    }

    [AvaloniaFact]
    public void SetChoices_ShowsTwoProfileChoices_AndSelectsActiveProfile()
    {
        var window = new SettingsWindow();
        var profileCombo = window.FindControl<ComboBox>("ProfileComboBox");
        var mathModeCombo = window.FindControl<ComboBox>("MathModeComboBox");
        var mathModePanel = window.FindControl<Grid>("MathModePanel");
        var descText = window.FindControl<TextBlock>("ProfileDescriptionText");

        profileCombo.ShouldNotBeNull();
        mathModeCombo.ShouldNotBeNull();
        descText.ShouldNotBeNull();

        // 1. Standard active
        window.SetChoices(AppSettings.Default with { ActiveProfileId = "standard" });
        Dispatcher.UIThread.RunJobs();

        profileCombo.ItemCount.ShouldBe(2);
        profileCombo.SelectedIndex.ShouldBe(0);
        mathModePanel!.IsVisible.ShouldBeFalse();
        descText.Text.ShouldBe(UiText.ProfileDescriptionStandard);

        // 2. Math active
        window.SetChoices(AppSettings.Default with { ActiveProfileId = "math" });
        Dispatcher.UIThread.RunJobs();

        profileCombo.SelectedIndex.ShouldBe(1);
        mathModePanel.IsVisible.ShouldBeTrue();
        descText.Text.ShouldBe(UiText.ProfileDescriptionMath);
    }

    [AvaloniaFact]
    public void SelectingProfile_TogglesMathModeVisibility_AndUpdatesDescription()
    {
        var window = new SettingsWindow();
        window.SetChoices(AppSettings.Default with { ActiveProfileId = "standard" });
        Dispatcher.UIThread.RunJobs();

        var profileCombo = window.FindControl<ComboBox>("ProfileComboBox");
        var mathModePanel = window.FindControl<Grid>("MathModePanel");
        var descText = window.FindControl<TextBlock>("ProfileDescriptionText");

        profileCombo.ShouldNotBeNull();
        mathModePanel.ShouldNotBeNull();
        descText.ShouldNotBeNull();

        // Switch to Math
        profileCombo.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        mathModePanel.IsVisible.ShouldBeTrue();
        descText.Text.ShouldBe(UiText.ProfileDescriptionMath);

        // Switch back to Standard
        profileCombo.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();

        mathModePanel.IsVisible.ShouldBeFalse();
        descText.Text.ShouldBe(UiText.ProfileDescriptionStandard);
    }

    [AvaloniaFact]
    public void ChangingMathModeAndRemoveFillers_ReflectedInSaveRequest()
    {
        var window = new SettingsWindow();
        window.SetDevices([new AudioInputDevice("dev-1", "Default Mic", true)], "dev-1");
        window.SetChoices(AppSettings.Default with
        {
            SpeechModelId = "parakeet-v3",
            HotkeyId = "vk:162",
            ActiveProfileId = "math"
        });
        window.SetModelProgress(1.0);
        Dispatcher.UIThread.RunJobs();

        SettingsSaveRequest? savedRequest = null;
        window.SaveRequested += req => savedRequest = req;

        var mathModeCombo = window.FindControl<ComboBox>("MathModeComboBox");
        var removeFillersCheck = window.FindControl<ToggleSwitch>("RemoveFillersCheckBox");
        var saveButton = window.FindControl<Button>("SaveButton");

        mathModeCombo.ShouldNotBeNull();
        removeFillersCheck.ShouldNotBeNull();
        saveButton.ShouldNotBeNull();

        // Change MathMode to LaTeX (index 1) and uncheck remove fillers
        mathModeCombo.SelectedIndex = 1;
        removeFillersCheck.IsChecked = false;
        Dispatcher.UIThread.RunJobs();

        saveButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        savedRequest.ShouldNotBeNull();
        savedRequest!.ActiveProfileId.ShouldBe("math");
        savedRequest.RemoveFillers.ShouldBeFalse();
        savedRequest.Profiles.ShouldNotBeNull();
        savedRequest.Profiles!.Count.ShouldBe(2);

        var mathProf = savedRequest.Profiles.First(p => p.Id == "math");
        mathProf.MathMode.ShouldBe(MathFormatMode.Latex);
        mathProf.RemoveFillers.ShouldBeFalse();

        // Standard profile retained its own defaults
        var stdProf = savedRequest.Profiles.First(p => p.Id == "standard");
        stdProf.RemoveFillers.ShouldBeTrue();
    }

    [Fact]
    public void Profile_Labels_Resolve_In_English_And_German()
    {
        UiText.SetLanguage("en");
        UiText.Profile.ShouldBe("Profile");
        UiText.ProfileStandard.ShouldBe("Standard");
        UiText.ProfileMath.ShouldBe("Math & Exams");
        UiText.MathOutputMode.ShouldBe("Math Output");

        UiText.SetLanguage("de");
        UiText.Profile.ShouldBe("Profil");
        UiText.ProfileStandard.ShouldBe("Standard");
        UiText.ProfileMath.ShouldBe("Mathe & Klausuren");
        UiText.MathOutputMode.ShouldBe("Mathe-Ausgabe");

        // Reset to English
        UiText.SetLanguage("en");
    }

    [AvaloniaFact]
    public void SettingsWindow_InterfaceLanguage_EmitsCorrectSaveRequest()
    {
        var window = new SettingsWindow();
        window.SetDevices([new AudioInputDevice("dev-1", "Default Mic", true)], "dev-1");
        window.SetChoices(AppSettings.Default with
        {
            SpeechModelId = "parakeet-v3",
            HotkeyId = "vk:162",
            Language = "en"
        });
        window.SetModelProgress(1.0);
        Dispatcher.UIThread.RunJobs();

        SettingsSaveRequest? req = null;
        window.SaveRequested += r => req = r;

        var languageCombo = window.FindControl<ComboBox>("LanguageComboBox");
        languageCombo.ShouldNotBeNull();
        languageCombo.SelectedIndex = 1; // Deutsch
        Dispatcher.UIThread.RunJobs();

        var saveButton = window.FindControl<Button>("SaveButton");
        saveButton.ShouldNotBeNull();
        saveButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        req.ShouldNotBeNull();
        req!.Language.ShouldBe(UiLanguage.German);
    }

    [Fact]
    public void HistoryDurationConverters_LocalizeInEnglishAndGerman()
    {
        UiText.SetLanguage(UiLanguage.English);
        HistoryAudioDurationConverter.Instance.Convert(1500.0, typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBe("1500 ms audio");
        HistoryInferenceDurationConverter.Instance.Convert(250.0, typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBe("250 ms inference");

        UiText.SetLanguage(UiLanguage.German);
        HistoryAudioDurationConverter.Instance.Convert(1500.0, typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBe("1500 ms Audio");
        HistoryInferenceDurationConverter.Instance.Convert(250.0, typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBe("250 ms Inferenz");

        UiText.SetLanguage(UiLanguage.English);
    }

    [AvaloniaFact]
    public void SettingsWindow_HistoryMetadata_LocalizedInVisualTree()
    {
        try
        {
            UiText.SetLanguage(UiLanguage.German);
            var window = new SettingsWindow();
            var historyTab = window.FindControl<TabItem>("HistoryTab")!;
            historyTab.IsSelected = true;

            window.SetHistory([
                new DictationHistoryEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, "Parakeet", 1200, 300, "Deutscher Testtext")
            ]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var textBlocks = window.GetVisualDescendants().OfType<TextBlock>().ToList();
            var metadataTexts = textBlocks.Select(tb => tb.Text ?? "").ToList();

            metadataTexts.ShouldContain(t => t.Contains("1200 ms Audio"));
            metadataTexts.ShouldContain(t => t.Contains("300 ms Inferenz"));
            metadataTexts.ShouldNotContain(t => t.Contains("ms audio"));
            metadataTexts.ShouldNotContain(t => t.Contains("ms infer"));

            window.Close();

            // English check
            UiText.SetLanguage(UiLanguage.English);
            var enWindow = new SettingsWindow();
            var enHistoryTab = enWindow.FindControl<TabItem>("HistoryTab")!;
            enHistoryTab.IsSelected = true;

            enWindow.SetHistory([
                new DictationHistoryEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, "Parakeet", 1200, 300, "English test text")
            ]);
            enWindow.Show();
            Dispatcher.UIThread.RunJobs();

            var enTextBlocks = enWindow.GetVisualDescendants().OfType<TextBlock>().ToList();
            var enMetadataTexts = enTextBlocks.Select(tb => tb.Text ?? "").ToList();

            enMetadataTexts.ShouldContain(t => t.Contains("1200 ms audio"));
            enMetadataTexts.ShouldContain(t => t.Contains("300 ms inference"));

            enWindow.Close();
        }
        finally
        {
            UiText.SetLanguage(UiLanguage.English);
        }
    }

    [AvaloniaFact]
    public void SettingsWindow_ContainsSingleSetupNote()
    {
        var window = new SettingsWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var setupNotes = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(tb => tb.IsVisible && tb.Text == UiText.SetupNote)
            .ToList();

        setupNotes.Count.ShouldBe(1);
        window.Close();
    }

    [AvaloniaFact]
    public void SettingsWindow_HistoryCards_ResilientAtMinWidth()
    {
        var window = new SettingsWindow
        {
            Width = 580,
            Height = 600
        };
        var historyTab = window.FindControl<TabItem>("HistoryTab")!;
        historyTab.IsSelected = true;

        window.SetHistory([
            new DictationHistoryEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, "Parakeet", 1200, 300, "Test transcript text for min width layout check.")
        ]);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var historyList = window.FindControl<ListBox>("HistoryListBox");
        historyList.ShouldNotBeNull();
        historyList.ItemCount.ShouldBe(1);

        var card = window.GetVisualDescendants()
            .OfType<Border>()
            .First(b => b.Classes.Contains("lensCard"));

        var buttons = card.GetVisualDescendants().OfType<Button>().ToList();
        var copyBtn = buttons.FirstOrDefault(b => b.Content?.ToString() == UiText.Copy);
        var deleteBtn = buttons.FirstOrDefault(b => b.Content?.ToString() == UiText.Delete);

        copyBtn.ShouldNotBeNull();
        deleteBtn.ShouldNotBeNull();
        copyBtn.IsVisible.ShouldBeTrue();
        deleteBtn.IsVisible.ShouldBeTrue();

        var textBlocks = card.GetVisualDescendants().OfType<TextBlock>().ToList();
        textBlocks.ShouldContain(tb => tb.Text != null && tb.Text.Contains("Parakeet"));
        textBlocks.ShouldContain(tb => tb.Text != null && tb.Text.Contains("audio"));

        window.Close();
    }

    [AvaloniaFact]
    public void SettingsWindow_Initializes_SelfCorrection_And_RestorePreviousWindow_From_AppSettings()
    {
        var window = new SettingsWindow();
        var settingsFalseTrue = AppSettings.Default with
        {
            EnableSelfCorrection = false,
            RestorePreviousWindowAfterInjection = true
        };

        window.LoadSettings(settingsFalseTrue);
        Dispatcher.UIThread.RunJobs();

        var selfCorrection = window.FindControl<ToggleSwitch>("EnableSelfCorrectionCheckBox");
        var restoreWindow = window.FindControl<ToggleSwitch>("RestorePreviousWindowCheckBox");

        selfCorrection.ShouldNotBeNull();
        restoreWindow.ShouldNotBeNull();
        selfCorrection.IsChecked.ShouldBe(false);
        restoreWindow.IsChecked.ShouldBe(true);

        var settingsTrueFalse = AppSettings.Default with
        {
            EnableSelfCorrection = true,
            RestorePreviousWindowAfterInjection = false
        };

        window.LoadSettings(settingsTrueFalse);
        Dispatcher.UIThread.RunJobs();

        selfCorrection.IsChecked.ShouldBe(true);
        restoreWindow.IsChecked.ShouldBe(false);
    }

    [AvaloniaFact]
    public void SettingsWindow_ApplyToSettings_Saves_ModifiedValues()
    {
        var window = new SettingsWindow();
        window.LoadSettings(AppSettings.Default with
        {
            EnableSelfCorrection = true,
            RestorePreviousWindowAfterInjection = false
        });
        Dispatcher.UIThread.RunJobs();

        var selfCorrection = window.FindControl<ToggleSwitch>("EnableSelfCorrectionCheckBox")!;
        var restoreWindow = window.FindControl<ToggleSwitch>("RestorePreviousWindowCheckBox")!;

        selfCorrection.IsChecked = false;
        restoreWindow.IsChecked = true;

        var result = window.ApplyToSettings(AppSettings.Default);
        result.EnableSelfCorrection.ShouldBeFalse();
        result.RestorePreviousWindowAfterInjection.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void SettingsWindow_SaveRequested_Includes_SelfCorrection_And_RestorePreviousWindow()
    {
        var window = new SettingsWindow();
        window.SetDevices([new AudioInputDevice("dev-1", "Default Mic", true)], "dev-1");
        window.LoadSettings(AppSettings.Default with
        {
            SpeechModelId = "parakeet-v3",
            HotkeyId = "vk:162",
            EnableSelfCorrection = true,
            RestorePreviousWindowAfterInjection = false
        });
        window.SetModelProgress(1.0);
        Dispatcher.UIThread.RunJobs();

        SettingsSaveRequest? savedRequest = null;
        window.SaveRequested += req => savedRequest = req;

        var selfCorrection = window.FindControl<ToggleSwitch>("EnableSelfCorrectionCheckBox")!;
        var restoreWindow = window.FindControl<ToggleSwitch>("RestorePreviousWindowCheckBox")!;
        selfCorrection.IsChecked = false;
        restoreWindow.IsChecked = true;

        var saveButton = window.FindControl<Button>("SaveButton")!;
        saveButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        savedRequest.ShouldNotBeNull();
        savedRequest!.EnableSelfCorrection.ShouldBeFalse();
        savedRequest.RestorePreviousWindowAfterInjection.ShouldBeTrue();
    }

    [Fact]
    public void Localization_SelfCorrection_And_RestoreWindow_Resolve_In_English_And_German()
    {
        UiText.SetLanguage("en");
        UiText.EnableSelfCorrection.ShouldBe("Automatically remove self-corrections (e.g. 'Oh no, Wednesday')");
        UiText.RestorePreviousWindowAfterInjection.ShouldBe("Return to previous window after injection");

        UiText.SetLanguage("de");
        UiText.EnableSelfCorrection.ShouldBe("Versprecher und Selbstkorrekturen automatisch tilgen");
        UiText.RestorePreviousWindowAfterInjection.ShouldBe("Nach dem Einfügen zum vorherigen Fenster zurückkehren");

        UiText.SetLanguage("en");
    }

    [Fact]
    public void App_WiresUp_WhisperTranscriber_SpeechRepairer_And_ControllerSettings()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Murmur.sln")))
        {
            root = root.Parent;
        }

        root.ShouldNotBeNull();
        var appSource = File.ReadAllText(Path.Combine(root!.FullName, "src", "Murmur.App", "App.axaml.cs"));

        appSource.ShouldContain("SherpaOnnxWhisperTranscriber");
        appSource.ShouldContain("RuleBasedSpeechRepairer");
        appSource.ShouldContain("_controller.SetSelfCorrectionEnabled");
        appSource.ShouldContain("_controller.SetRestorePreviousWindow");
    }
}
