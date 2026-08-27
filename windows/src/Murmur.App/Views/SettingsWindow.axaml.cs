using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Win32.Input;
using Murmur.App.Diagnostics;
using Murmur.App.Localization;
using Murmur.Core.Formatting;
using Murmur.Core.History;
using Murmur.Core.Settings;
using Murmur.Dictionary;
using Murmur.Platform.Windows.Audio;
using Murmur.Platform.Windows.Hotkey;
using Murmur.Speech;

namespace Murmur.App.Views;

/// <summary>
/// Settings save request data.
/// </summary>
public sealed record SettingsSaveRequest(
    string? MicrophoneEndpointId,
    bool StartWithWindows,
    string HotkeyId,
    string HudPlacement,
    string SpeechModelId,
    bool RemoveFillers,
    string Language,
    bool AudioFeedbackEnabled,
    IReadOnlyList<DictationProfile>? Profiles = null,
    string? ActiveProfileId = null);

/// <summary>
/// Settings and first-run setup window.
/// </summary>
public partial class SettingsWindow : Window
{
    private static readonly (string Id, string Name)[] HudPlacements =
    [
        ("top-left", "Top left"),
        ("top-center", "Top center"),
        ("top-right", "Top right"),
        ("bottom-left", "Bottom left"),
        ("bottom-center", "Bottom center"),
        ("bottom-right", "Bottom right")
    ];

    private string _selectedHotkeyId = AppSettings.DefaultHotkeyId;
    private string _selectedHotkeyName = UiText.Text("RightCtrl");
    private bool _isCapturingHotkey;

    private DictationProfile[] _profiles = [];
    private string _selectedProfileId = "standard";
    private bool _isUpdatingProfileControls;

    private IReadOnlyList<AudioInputDevice> _devices = [];
    private IReadOnlyList<DictationHistoryEntry> _history = [];
    private DictionaryEntry[] _dictionary = [];
    private Guid? _editingDictionaryId;
    private bool _isDownloading;
    private bool _modelReady;
    private int _modelStatusGeneration;
    private bool _isApplyingChoices;
    private bool _isFirstRun;

    private DispatcherTimer? _historySearchTimer;
    private Button? _lastDeleteAllInitiator;
    private int _micLevelUpdatePending;
    private float _pendingMicLevel;
    private double _lastReportedProgress = -1;

    /// <summary>Fired when save is requested.</summary>
    public event Action<SettingsSaveRequest>? SaveRequested;

    /// <summary>Fired when device refresh is requested.</summary>
    public event Action? RefreshRequested;

    /// <summary>Fired when model download is requested with the selected model ID.</summary>
    public event Action<string>? DownloadRequested;

    /// <summary>Fired when model download cancellation is requested.</summary>
    public event EventHandler? DownloadCancelRequested;

    /// <summary>Fired when microphone test is requested.</summary>
    public event Action? MicrophoneTestRequested;

    /// <summary>Fired when one history entry should be deleted.</summary>
    public event Action<Guid>? HistoryDeleteRequested;

    /// <summary>Fired after the user confirms deletion of all history.</summary>
    public event Action? HistoryClearRequested;

    /// <summary>Fired after a dictionary entry is added, edited, toggled, or deleted.</summary>
    public event Action<IReadOnlyList<DictionaryEntry>>? DictionaryChanged;

    /// <summary>Initializes a new instance of SettingsWindow.</summary>
    public SettingsWindow()
    {
        InitializeComponent();
        ActualThemeVariantChanged += (s, e) => ApplySettingsSurfaceMaterial();
        PropertyChanged += (s, e) =>
        {
            if (e.Property == ActualTransparencyLevelProperty)
            {
                ApplySettingsSurfaceMaterial();
            }
        };
        DictionaryKindComboBox.SelectedIndex = 1;
        UpdateDictionaryEditorState();
        RefreshReadiness();
        UpdateModelStatus();
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ApplySettingsSurfaceMaterial();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _historySearchTimer?.Stop();
        _historySearchTimer = null;
        base.OnClosed(e);
    }

    private string SelectedModelId
    {
        get
        {
            if (SpeechModelComboBox.SelectedIndex >= 0 && SpeechModelComboBox.SelectedIndex < ModelManifest.All.Count)
            {
                return ModelManifest.All[SpeechModelComboBox.SelectedIndex].Id;
            }
            return ModelManifest.ParakeetV3.Id;
        }
    }

    /// <summary>Populates hotkey, hud placement, and speech model choices from current settings.</summary>
    public void SetChoices(AppSettings settings, bool isFirstRun = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var choicesGeneration = Interlocked.Increment(ref _modelStatusGeneration);
        _isFirstRun = isFirstRun;

        var normalized = settings.Normalize();

        Dispatcher.UIThread.Post(() =>
        {
            _isApplyingChoices = true;
            try
            {
                _profiles = normalized.Profiles.ToArray();
                _selectedProfileId = normalized.ActiveProfileId;

                var hotkey = HotkeyOptions.Get(normalized.HotkeyId);
                _selectedHotkeyId = hotkey.Id;
                _selectedHotkeyName = hotkey.Id == AppSettings.DefaultHotkeyId
                    ? UiText.Text("RightCtrl")
                    : hotkey.DisplayName;
                HotkeyTextBox.Text = _selectedHotkeyName;
                AudioFeedbackCheckBox.IsChecked = normalized.AudioFeedbackEnabled;
                LanguageComboBox.ItemsSource = new[] { UiText.English, UiText.German };
                LanguageComboBox.SelectedIndex = string.Equals(normalized.Language, UiLanguage.German, StringComparison.OrdinalIgnoreCase) ? 1 : 0;

                HudPlacementComboBox.ItemsSource = new[] { UiText.Text("TopLeft"), UiText.Text("TopCenter"), UiText.Text("TopRight"), UiText.Text("BottomLeft"), UiText.Text("BottomCenter"), UiText.Text("BottomRight") };
                var hudIdx = Array.FindIndex(HudPlacements, p => string.Equals(p.Id, normalized.HudPlacement, StringComparison.OrdinalIgnoreCase));
                HudPlacementComboBox.SelectedIndex = hudIdx >= 0 ? hudIdx : 4; // bottom-center

                SpeechModelComboBox.ItemsSource = ModelManifest.All.Select(m => m.DisplayName).ToList();
                var modelIdx = ModelManifest.All.ToList().FindIndex(m => string.Equals(m.Id, normalized.SpeechModelId, StringComparison.OrdinalIgnoreCase));
                SpeechModelComboBox.SelectedIndex = modelIdx >= 0 ? modelIdx : 0;

                if (ProfileComboBox.ItemsSource == null)
                {
                    ProfileComboBox.ItemsSource = new[] { UiText.ProfileStandard, UiText.ProfileMath };
                }
                if (MathModeComboBox.ItemsSource == null)
                {
                    MathModeComboBox.ItemsSource = new[] { UiText.MathOutputUnicode, UiText.MathOutputLatex };
                }

                _isUpdatingProfileControls = true;
                try
                {
                    ProfileComboBox.SelectedIndex = string.Equals(_selectedProfileId, "math", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                    UpdateProfileControls();
                }
                finally
                {
                    _isUpdatingProfileControls = false;
                }

                if (choicesGeneration == Volatile.Read(ref _modelStatusGeneration))
                {
                    UpdateModelStatus(normalized.SpeechModelId);
                }

                RefreshReadiness();
            }
            finally
            {
                _isApplyingChoices = false;
            }
        });
    }

    /// <summary>Sets the available capture devices and selects the active one.</summary>
    public void SetDevices(IReadOnlyList<AudioInputDevice> devices, string? selectedId)
    {
        ArgumentNullException.ThrowIfNull(devices);
        _devices = devices;

        Dispatcher.UIThread.Post(() =>
        {
            MicrophoneComboBox.ItemsSource = _devices.Select(d => d.Name + (d.IsDefault ? " (" + UiText.Text("DefaultDevice") + ")" : "")).ToList();

            var selectedIndex = -1;
            if (!string.IsNullOrEmpty(selectedId))
            {
                var idx = _devices.ToList().FindIndex(d => string.Equals(d.Id, selectedId, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    selectedIndex = idx;
                }
            }
            else if (_devices.Count > 0)
            {
                var defaultIdx = _devices.ToList().FindIndex(d => d.IsDefault);
                selectedIndex = defaultIdx >= 0 ? defaultIdx : 0;
            }

            MicrophoneComboBox.SelectedIndex = selectedIndex;
            RefreshReadiness();
        });
    }

    private bool _isStatusError;

    /// <summary>Sets the status text and error appearance.</summary>
    public void SetStatus(string text, bool isError = false)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _isStatusError = isError;
            StatusText.Text = text;
            var amberBrush = ResolveBrush("LensMeterAmberBrush", Brushes.Orange);
            var warningBrush = ResolveBrush("LensWarningBrush", amberBrush);
            var inkPrimary = ResolveBrush("LensInkPrimaryBrush", Brushes.White);
            StatusText.Foreground = isError ? warningBrush : inkPrimary;

            var micReady = _devices.Count > 0 && MicrophoneComboBox.SelectedIndex >= 0 && MicrophoneComboBox.SelectedIndex < _devices.Count;
            var hotkeyReady = !string.IsNullOrWhiteSpace(_selectedHotkeyId);
            var isConfigured = micReady && _modelReady && hotkeyReady && !_isDownloading;
            ReadinessDetails.IsVisible = !isConfigured || _isStatusError;
        });
    }

    /// <summary>Sets the model download progress.</summary>
    public void SetModelProgress(double progress)
    {
        Interlocked.Increment(ref _modelStatusGeneration);
        Dispatcher.UIThread.Post(() =>
        {
            var clamped = Math.Clamp(progress, 0.0, 1.0);
            ModelProgressBar.Value = clamped;
            if (clamped >= 1.0)
            {
                _isDownloading = false;
                _modelReady = true;
                ModelStatusText.Text = UiText.Text("ModelInstalledReady");
                DownloadButton.IsEnabled = false;
                DownloadButton.Content = UiText.Text("InstalledButton");
                CancelDownloadButton.IsVisible = false;
                RefreshReadiness();
            }
            else
            {
                var wasDownloading = _isDownloading;
                _isDownloading = true;
                _modelReady = false;
                var pct = Math.Round(clamped * 100.0);
                if (pct != _lastReportedProgress)
                {
                    _lastReportedProgress = pct;
                    ModelStatusText.Text = UiText.Format("DownloadingProgress", clamped);
                }
                if (!wasDownloading)
                {
                    DownloadButton.IsEnabled = false;
                    DownloadButton.Content = UiText.Text("DownloadingButton");
                    CancelDownloadButton.IsVisible = true;
                    CancelDownloadButton.IsEnabled = true;
                    RefreshReadiness();
                }
            }
        });
    }

    /// <summary>Sets the microphone level for testing.</summary>
    public void SetMicrophoneLevel(float level)
    {
        _pendingMicLevel = level;
        if (Interlocked.CompareExchange(ref _micLevelUpdatePending, 1, 0) == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var lvl = _pendingMicLevel;
                Interlocked.Exchange(ref _micLevelUpdatePending, 0);

                var clamped = Math.Clamp(lvl, 0.0f, 1.0f);
                MicrophoneVUMeter.Level = clamped;
                MicrophoneVUMeter.IsActive = clamped > 0.001f;

                var db = clamped <= 0.001f ? double.NegativeInfinity : Math.Clamp(-50.0 + (clamped * 50.0), -50.0, 0.0);
                MicrophoneDbText.Text = double.IsNegativeInfinity(db) ? "−∞ dB" : $"{Math.Round(db):0} dB";
            }, DispatcherPriority.Input);
        }
    }

    /// <summary>Sets initial checkbox state for Start with Windows.</summary>
    public void SetStartWithWindows(bool startWithWindows)
    {
        Dispatcher.UIThread.Post(() =>
        {
            StartWithWindowsCheckBox.IsChecked = startWithWindows;
        });
    }

    /// <summary>Updates UI to reflect current model download status for the specified or selected model.</summary>
    public async void UpdateModelStatus(string? modelId = null)
    {
        var targetId = modelId ?? SelectedModelId;
        var model = ModelManifest.Get(targetId);
        var generation = Interlocked.Increment(ref _modelStatusGeneration);
        bool ready;
        try
        {
            ready = await Task.Run(() => ModelLocator.IsModelDownloaded(model));
        }
        catch (Exception ex)
        {
            AppLogger.LogException("UpdateModelStatus", ex);
            ready = false;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (generation != Volatile.Read(ref _modelStatusGeneration))
            {
                return;
            }

            if (targetId != SelectedModelId && modelId != null)
            {
                return;
            }

            if (_isDownloading)
            {
                return;
            }

            _modelReady = ready;
            if (ready)
            {
                ModelStatusText.Text = UiText.Text("ModelInstalledReady");
                ModelProgressBar.Value = 1.0;
                DownloadButton.IsEnabled = false;
                DownloadButton.Content = UiText.Text("InstalledButton");
                CancelDownloadButton.IsVisible = false;
            }
            else
            {
                var mb = (int)Math.Round(model.ArchiveSize / (1024.0 * 1024.0));
                ModelStatusText.Text = UiText.Format("ModelNotDownloaded", mb);
                ModelProgressBar.Value = 0.0;
                DownloadButton.IsEnabled = true;
                DownloadButton.Content = UiText.Download;
                CancelDownloadButton.IsVisible = false;
            }

            RefreshReadiness();
        });
    }

    private IBrush ResolveBrush(string key, IBrush fallback)
    {
        if (this.TryFindResource(key, ActualThemeVariant, out var themed) && themed is IBrush themedBrush)
        {
            return themedBrush;
        }

        return this.TryFindResource(key, out var resource) && resource is IBrush brush
            ? brush
            : fallback;
    }

    private void RefreshReadiness()
    {
        var micReady = _devices.Count > 0 && MicrophoneComboBox.SelectedIndex >= 0 && MicrophoneComboBox.SelectedIndex < _devices.Count;
        var hotkeyReady = !string.IsNullOrWhiteSpace(_selectedHotkeyId);
        var modelReady = _modelReady;

        var greenBrush = ResolveBrush("LensMeterGreenBrush", Brushes.Green);
        var amberBrush = ResolveBrush("LensMeterAmberBrush", Brushes.Orange);
        var warningBrush = ResolveBrush("LensWarningBrush", amberBrush);
        var inkPrimary = ResolveBrush("LensInkPrimaryBrush", Brushes.White);

        MicStatusDot.Fill = micReady ? greenBrush : amberBrush;
        ModelStatusDot.Fill = _isDownloading ? amberBrush : (modelReady ? greenBrush : amberBrush);
        HotkeyStatusDot.Fill = hotkeyReady ? greenBrush : amberBrush;

        var readyCount = (micReady ? 1 : 0) + (modelReady && !_isDownloading ? 1 : 0) + (hotkeyReady ? 1 : 0);
        StatusCounterBadgeText.Text = $"{readyCount}/3";

        StatusLamp.Fill = (micReady && modelReady && hotkeyReady && !_isDownloading) ? greenBrush : amberBrush;

        MicReadinessText.Text = micReady
            ? $"{UiText.Microphone}: {_devices[MicrophoneComboBox.SelectedIndex].Name}"
            : UiText.Text("MicrophoneMissing");

        ModelReadinessText.Text = _isDownloading
            ? UiText.Text("ModelDownloading")
            : (modelReady ? UiText.Text("ModelInstalled") : UiText.Text("ModelMissing"));

        HotkeyReadinessText.Text = hotkeyReady
            ? $"{UiText.PushToTalkKey}: {_selectedHotkeyName}"
            : UiText.Text("HotkeyUnset");

        if (!_isStatusError)
        {
            if (_isDownloading)
            {
                StatusText.Text = UiText.Format("StatusDownloading", ModelManifest.Get(SelectedModelId).DisplayName);
            }
            else if (micReady && modelReady && hotkeyReady)
            {
                StatusText.Text = UiText.Text("StatusReady");
            }
            else if (!micReady)
            {
                StatusText.Text = UiText.Text("StatusMicMissing");
            }
            else if (!modelReady)
            {
                StatusText.Text = UiText.Text("StatusSpeechModelMissing");
            }
            else if (!hotkeyReady)
            {
                StatusText.Text = UiText.Text("StatusHotkeyMissing");
            }
            else
            {
                StatusText.Text = UiText.Text("StatusSetupIncomplete");
            }
        }

        StatusText.Foreground = _isStatusError ? warningBrush : inkPrimary;

        var isConfigured = micReady && modelReady && hotkeyReady && !_isDownloading;
        ReadinessDetails.IsVisible = !isConfigured || _isStatusError;
        PreferencesPanel.IsEnabled = isConfigured;
        SaveButton.IsEnabled = isConfigured;
        SaveButton.Content = _isFirstRun ? UiText.StartMurmur : UiText.Apply;
    }

    private void OnMicrophoneSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshReadiness();
    }

    private void OnSpeechModelSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingChoices)
        {
            return;
        }

        UpdateModelStatus(SelectedModelId);
    }

    /// <summary>Filters history entries in memory.</summary>
    internal static IReadOnlyList<DictationHistoryEntry> FilterHistory(IReadOnlyList<DictationHistoryEntry> source, string? query)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(query))
        {
            return source;
        }

        var results = new List<DictationHistoryEntry>();
        for (var i = 0; i < source.Count; i++)
        {
            var entry = source[i];
            if (entry.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(entry);
            }
        }
        return results;
    }

    /// <summary>Replaces displayed history and clears prior errors.</summary>
    public void SetHistory(IReadOnlyList<DictationHistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Dispatcher.UIThread.Post(() =>
        {
            _history = entries;
            HistoryErrorText.IsVisible = false;
            DeleteAllConfirmationPanel.IsVisible = false;
            RenderHistory();
        });
    }

    /// <summary>Shows a non-destructive history operation error.</summary>
    public void SetHistoryError(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            HistoryErrorText.Text = message;
            HistoryErrorText.IsVisible = true;
        });
    }

    /// <summary>Shows history as unavailable while leaving Setup usable.</summary>
    public void SetHistoryUnavailable(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _history = [];
            HistoryErrorText.Text = message;
            HistoryErrorText.IsVisible = true;
            RenderHistory();
        });
    }

    /// <summary>Replaces the dictionary rows shown in the editor.</summary>
    public void SetDictionary(IReadOnlyList<DictionaryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Dispatcher.UIThread.Post(() =>
        {
            _dictionary = entries.ToArray();
            RenderDictionary();
        });
    }

    /// <summary>Shows a non-destructive dictionary operation error.</summary>
    public void SetDictionaryError(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            DictionaryErrorText.Text = message;
            DictionaryErrorText.IsVisible = true;
        });
    }

    /// <summary>Filters dictionary entries across both sides of a correction.</summary>
    internal static IReadOnlyList<DictionaryEntry> FilterDictionary(
        IReadOnlyList<DictionaryEntry> source, string? query)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(query)) return source;

        var results = new List<DictionaryEntry>();
        for (var i = 0; i < source.Count; i++)
        {
            var entry = source[i];
            if (entry.Write.Contains(query, StringComparison.OrdinalIgnoreCase)
                || entry.Hear.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(entry);
            }
        }
        return results;
    }

    private void RenderHistory()
    {
        var query = HistorySearchTextBox.Text;
        var filtered = FilterHistory(_history, query);

        HistoryListBox.ItemsSource = filtered;
        var isEmpty = filtered.Count == 0;
        HistoryListBox.IsVisible = !isEmpty;
        HistoryEmptyPanel.IsVisible = isEmpty;
        HistoryEmptyText.IsVisible = isEmpty;
        HistoryEmptyText.Text = _history.Count == 0 ? UiText.NoDictations : UiText.Text("NoMatchingDictations");
        HistoryEmptyHintText.IsVisible = isEmpty && _history.Count == 0;
        HistoryStorageText.IsVisible = isEmpty && _history.Count == 0;
        HistoryCountText.Text = string.IsNullOrWhiteSpace(query)
            ? UiText.Format("Entries", _history.Count)
            : UiText.Format("EntriesOf", filtered.Count, _history.Count);
        DeleteAllHistoryButton.IsEnabled = _history.Count > 0;
    }

    private void OnHistorySearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (_historySearchTimer == null)
        {
            _historySearchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _historySearchTimer.Tick += (s, args) =>
            {
                _historySearchTimer.Stop();
                RenderHistory();
            };
        }
        else
        {
            _historySearchTimer.Stop();
        }
        _historySearchTimer.Start();
    }

    private void RenderDictionary()
    {
        var filtered = FilterDictionary(_dictionary, DictionarySearchTextBox.Text);
        DictionaryListBox.ItemsSource = filtered;
        DictionaryListBox.IsVisible = filtered.Count > 0;
        DictionaryEmptyText.IsVisible = filtered.Count == 0;
        DictionaryEmptyText.Text = _dictionary.Length == 0 ? UiText.DictionaryEmpty : UiText.Text("NoMatchingEntries");
        DictionaryCountText.Text = string.IsNullOrWhiteSpace(DictionarySearchTextBox.Text)
            ? UiText.Format("Entries", _dictionary.Length)
            : UiText.Format("EntriesOf", filtered.Count, _dictionary.Length);
        DictionaryErrorText.IsVisible = false;
    }

    private void OnDictionarySearchChanged(object? sender, TextChangedEventArgs e)
    {
        RenderDictionary();
    }

    private void OnAddDictionaryClick(object? sender, RoutedEventArgs e)
    {
        _editingDictionaryId = null;
        DictionaryKindComboBox.SelectedIndex = 1;
        DictionaryHearTextBox.Text = string.Empty;
        DictionaryWriteTextBox.Text = string.Empty;
        DictionaryEditorTitle.Text = UiText.AddCorrection;
        DictionaryEditorPanel.IsVisible = true;
        UpdateDictionaryEditorState();
        DictionaryHearTextBox.Focus();
    }

    private void OnEditDictionaryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DictionaryEntry entry }) return;

        _editingDictionaryId = entry.Id;
        DictionaryKindComboBox.SelectedIndex = entry.Kind == EntryKind.Correction ? 1 : 0;
        DictionaryHearTextBox.Text = entry.Hear;
        DictionaryWriteTextBox.Text = entry.Write;
        DictionaryEditorTitle.Text = UiText.EditDictionaryEntry;
        DictionaryEditorPanel.IsVisible = true;
        UpdateDictionaryEditorState();
        DictionaryWriteTextBox.Focus();
    }

    private void OnCancelDictionaryClick(object? sender, RoutedEventArgs e)
    {
        DictionaryEditorPanel.IsVisible = false;
        _editingDictionaryId = null;
    }

    private void OnSaveDictionaryClick(object? sender, RoutedEventArgs e)
    {
        var kind = DictionaryKindComboBox.SelectedIndex == 1 ? EntryKind.Correction : EntryKind.Term;
        var write = DictionaryWriteTextBox.Text?.Trim() ?? string.Empty;
        var hear = kind == EntryKind.Correction
            ? DictionaryHearTextBox.Text?.Trim() ?? string.Empty
            : string.Empty;
        if (write.Length == 0 || (kind == EntryKind.Correction && hear.Length == 0))
        {
            DictionaryErrorText.Text = kind == EntryKind.Correction
                ? UiText.Text("EnterCorrection")
                : UiText.Text("EnterWord");
            DictionaryErrorText.IsVisible = true;
            return;
        }

        var entry = new DictionaryEntry
        {
            Id = _editingDictionaryId ?? Guid.NewGuid(),
            Kind = kind,
            Hear = hear,
            Write = write,
            IsEnabled = _editingDictionaryId is null
                || _dictionary.FirstOrDefault(item => item.Id == _editingDictionaryId)?.IsEnabled != false
        };
        var updated = _editingDictionaryId is Guid id
            ? _dictionary.Select(item => item.Id == id ? entry : item).ToArray()
            : _dictionary.Append(entry).ToArray();

        _dictionary = updated;
        DictionaryChanged?.Invoke(_dictionary);
        DictionaryEditorPanel.IsVisible = false;
        _editingDictionaryId = null;
        RenderDictionary();
    }

    private void OnDictionaryKindChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateDictionaryEditorState();
    }

    private void UpdateDictionaryEditorState()
    {
        var isCorrection = DictionaryKindComboBox.SelectedIndex == 1;
        DictionaryHearLabel.IsVisible = isCorrection;
        DictionaryHearTextBox.IsVisible = isCorrection;
        DictionaryWarningText.Text = isCorrection
            ? string.Join(Environment.NewLine, DictionaryWarning.Check(new DictionaryEntry
            {
                Kind = EntryKind.Correction,
                Hear = DictionaryHearTextBox.Text?.Trim() ?? string.Empty,
                Write = DictionaryWriteTextBox.Text?.Trim() ?? string.Empty
            }).Select(warning => warning.Message))
            : string.Empty;
        DictionaryWarningText.IsVisible = !string.IsNullOrEmpty(DictionaryWarningText.Text);
    }

    private void OnDictionaryTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateDictionaryEditorState();
    }

    private void OnToggleDictionaryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DictionaryEntry entry }) return;
        _dictionary = _dictionary.Select(item => item.Id == entry.Id
            ? item with { IsEnabled = !item.IsEnabled }
            : item).ToArray();
        DictionaryChanged?.Invoke(_dictionary);
        RenderDictionary();
    }

    private void OnDeleteDictionaryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DictionaryEntry entry }) return;
        _dictionary = _dictionary.Where(item => item.Id != entry.Id).ToArray();
        DictionaryChanged?.Invoke(_dictionary);
        RenderDictionary();
    }

    private async void OnCopyHistoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DictationHistoryEntry entry } btn)
        {
            return;
        }

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                SetHistoryError(UiText.Text("ClipboardUnavailable"));
                return;
            }

            await clipboard.SetTextAsync(entry.Text);
            var originalContent = btn.Content;
            btn.Content = UiText.Text("Copied");
            btn.IsEnabled = false;
            await Task.Delay(1500);
            btn.Content = originalContent;
            btn.IsEnabled = true;
        }
        catch (Exception ex)
        {
            SetHistoryError(UiText.Format("CopyFailed", ex.Message));
        }
    }

    private void OnDeleteHistoryRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Parent is StackPanel defaultPanel && defaultPanel.Parent is Grid actionsGrid)
        {
            var confirmPanel = actionsGrid.Children.OfType<Grid>().FirstOrDefault(g => g.Name == "InlineDeleteConfirmPanel");
            if (confirmPanel != null)
            {
                defaultPanel.IsVisible = false;
                confirmPanel.IsVisible = true;
            }
        }
    }

    private void OnCancelInlineDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Parent is Grid confirmPanel && confirmPanel.Parent is Grid actionsGrid)
        {
            var defaultPanel = actionsGrid.Children.OfType<StackPanel>().FirstOrDefault(p => p.Name == "DefaultActionPanel");
            confirmPanel.IsVisible = false;
            if (defaultPanel != null)
            {
                defaultPanel.IsVisible = true;
            }
        }
    }

    private void OnConfirmInlineDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DictationHistoryEntry entry } btn && btn.Parent is Grid confirmPanel && confirmPanel.Parent is Grid actionsGrid)
        {
            var defaultPanel = actionsGrid.Children.OfType<StackPanel>().FirstOrDefault(p => p.Name == "DefaultActionPanel");
            confirmPanel.IsVisible = false;
            if (defaultPanel != null)
            {
                defaultPanel.IsVisible = true;
            }

            HistoryDeleteRequested?.Invoke(entry.Id);
        }
    }

    private void OnDeleteAllHistoryClick(object? sender, RoutedEventArgs e)
    {
        _lastDeleteAllInitiator = sender as Button;
        DeleteAllConfirmationPanel.IsVisible = true;
        CancelDeleteAllButton.Focus();
    }

    private void OnCancelDeleteAllHistoryClick(object? sender, RoutedEventArgs e)
    {
        DeleteAllConfirmationPanel.IsVisible = false;
        _lastDeleteAllInitiator?.Focus();
    }

    private void OnConfirmDeleteAllHistoryClick(object? sender, RoutedEventArgs e)
    {
        DeleteAllConfirmationPanel.IsVisible = false;
        HistoryClearRequested?.Invoke();
        _lastDeleteAllInitiator?.Focus();
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        RefreshRequested?.Invoke();
    }

    private void OnTestMicrophoneClick(object? sender, RoutedEventArgs e)
    {
        MicrophoneTestRequested?.Invoke();
    }

    private void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        Interlocked.Increment(ref _modelStatusGeneration);
        _isDownloading = true;
        CancelDownloadButton.IsVisible = true;
        CancelDownloadButton.IsEnabled = true;
        RefreshReadiness();
        DownloadRequested?.Invoke(SelectedModelId);
    }

    private void OnCancelDownloadClick(object? sender, RoutedEventArgs e)
    {
        CancelDownloadButton.IsEnabled = false;
        DownloadCancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCaptureHotkeyClick(object? sender, RoutedEventArgs e)
    {
        _isCapturingHotkey = true;
        HotkeyTextBox.Text = UiText.Text("PressAnyKey");
        Activate();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_isCapturingHotkey)
        {
            return;
        }

        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            _isCapturingHotkey = false;
            HotkeyTextBox.Text = _selectedHotkeyName;
            RefreshReadiness();
            return;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(e.Key);
        if (virtualKey is <= 0 or > 0xFF)
        {
            SetStatus(UiText.Text("KeyCannot"), isError: true);
            return;
        }

        _selectedHotkeyId = $"vk:{virtualKey}";
        _selectedHotkeyName = e.Key.ToString();
        _isCapturingHotkey = false;
        HotkeyTextBox.Text = _selectedHotkeyName;
        RefreshReadiness();
    }

    private void OnProfileSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingProfileControls || ProfileComboBox.SelectedIndex < 0) return;

        var newProfileId = ProfileComboBox.SelectedIndex == 1 ? "math" : "standard";
        if (string.Equals(_selectedProfileId, newProfileId, StringComparison.OrdinalIgnoreCase)) return;

        SaveCurrentControlValuesToProfile();
        _selectedProfileId = newProfileId;

        _isUpdatingProfileControls = true;
        try
        {
            UpdateProfileControls();
        }
        finally
        {
            _isUpdatingProfileControls = false;
        }
    }

    private void OnMathModeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingProfileControls) return;
        SaveCurrentControlValuesToProfile();
    }

    private void UpdateProfileControls()
    {
        var isMath = string.Equals(_selectedProfileId, "math", StringComparison.OrdinalIgnoreCase);
        var profileIndex = isMath ? 1 : 0;
        var profile = _profiles.Length > profileIndex ? _profiles[profileIndex] : new DictationProfile();

        RemoveFillersCheckBox.IsChecked = profile.RemoveFillers;
        MathModePanel.IsVisible = isMath;
        MathModeComboBox.IsVisible = isMath;
        MathModeComboBox.SelectedIndex = profile.MathMode == MathFormatMode.Latex ? 1 : 0;

        ProfileDescriptionText.Text = isMath
            ? UiText.ProfileDescriptionMath
            : UiText.ProfileDescriptionStandard;
    }

    private void SaveCurrentControlValuesToProfile()
    {
        var isMath = string.Equals(_selectedProfileId, "math", StringComparison.OrdinalIgnoreCase);
        var profileIndex = isMath ? 1 : 0;
        if (_profiles.Length > profileIndex)
        {
            _profiles[profileIndex] = _profiles[profileIndex] with
            {
                RemoveFillers = RemoveFillersCheckBox.IsChecked ?? true,
                MathMode = MathModeComboBox.SelectedIndex == 1 ? MathFormatMode.Latex : MathFormatMode.Unicode
            };
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        string? selectedId = null;
        if (MicrophoneComboBox.SelectedIndex >= 0 && MicrophoneComboBox.SelectedIndex < _devices.Count)
        {
            selectedId = _devices[MicrophoneComboBox.SelectedIndex].Id;
        }

        var startWithWindows = StartWithWindowsCheckBox.IsChecked ?? true;
        var hudPlacement = HudPlacements[4].Id;
        if (HudPlacementComboBox.SelectedIndex >= 0 && HudPlacementComboBox.SelectedIndex < HudPlacements.Length)
        {
            hudPlacement = HudPlacements[HudPlacementComboBox.SelectedIndex].Id;
        }

        var speechModelId = SelectedModelId;

        SaveCurrentControlValuesToProfile();

        var isMath = string.Equals(_selectedProfileId, "math", StringComparison.OrdinalIgnoreCase);
        var activeProfile = _profiles.Length > (isMath ? 1 : 0)
            ? _profiles[isMath ? 1 : 0]
            : new DictationProfile();

        SaveRequested?.Invoke(new SettingsSaveRequest(
            selectedId,
            startWithWindows,
            _selectedHotkeyId,
            hudPlacement,
            speechModelId,
            activeProfile.RemoveFillers,
            LanguageComboBox.SelectedIndex == 1 ? UiLanguage.German : UiLanguage.English,
            AudioFeedbackCheckBox.IsChecked ?? false,
            _profiles,
            _selectedProfileId));
    }

    private void OnCloseButtonClick(object? sender, RoutedEventArgs e) => Close();

    private void OnMinimizeButtonClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeButtonClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                BeginMoveDrag(e);
            }
        }
    }

    private void OnResizeNorth(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginResizeDrag(WindowEdge.North, e);
    }
    private void OnResizeSouth(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginResizeDrag(WindowEdge.South, e);
    }
    private void OnResizeWest(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginResizeDrag(WindowEdge.West, e);
    }
    private void OnResizeEast(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginResizeDrag(WindowEdge.East, e);
    }
    private void OnResizeNorthWest(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginResizeDrag(WindowEdge.NorthWest, e);
    }
    private void OnResizeNorthEast(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginResizeDrag(WindowEdge.NorthEast, e);
    }
    private void OnResizeSouthWest(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginResizeDrag(WindowEdge.SouthWest, e);
    }
    private void OnResizeSouthEast(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginResizeDrag(WindowEdge.SouthEast, e);
    }

    private void ApplySettingsSurfaceMaterial()
    {
        var surface = this.FindControl<Border>("SettingsSurface");
        if (surface == null) return;

        if (ActualTransparencyLevel == WindowTransparencyLevel.None)
        {
            if (this.TryFindResource("LensSolidFallbackBrush", ActualThemeVariant, out var solidRes) && solidRes is IBrush solidBrush)
            {
                surface.Background = solidBrush;
            }
        }
        else
        {
            if (this.TryFindResource("LensChassisBrush", ActualThemeVariant, out var chassisRes) && chassisRes is IBrush chassisBrush)
            {
                surface.Background = chassisBrush;
            }
        }
    }
}
