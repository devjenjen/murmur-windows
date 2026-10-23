using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Murmur.App.Diagnostics;
using Murmur.App.Localization;
using Murmur.App.Views;
using Murmur.Core;
using Murmur.Core.Abstractions;
using Murmur.Core.Formatting;
using Murmur.Core.History;
using Murmur.Core.Settings;
using Murmur.Core.Updates;
using Murmur.Core.State;
using Murmur.Dictionary;
using Murmur.Platform.Windows.Audio;
using Murmur.Platform.Windows.Hotkey;
using Murmur.Platform.Windows.Injection;
using Murmur.Platform.Windows.Startup;
using Murmur.Speech;

namespace Murmur.App;

/// <summary>
/// Main Murmur Avalonia application.
/// </summary>
public partial class App : Application
{
    private const string UpdateAvailableMenuItemId = "Murmur.UpdateAvailable";

    private enum TrayStatusKind
    {
        Ready,
        LoadingSpeechModel,
        Error
    }

    /// <summary>Initializes the application and restores persisted settings.</summary>
    public App()
    {
        _settings = _settingsStore.Load().Normalize();
        UiText.SetLanguage(_settings.Language);
    }

    private readonly SettingsStore _settingsStore = new();
    private readonly HistoryStore _historyStore = new();
    private string? _historyError;
    private AppSettings _settings = AppSettings.Default;
    private IReadOnlyList<DictionaryEntry> _dictionaryEntries = [];

    private DictationController? _controller;
    private readonly SemaphoreSlim _controllerGate = new(1, 1);
    private int _controllerGeneration;
    private volatile bool _shuttingDown;
    private Action? _unsubscribeControllerEvents;
    private Task? _initializationTask;
    private string? _controllerModelId;
    private string? _loadingModelId;
    private readonly Dictionary<string, string> _modelLoadErrors = new(StringComparer.OrdinalIgnoreCase);
    private LowLevelKeyboardHook? _profileToggleHook;
    private HudWindow? _hud;
    private SettingsWindow? _settingsWindow;
    private WasapiAudioCapture? _testCapture;
    private CancellationTokenSource? _testCts;
    private readonly Lock _testCaptureLock = new();
    private int _testCaptureGeneration;
    private CancellationTokenSource? _downloadCts;
    private int _downloadGeneration;
    private readonly ClickSoundPlayer _clickSoundPlayer = new();
    private string? _clickSoundPath;
    private readonly ReleaseUpdateChecker _updateChecker = new();
    private readonly CancellationTokenSource _updateCts = new();
    private ReleaseInfo? _availableRelease;
    private int _updateCheckInProgress;
    private NativeMenuItem? _updateAvailableMenuItem;
    private TrayStatusKind _trayStatus = TrayStatusKind.LoadingSpeechModel;
    private string? _recoveryTranscript;

    /// <inheritdoc />
    public override void Initialize()
    {
        AppLogger.Log("App.Initialize() started.");
        AvaloniaXamlLoader.Load(this);
        AppLogger.Log("App.Initialize() completed.");
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        AppLogger.Log("App.OnFrameworkInitializationCompleted() started.");
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _hud = new HudWindow();
            AppLogger.Log("HudWindow created.");

            _initializationTask = InitializeMurmurAsync();
            _ = CheckForUpdatesAsync();
        }

        base.OnFrameworkInitializationCompleted();
        AppLogger.Log("App.OnFrameworkInitializationCompleted() completed.");
    }

    private async Task InitializeMurmurAsync()
    {
        try
        {
            _clickSoundPath = Path.Combine(
                AppContext.BaseDirectory,
                "Sounds",
                "feedback.wav");

            _settings = _settingsStore.Load().Normalize();
            UiText.SetLanguage(_settings.Language);
            AppLogger.Log($"Settings loaded: StartWithWindows={_settings.StartWithWindows}, SetupCompleted={_settings.SetupCompleted}, HotkeyId={_settings.HotkeyId}, HudPlacement={_settings.HudPlacement}, SpeechModelId={_settings.SpeechModelId}, Language={_settings.Language}, AudioFeedbackEnabled={_settings.AudioFeedbackEnabled}, ActiveProfileId={_settings.ActiveProfileId}");

            var devices = WasapiAudioCapture.GetActiveDevices();
            AppLogger.Log($"Active audio input devices: {devices.Count}");
            foreach (var dev in devices)
            {
                AppLogger.Log($"  Device: {dev.Name} (Default: {dev.IsDefault})");
            }

            var model = ModelManifest.Get(_settings.SpeechModelId);
            var modelAvailable = await Task.Run(() => ModelLocator.HasRequiredFiles(model));
            if (!_settings.SetupCompleted || !modelAvailable || devices.Count == 0)
            {
                AppLogger.Log("Setup incomplete or model missing. Showing settings window.");
                if (_shuttingDown) return;
                UpdateTrayStatus(TrayStatusKind.Error);
                ShowSettings();
                return;
            }

            await StartControllerAsync();
        }
        catch (Exception ex)
        {
            AppLogger.LogException("InitializeMurmurAsync", ex);
            if (_shuttingDown) return;
            UpdateTrayStatus(TrayStatusKind.Error);
            ShowSettings();
            Dispatcher.UIThread.Post(() =>
            {
                if (!_shuttingDown)
                    _settingsWindow?.SetStatus(UiText.Format("AppError", ex.Message), isError: true);
            });
        }
    }

    private static ITextFormatter CreateFormatter(DictationProfile profile)
    {
        return profile.FormatterKind == ProfileFormatterKind.Math
            ? new MathFormatter(profile.MathMode, profile.RemoveFillers)
            : new RuleBasedFormatter(profile.RemoveFillers);
    }

    private static bool RequiresControllerRestart(AppSettings previous, AppSettings next)
    {
        return !string.Equals(previous.MicrophoneEndpointId, next.MicrophoneEndpointId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(previous.HotkeyId, next.HotkeyId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(previous.SpeechModelId, next.SpeechModelId, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyActiveProfile()
    {
        _settings = _settings.Normalize();
        _settingsStore.Save(_settings);

        var activeProfile = _settings.ActiveProfile;
        var formatter = CreateFormatter(activeProfile);

        _controller?.UpdateProfile(activeProfile, formatter);
        _hud?.UpdateProfileBadge(activeProfile);
        UpdateTrayProfileItems();

        AppLogger.Log($"Applied active profile: {activeProfile.Id} (Kind={activeProfile.FormatterKind}, MathMode={activeProfile.MathMode}, RemoveFillers={activeProfile.RemoveFillers})");
    }

    private void SetupProfileToggleHook()
    {
        _profileToggleHook?.Dispose();
        _profileToggleHook = null;

        var pttOption = HotkeyOptions.Get(_settings.HotkeyId);
        var toggleOption = HotkeyOptions.Get(_settings.ProfileToggleHotkeyId);

        if (toggleOption.VirtualKey == pttOption.VirtualKey)
        {
            var fallbackKeys = new[] { "f8", "f9", "f10", "f11", "f12" };
            var found = false;
            foreach (var keyId in fallbackKeys)
            {
                var candidate = HotkeyOptions.Get(keyId);
                if (candidate.VirtualKey != pttOption.VirtualKey)
                {
                    _settings = _settings with { ProfileToggleHotkeyId = keyId };
                    _settingsStore.Save(_settings);
                    toggleOption = candidate;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return;
            }
        }

        try
        {
            var controllerOwner = _controller;
            var hookOwner = new LowLevelKeyboardHook(_settings.ProfileToggleHotkeyId);
            _profileToggleHook = hookOwner;
            hookOwner.Pressed += () => OnProfileToggleHotkeyPressed(hookOwner, controllerOwner);
            hookOwner.Start();
            AppLogger.Log($"Profile toggle hook started: {_settings.ProfileToggleHotkeyId} (vk={toggleOption.VirtualKey})");
        }
        catch (Exception ex)
        {
            AppLogger.LogException("SetupProfileToggleHook", ex);
            _profileToggleHook?.Dispose();
            _profileToggleHook = null;
        }
    }

    private void OnProfileToggleHotkeyPressed(LowLevelKeyboardHook hookOwner, DictationController? controllerOwner)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_shuttingDown || controllerOwner == null || !ReferenceEquals(_controller, controllerOwner) ||
                !ReferenceEquals(_profileToggleHook, hookOwner)) return;
            var nextId = string.Equals(_settings.ActiveProfileId, "standard", StringComparison.OrdinalIgnoreCase)
                ? "math"
                : "standard";

            _settings = _settings with { ActiveProfileId = nextId };
            ApplyActiveProfile();
        });
    }

    private void UpdateTrayProfileItems()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var trayIcons = TrayIcon.GetIcons(this);
            if (trayIcons is null) return;

            foreach (var icon in trayIcons)
            {
                var items = icon.Menu?.Items;
                if (items is null) continue;

                foreach (var item in items.OfType<NativeMenuItem>())
                {
                    if (item.CommandParameter is string param)
                    {
                        if (string.Equals(param, "Murmur.Profile.standard", StringComparison.Ordinal))
                        {
                            item.IsChecked = string.Equals(_settings.ActiveProfileId, "standard", StringComparison.OrdinalIgnoreCase);
                        }
                        else if (string.Equals(param, "Murmur.Profile.math", StringComparison.Ordinal))
                        {
                            item.IsChecked = string.Equals(_settings.ActiveProfileId, "math", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
            }
        });
    }

    internal void RefreshTrayLocalization()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var trayIcons = TrayIcon.GetIcons(this);
            if (trayIcons is null) return;

            foreach (var icon in trayIcons)
            {
                foreach (var item in icon.Menu?.Items.OfType<NativeMenuItem>() ?? [])
                {
                    item.Header = (item.CommandParameter as string) switch
                    {
                        "Murmur.Tray.status" => GetTrayStatusText(),
                        "Murmur.Profile.standard" => UiText.ProfileStandard,
                        "Murmur.Profile.math" => UiText.ProfileMath,
                        "Murmur.Tray.settings" => UiText.TraySettings,
                        "Murmur.Tray.open-log-folder" => UiText.TrayOpenLogFolder,
                        UpdateAvailableMenuItemId => UiText.TrayUpdateAvailable,
                        "Murmur.Tray.check-for-updates" => UiText.TrayCheckForUpdates,
                        "Murmur.Tray.start-with-windows" => UiText.TrayStartWithWindows,
                        "Murmur.Tray.exit" => UiText.TrayExit,
                        _ => item.Header
                    };
                }
            }
        });
    }

    private void OnProfileMenuItemClick(object? sender, EventArgs e)
    {
        if (sender is not NativeMenuItem { CommandParameter: string param }) return;

        var targetId = param switch
        {
            "Murmur.Profile.standard" => "standard",
            "Murmur.Profile.math" => "math",
            _ => null
        };

        if (targetId is null)
        {
            UpdateTrayProfileItems();
            return;
        }

        if (!string.Equals(_settings.ActiveProfileId, targetId, StringComparison.OrdinalIgnoreCase))
        {
            _settings = _settings with { ActiveProfileId = targetId };
            ApplyActiveProfile();
        }
        else
        {
            UpdateTrayProfileItems();
        }
    }

    private async Task StartControllerAsync()
    {
        var generation = Interlocked.Increment(ref _controllerGeneration);
        var settings = _settings.Normalize();
        var model = ModelManifest.Get(settings.SpeechModelId);
        bool IsCurrentLoad() => !_shuttingDown && generation == Volatile.Read(ref _controllerGeneration);
        await _controllerGate.WaitAsync();
        ITranscriber? transcriber = null;
        WasapiAudioCapture? audioCapture = null;
        LowLevelKeyboardHook? hotkeyHook = null;
        DictationController? candidate = null;
        var loadingTranscriber = false;
        try
        {
            if (!IsCurrentLoad()) return;
            _profileToggleHook?.Dispose();
            _profileToggleHook = null;
            await DisposeControllerAsync();
            if (!IsCurrentLoad()) return;

            if (!await Task.Run(() => ModelLocator.HasRequiredFiles(model)))
            {
                if (IsCurrentLoad())
                {
                    UpdateTrayStatus(TrayStatusKind.Error);
                    ShowSettings();
                }
                return;
            }
            if (!IsCurrentLoad()) return;

            var activeProfile = settings.ActiveProfile;
            var initialFormatter = CreateFormatter(activeProfile);

            _hud?.SetPlacement(settings.HudPlacement);
            _hud?.UpdateState(DictationState.Idle, 0, string.Empty);
            UpdateTrayStatus(TrayStatusKind.LoadingSpeechModel);
            _loadingModelId = model.Id;
            _settingsWindow?.SetModelLoading(model.Id);

            var sw = Stopwatch.StartNew();
            loadingTranscriber = true;
            transcriber = model.SupportsLivePreview
                ? await Task.Run(() => new NemotronStreamingTranscriber(ModelLocator.GetDefaultModelDirectory(model)))
                : string.Equals(model.Id, ModelManifest.WhisperV3Turbo.Id, StringComparison.OrdinalIgnoreCase)
                    ? await Task.Run(() => new SherpaOnnxWhisperTranscriber(ModelLocator.GetDefaultModelDirectory(model)))
                    : await Task.Run(() => new SherpaOnnxTranscriber(ModelLocator.GetDefaultModelDirectory(model)));
            loadingTranscriber = false;
            if (!IsCurrentLoad()) return;
            sw.Stop();
            AppLogger.Log($"Transcriber initialized in {sw.ElapsedMilliseconds} ms (Model: {model.Id}, live_preview={model.SupportsLivePreview})");

            audioCapture = new WasapiAudioCapture(settings.MicrophoneEndpointId);
            var captureOwner = audioCapture;
            captureOwner.SessionStopped += () => LogCaptureDiagnostics(captureOwner);
            hotkeyHook = new LowLevelKeyboardHook(settings.HotkeyId);
            var injector = new SendInputInjector();
            DictionaryCorrector dictionaryCorrector;
            try
            {
                _dictionaryEntries = DictionaryFile.LoadOrCreate();
                dictionaryCorrector = new DictionaryCorrector(_dictionaryEntries);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Dictionary load failed: path={DictionaryFile.DefaultPath} exception={ex.GetType().Name}");
                dictionaryCorrector = new DictionaryCorrector([]);
            }

            var qwenDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Murmur", "models", "qwen-2.5-0.5b");
            ISpeechRepairer speechRepairer = new LlamaSharpSpeechRepairer(qwenDir, new RuleBasedSpeechRepairer());

            candidate = new DictationController(
                audioCapture,
                hotkeyHook,
                transcriber,
                injector,
                model.DisplayName,
                initialFormatter,
                dictionaryCorrector,
                activeProfile,
                settings.TextInsertionMode,
                speechRepairer,
                settings.EnableSelfCorrection,
                settings.RestorePreviousWindowAfterInjection);
            transcriber = null;
            audioCapture = null;
            hotkeyHook = null;
            if (!IsCurrentLoad()) return;
            _controller = candidate;
            _unsubscribeControllerEvents = SubscribeControllerEvents(candidate, model.Id);
            candidate.Start();
            AppLogger.Log("DictationController started successfully.");

            SetupProfileToggleHook();
            _hud?.UpdateProfileBadge(activeProfile);
            UpdateTrayProfileItems();

            if (!_settings.SetupCompleted)
            {
                _settings = _settings with { SetupCompleted = true };
                _settingsStore.Save(_settings);
            }

            UpdateTrayStatus(TrayStatusKind.Ready);
            _controllerModelId = model.Id;
            _loadingModelId = null;
            _modelLoadErrors.Remove(model.Id);
            _settingsWindow?.SetModelLoaded(model.Id);
            candidate = null;
        }
        catch (Exception ex)
        {
            AppLogger.LogException("StartControllerAsync", ex);
            if (candidate != null && ReferenceEquals(_controller, candidate))
                await DisposeControllerAsync();
            if (IsCurrentLoad())
            {
                _loadingModelId = null;
                _settingsWindow?.SetModelNotLoaded();
                var message = loadingTranscriber
                    ? UiText.Format("ModelLoadFailed", ex.Message) : UiText.Format("AppError", ex.Message);
                if (loadingTranscriber) _modelLoadErrors[model.Id] = message;
                UpdateTrayStatus(TrayStatusKind.Error);
                ShowSettings();
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsCurrentLoad())
                    {
                        if (loadingTranscriber) _settingsWindow?.SetModelLoadError(model.Id, message);
                        else _settingsWindow?.SetStatus(message, isError: true);
                    }
                });
            }
        }
        finally
        {
            try
            {
                if (candidate != null) await candidate.DisposeAsync();
                try { audioCapture?.Dispose(); }
                finally
                {
                    try { hotkeyHook?.Dispose(); }
                    finally { transcriber?.Dispose(); }
                }
            }
            catch (Exception ex) { AppLogger.LogException("Controller load cleanup", ex); }
            finally { _controllerGate.Release(); }
            if (string.Equals(_loadingModelId, model.Id, StringComparison.OrdinalIgnoreCase)) _loadingModelId = null;
        }
    }

    private async Task DisposeControllerAsync()
    {
        var owner = _controller;
        var unsubscribe = _unsubscribeControllerEvents;
        _unsubscribeControllerEvents = null;
        _controller = null;
        _controllerModelId = null;
        _settingsWindow?.SetModelNotLoaded();
        try { if (owner != null) await owner.DisposeAsync(); }
        finally { unsubscribe?.Invoke(); }
    }

    private Action SubscribeControllerEvents(DictationController owner, string modelId)
    {
        bool IsOwner() => !_shuttingDown && ReferenceEquals(_controller, owner);
        void Post(Action action)
        {
            if (!IsOwner()) return;
            Dispatcher.UIThread.Post(() => { if (IsOwner()) action(); });
        }
        // Persistence belongs to committed insertions, including ones shutdown is still awaiting.
        Action<CompletedDictation> completed = OnDictationCompleted;
        Action<CompletedDictation> injected = value => Post(() => OnTextInjected(value));
        Action<DictationState> stateChanged = state =>
        {
            var text = owner.CurrentTranscript;
            var level = owner.Level;
            Post(() =>
            {
                AppLogger.Log($"StateChanged: {state.Status}");
                if (state.Status == DictationStatus.Error && !string.IsNullOrWhiteSpace(text))
                {
                    _recoveryTranscript = text;
                    _settingsWindow?.SetRecoveryTranscript(text);
                }
                _hud?.UpdateState(LocalizeDictationState(state), level, text);
            });
        };
        Action<float> levelChanged = level =>
        {
            var status = owner.State.Status;
            Post(() => _hud?.UpdateLevel(status, level));
        };
        Action<string> transcriptChanged = text =>
        {
            var state = owner.State;
            var level = owner.Level;
            Post(() => _hud?.UpdateState(LocalizeDictationState(state), level, text));
        };
        Action<DictationMetrics> metrics = value =>
        {
            var durationKind = value.IsStreaming ? "streaming_completion_ms" : "offline_inference_ms";
            var status = value.ReleaseToInjectionMs.HasValue ? "injected" : value.TranscriptLength == 0 ? "empty" : "injection_failed";
            var ratio = value.IsStreaming ? string.Empty : FormattableString.Invariant($" offline_rtf={value.RealTimeFactor:F3}");
            var firstPartial = value.FirstPartialMs is { } partial ? FormattableString.Invariant($"{partial:F0}") : "null";
            var releaseToInjection = value.ReleaseToInjectionMs is { } injection ? FormattableString.Invariant($"{injection:F0}") : "null";
            AppLogger.Log(FormattableString.Invariant(
                $"Dictation metrics: model_id={modelId} result={status} audio_ms={value.AudioDurationMs:F0} {durationKind}={value.InferenceDurationMs:F0} first_partial_ms={firstPartial} release_to_injection_ms={releaseToInjection}{ratio}"));
        };
        owner.DictationCompleted += completed;
        owner.TextInjected += injected;
        owner.StateChanged += stateChanged;
        owner.LevelChanged += levelChanged;
        owner.TranscriptChanged += transcriptChanged;
        owner.MetricsRecorded += metrics;
        return () =>
        {
            owner.DictationCompleted -= completed;
            owner.TextInjected -= injected;
            owner.StateChanged -= stateChanged;
            owner.LevelChanged -= levelChanged;
            owner.TranscriptChanged -= transcriptChanged;
            owner.MetricsRecorded -= metrics;
        };
    }

    private void OnTextInjected(CompletedDictation completed)
    {
        _recoveryTranscript = null;
        _settingsWindow?.ClearRecoveryTranscript();

        if (!_settings.AudioFeedbackEnabled)
        {
            return;
        }

        if (_clickSoundPath is null || !File.Exists(_clickSoundPath))
        {
            var expectedPath = _clickSoundPath ?? Path.Combine(AppContext.BaseDirectory, "Sounds", "feedback.wav");
            AppLogger.Log($"Audio feedback WAV not found at expected path: {expectedPath}");
            return;
        }

        if (!_clickSoundPlayer.Play(_clickSoundPath))
        {
            AppLogger.Log($"Audio feedback WAV could not be played: {_clickSoundPath}");
        }
    }

    private static DictationState LocalizeDictationState(DictationState state)
    {
        if (state.Status != DictationStatus.Error || string.IsNullOrWhiteSpace(state.Message))
        {
            return state;
        }

        var localized = state.Message switch
        {
            "TextTargetUnavailable" => UiText.TextTargetUnavailable,
            "TextTargetChanged" => UiText.TextTargetChanged,
            "TextModifierHeld" => UiText.TextModifierHeld,
            _ => state.Message
        };
        return string.Equals(localized, state.Message, StringComparison.Ordinal)
            ? state
            : state with { Message = localized };
    }

    private void OnRecoveryTranscriptCleared()
    {
        _recoveryTranscript = null;
    }

    private void ShowSettings()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_shuttingDown) return;
            if (_settingsWindow == null)
            {
                _settingsWindow = new SettingsWindow();
                _settingsWindow.SaveRequested += OnSettingsSaveRequested;
                _settingsWindow.RefreshRequested += OnSettingsRefreshRequested;
                _settingsWindow.DownloadRequested += OnSettingsDownloadRequested;
                _settingsWindow.DownloadCancelRequested += OnSettingsDownloadCancelRequested;
                _settingsWindow.MicrophoneTestRequested += OnSettingsMicrophoneTestRequested;
                _settingsWindow.HistoryDeleteRequested += OnHistoryDeleteRequested;
                _settingsWindow.HistoryClearRequested += OnHistoryClearRequested;
                _settingsWindow.DictionaryChanged += OnDictionaryChanged;
                _settingsWindow.RecoveryTranscriptCleared += OnRecoveryTranscriptCleared;
                _settingsWindow.Closed += (s, e) =>
                {
                    _ = StopMicrophoneTestAsync();
                    _settingsWindow = null;
                };
            }

            IReadOnlyList<AudioInputDevice> devices;
            string? deviceError = null;
            try { devices = WasapiAudioCapture.GetActiveDevices(); }
            catch (Exception ex)
            {
                AppLogger.LogException("ShowSettings audio devices", ex);
                devices = [];
                deviceError = UiText.Format("AppError", ex.Message);
            }
            _settingsWindow.SetChoices(_settings);
            foreach (var (modelId, error) in _modelLoadErrors)
                _settingsWindow.SetModelLoadError(modelId, error);
            if (_loadingModelId != null) _settingsWindow.SetModelLoading(_loadingModelId);
            else if (_controllerModelId != null) _settingsWindow.SetModelLoaded(_controllerModelId);
            _settingsWindow.SetDevices(devices, _settings.MicrophoneEndpointId);
            _settingsWindow.SetStartWithWindows(_settings.StartWithWindows);
            _settingsWindow.SetRecoveryTranscript(_recoveryTranscript);
            if (deviceError != null) _settingsWindow.SetStatus(deviceError, isError: true);
            try
            {
                _dictionaryEntries = DictionaryFile.LoadOrCreate();
                _settingsWindow.SetDictionary(_dictionaryEntries);
            }
            catch (Exception ex)
            {
                AppLogger.LogException("DictionaryFile.LoadOrCreate", ex);
                _settingsWindow.SetDictionaryError(UiText.Text("DictionaryLoadFailed"));
            }
            RefreshHistory();
            _settingsWindow.Show();
            _settingsWindow.Activate();
        });
    }

    private void OnDictationCompleted(CompletedDictation completed)
    {
        var settings = Volatile.Read(ref _settings);
        if (!settings.HistoryEnabled)
        {
            return;
        }

        try
        {
            var id = Guid.NewGuid();
            _historyStore.Append(new DictationHistoryEntry(
                id,
                completed.Timestamp,
                completed.Engine,
                completed.AudioDurationMs,
                completed.InferenceDurationMs,
                completed.Text));
            _historyStore.Prune(
                DateTimeOffset.Now,
                settings.HistoryRetentionDays,
                settings.HistoryMaxEntries);
            _historyError = null;
            AppLogger.Log($"History entry saved: id={id} retention_days={settings.HistoryRetentionDays} max_entries={settings.HistoryMaxEntries}");

            if (_settingsWindow != null)
            {
                Dispatcher.UIThread.Post(RefreshHistory);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogException("HistoryStore.Append", ex);
            _historyError = UiText.Text("HistorySaveFailed");
            _settingsWindow?.SetHistoryError(_historyError);
        }
    }

    private void RefreshHistory()
    {
        try
        {
            var entries = _historyStore.Load(out var skippedLineCount);
            if (skippedLineCount > 0)
            {
                AppLogger.Log($"History loaded with skipped_lines={skippedLineCount}");
            }

            _settingsWindow?.SetHistory(entries);
            if (_historyError != null)
            {
                _settingsWindow?.SetHistoryError(_historyError);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogException("HistoryStore.Load", ex);
            _settingsWindow?.SetHistoryUnavailable(UiText.Text("HistoryUnavailable"));
        }
    }

    private void OnHistoryDeleteRequested(Guid id)
    {
        try
        {
            _historyStore.Delete(id);
            AppLogger.Log($"History entry deleted: id={id}");
            RefreshHistory();
        }
        catch (Exception ex)
        {
            AppLogger.LogException("HistoryStore.Delete", ex);
            _settingsWindow?.SetHistoryError(UiText.Text("HistoryEntryDeleteFailed"));
        }
    }

    private void OnHistoryClearRequested()
    {
        try
        {
            _historyStore.Clear();
            AppLogger.Log("History cleared by user.");
            RefreshHistory();
        }
        catch (Exception ex)
        {
            AppLogger.LogException("HistoryStore.Clear", ex);
            _settingsWindow?.SetHistoryError(UiText.Text("HistoryClearFailed"));
        }
    }

    private void OnDictionaryChanged(IReadOnlyList<DictionaryEntry> entries)
    {
        try
        {
            DictionaryFile.Save(entries);
            _dictionaryEntries = entries.ToArray();
            _controller?.SetDictionary(_dictionaryEntries);
            AppLogger.Log($"Dictionary updated: entries={_dictionaryEntries.Count}");
        }
        catch (Exception ex)
        {
            AppLogger.LogException("DictionaryFile.Save", ex);
            _settingsWindow?.SetDictionaryError(UiText.Text("DictionarySaveFailed"));
        }
    }

    private async void OnSettingsSaveRequested(SettingsSaveRequest req)
    {
        AppLogger.Log($"SettingsSaveRequested: StartWithWindows={req.StartWithWindows}, HotkeyId={req.HotkeyId}, HudPlacement={req.HudPlacement}, SpeechModelId={req.SpeechModelId}, RemoveFillers={req.RemoveFillers}, Language={req.Language}, AudioFeedbackEnabled={req.AudioFeedbackEnabled}, ActiveProfileId={req.ActiveProfileId}");

        var prevSettings = _settings;
        var nextSettings = _settings with
        {
            MicrophoneEndpointId = req.MicrophoneEndpointId,
            StartWithWindows = req.StartWithWindows,
            HotkeyId = req.HotkeyId,
            HudPlacement = req.HudPlacement,
            SpeechModelId = req.SpeechModelId,
            RemoveFillers = req.RemoveFillers,
            Language = req.Language,
            AudioFeedbackEnabled = req.AudioFeedbackEnabled,
            Profiles = req.Profiles ?? _settings.Profiles,
            ActiveProfileId = !string.IsNullOrEmpty(req.ActiveProfileId) ? req.ActiveProfileId : _settings.ActiveProfileId,
            TextInsertionMode = req.TextInsertionMode,
            HistoryEnabled = req.HistoryEnabled,
            HistoryRetentionDays = req.HistoryRetentionDays,
            HistoryMaxEntries = req.HistoryMaxEntries,
            EnableSelfCorrection = req.EnableSelfCorrection,
            RestorePreviousWindowAfterInjection = req.RestorePreviousWindowAfterInjection
        };

        nextSettings = nextSettings.Normalize();
        try
        {
            _settingsStore.Save(nextSettings);
        }
        catch (IOException ex)
        {
            AppLogger.LogException("SettingsStore.Save", ex);
            _settingsWindow?.SetStatus(UiText.Text("SettingsSaveFailed"), isError: true);
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLogger.LogException("SettingsStore.Save", ex);
            _settingsWindow?.SetStatus(UiText.Text("SettingsSaveFailed"), isError: true);
            return;
        }
        catch (System.Text.Json.JsonException ex)
        {
            AppLogger.LogException("SettingsStore.Save", ex);
            _settingsWindow?.SetStatus(UiText.Text("SettingsSaveFailed"), isError: true);
            return;
        }

        _settings = nextSettings;
        UiText.SetLanguage(_settings.Language);
        RefreshTrayLocalization();
        _hud?.RefreshLocalizedLabels();

        try
        {
            if (_settings.StartWithWindows)
            {
                WindowsStartup.Enable();
            }
            else
            {
                WindowsStartup.Disable();
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogException("WindowsStartup", ex);
        }

        if (_controller != null && !RequiresControllerRestart(prevSettings, _settings))
        {
            _controller.SetTextInsertionMode(_settings.TextInsertionMode);
            _controller.SetSelfCorrectionEnabled(_settings.EnableSelfCorrection);
            _controller.SetRestorePreviousWindow(_settings.RestorePreviousWindowAfterInjection);
        }

        await StopMicrophoneTestAsync();
        _settingsWindow?.Close();

        _hud?.SetPlacement(_settings.HudPlacement);

        if (_controller != null && !RequiresControllerRestart(prevSettings, _settings))
        {
            ApplyActiveProfile();
            SetupProfileToggleHook();
        }
        else
        {
            await StartControllerAsync();
        }
    }

    private void OnSettingsRefreshRequested()
    {
        try
        {
            var devices = WasapiAudioCapture.GetActiveDevices();
            _settingsWindow?.SetDevices(devices, _settings.MicrophoneEndpointId);
        }
        catch (Exception ex)
        {
            AppLogger.LogException("Refresh audio devices", ex);
            _settingsWindow?.SetDevices([], _settings.MicrophoneEndpointId);
            _settingsWindow?.SetStatus(UiText.Format("AppError", ex.Message), isError: true);
        }
    }

    private async void OnSettingsDownloadRequested(string modelId)
    {
        if (_shuttingDown || _downloadCts != null)
        {
            return;
        }

        var model = ModelManifest.Get(modelId);
        var downloadCts = new CancellationTokenSource();
        var downloadSucceeded = false;
        var generation = Interlocked.Increment(ref _downloadGeneration);
        _downloadCts = downloadCts;
        bool IsCurrent() => ReferenceEquals(_downloadCts, downloadCts);
        bool AcceptUpdate() => IsCurrent()
            && AcceptDownloadUpdate(
                _settingsWindow?.SelectedSpeechModelId,
                model.Id,
                generation,
                Volatile.Read(ref _downloadGeneration));
        var progress = new Progress<double>(p =>
        {
            if (AcceptUpdate())
            {
                _settingsWindow?.SetModelProgress(p);
            }
        });

        try
        {
            if (AcceptUpdate())
            {
                _settingsWindow?.SetStatus(UiText.Format("DownloadingModel", model.DisplayName));
            }

            var downloader = new ModelDownloader();
            await downloader.DownloadModelAsync(model, progress: progress, ct: downloadCts.Token);
            downloadSucceeded = true;
            _modelLoadErrors.Remove(model.Id);
            if (AcceptUpdate())
            {
                _settingsWindow?.SetStatus(UiText.Text("ModelDownloaded"));
            }
        }
        catch (OperationCanceledException)
        {
            if (AcceptUpdate())
            {
                _settingsWindow?.SetStatus(UiText.Text("DownloadCancelled"));
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogException("OnSettingsDownloadRequested", ex);
            if (AcceptUpdate())
            {
                _settingsWindow?.SetStatus(UiText.Format("DownloadFailed", ex.Message), isError: true);
            }
        }
        finally
        {
            if (IsCurrent())
            {
                _settingsWindow?.CompleteModelDownload(model.Id, downloadSucceeded);
                _downloadCts = null;
                downloadCts.Dispose();
            }
        }
    }

    internal static bool AcceptDownloadUpdate(
        string? selectedModelId,
        string downloadModelId,
        int downloadGeneration,
        int currentGeneration)
    {
        return downloadGeneration == currentGeneration
            && string.Equals(selectedModelId, downloadModelId, StringComparison.OrdinalIgnoreCase);
    }

    private void OnSettingsDownloadCancelRequested(object? sender, EventArgs e)
    {
        _downloadCts?.Cancel();
    }

    private async void OnSettingsMicrophoneTestRequested()
    {
        if (_shuttingDown) return;
        bool isRunning;
        lock (_testCaptureLock)
        {
            isRunning = _testCapture != null;
        }

        if (isRunning)
        {
            await StopMicrophoneTestAsync();
            return;
        }

        var generation = 0;
        CancellationToken timerToken = default;
        try
        {
            var selectedId = _settings.MicrophoneEndpointId;
            var capture = new WasapiAudioCapture(selectedId);
            capture.SessionStopped += () => LogCaptureDiagnostics(capture);
            capture.LevelChanged += level => _settingsWindow?.SetMicrophoneLevel(level);

            lock (_testCaptureLock)
            {
                generation = ++_testCaptureGeneration;
                _testCapture = capture;
                capture.Start();

                _testCts = new CancellationTokenSource();
                timerToken = _testCts.Token;
            }

            _ = StopMicrophoneTestAfterDelayAsync(generation, timerToken);
        }
        catch (Exception ex)
        {
            AppLogger.LogException("OnSettingsMicrophoneTestRequested", ex);
            _settingsWindow?.SetStatus(UiText.Format("MicrophoneTestFailed", ex.Message), isError: true);
            await StopMicrophoneTestAsync(generation == 0 ? null : generation);
        }
    }

    private async Task StopMicrophoneTestAfterDelayAsync(int generation, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await StopMicrophoneTestAsync(generation).ConfigureAwait(false);
    }

    private async Task StopMicrophoneTestAsync(int? expectedGeneration = null)
    {
        WasapiAudioCapture? capture;
        CancellationTokenSource? cts;
        lock (_testCaptureLock)
        {
            if (expectedGeneration.HasValue && expectedGeneration.Value != _testCaptureGeneration)
            {
                return;
            }

            _testCaptureGeneration++;
            capture = _testCapture;
            _testCapture = null;
            cts = _testCts;
            _testCts = null;
        }

        cts?.Cancel();
        cts?.Dispose();
        try
        {
            if (capture != null)
            {
                try
                {
                    await capture.StopAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AppLogger.LogException("StopMicrophoneTestAsync", ex);
                }
                finally
                {
                    capture.Dispose();
                }
            }
        }
        finally
        {
            Dispatcher.UIThread.Post(() => _settingsWindow?.SetMicrophoneLevel(0));
        }
    }

    private static void LogCaptureDiagnostics(WasapiAudioCapture capture)
    {
        if (capture.Format is not { } format || capture.LastSessionStats is not { } stats)
        {
            return;
        }

        var durationMs = stats.OutputSamples * 1000.0 / 16000.0;
        AppLogger.Log(FormattableString.Invariant(
            $"Capture diagnostics: microphone={capture.FriendlyName ?? "unknown"}, format={format.SampleRate}Hz/{format.Channels}ch/{format.BitsPerSample}bit/{format.Encoding}, output_samples={stats.OutputSamples}, duration_ms={durationMs:F0}, rms={stats.Rms:F6}, peak={stats.Peak:F6}, clipped_samples={stats.ClippedSamples}"));
    }

    private string GetTrayStatusText() => _trayStatus switch
    {
        TrayStatusKind.LoadingSpeechModel => UiText.Text("LoadingSpeechModel"),
        TrayStatusKind.Error => $"Murmur ({UiText.HudError})",
        _ => UiText.TrayReady
    };

    private void UpdateTrayStatus(TrayStatusKind status)
    {
        _trayStatus = status;
        Dispatcher.UIThread.Post(() =>
        {
            var trayIcons = TrayIcon.GetIcons(this);
            if (trayIcons != null && trayIcons.Count > 0)
            {
                var icon = trayIcons[0];
                if (icon.Menu?.Items.Count > 0 && icon.Menu.Items[0] is NativeMenuItem item)
                {
                    item.Header = GetTrayStatusText();
                }
            }
        });
    }

    private async Task CheckForUpdatesAsync()
    {
        if (Interlocked.Exchange(ref _updateCheckInProgress, 1) != 0)
        {
            return;
        }

        try
        {
            var latest = await _updateChecker.GetLatestStableReleaseAsync(_updateCts.Token);
            if (latest is null)
            {
                AppLogger.Log("Update check completed without usable release metadata.");
                return;
            }

            if (!ReleaseUpdateChecker.IsNewerThan(GetCurrentVersion(), latest))
            {
                return;
            }

            _availableRelease = latest;
            Dispatcher.UIThread.Post(() =>
            {
                var menuItem = FindUpdateAvailableMenuItem();
                if (menuItem is null)
                {
                    return;
                }

                menuItem.Header = UiText.Format("UpdateAvailableWithVersion", latest.Tag);
                menuItem.IsEnabled = true;
                menuItem.IsVisible = true;
            });
            AppLogger.Log($"Update available: {latest.Tag}");
        }
        catch (Exception ex)
        {
            AppLogger.LogException("Update check", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _updateCheckInProgress, 0);
        }
    }

    private static Version GetCurrentVersion()
    {
        var version = typeof(App).Assembly.GetName().Version;
        return version is null
            ? new Version(0, 0, 0)
            : new Version(version.Major, Math.Max(version.Minor, 0), Math.Max(version.Build, 0));
    }

    private async void OnCheckForUpdatesClick(object? sender, EventArgs e)
    {
        await CheckForUpdatesAsync();
    }

    private void OnUpdateAvailableClick(object? sender, EventArgs e)
    {
        var release = _availableRelease;
        if (release is null ||
            release.ReleaseUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(release.ReleaseUri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !release.ReleaseUri.AbsolutePath.StartsWith(
                $"/{ReleaseUpdateChecker.Repository}/releases/tag/",
                StringComparison.Ordinal))
        {
            AppLogger.Log("Refused to open an invalid update URL.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = release.ReleaseUri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.LogException("Open update release page", ex);
        }
    }
    private NativeMenuItem? FindUpdateAvailableMenuItem()
    {
        if (_updateAvailableMenuItem is not null)
        {
            return _updateAvailableMenuItem;
        }

        var trayIcons = TrayIcon.GetIcons(this);
        if (trayIcons is null)
        {
            return null;
        }

        foreach (var icon in trayIcons)
        {
            var items = icon.Menu?.Items;
            var item = items?
                .OfType<NativeMenuItem>()
                .FirstOrDefault(candidate => string.Equals(
                    candidate.CommandParameter as string,
                    UpdateAvailableMenuItemId,
                    StringComparison.Ordinal));
            if (item is null)
            {
                continue;
            }

            _updateAvailableMenuItem = item;
            return item;
        }

        return null;
    }

    private void OnSettingsClick(object? sender, EventArgs e)
    {
        ShowSettings();
    }

    private void OnOpenLogFolderClick(object? sender, EventArgs e)
    {
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Murmur");
            if (Directory.Exists(logDir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{logDir}\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogException("OnOpenLogFolderClick", ex);
        }
    }

    private void OnStartWithWindowsClick(object? sender, EventArgs e)
    {
        var newState = !_settings.StartWithWindows;
        _settings = _settings with { StartWithWindows = newState };
        _settingsStore.Save(_settings);
        try
        {
            if (newState)
            {
                WindowsStartup.Enable();
            }
            else
            {
                WindowsStartup.Disable();
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogException("WindowsStartup", ex);
        }
    }

    private async void OnExitClick(object? sender, EventArgs e)
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        Interlocked.Increment(ref _controllerGeneration);
        AppLogger.Log("App exiting via Tray Exit.");
        _updateCts.Cancel();
        _downloadCts?.Cancel();
        if (_initializationTask != null) await _initializationTask;
        await StopMicrophoneTestAsync();
        await _controllerGate.WaitAsync();
        try
        {
            _profileToggleHook?.Dispose();
            _profileToggleHook = null;
            await DisposeControllerAsync();
        }
        catch (Exception ex) { AppLogger.LogException("Controller shutdown", ex); }
        finally { _controllerGate.Release(); }
        _updateChecker.Dispose();
        _updateCts.Dispose();
        _recoveryTranscript = null;
        _clickSoundPlayer.Dispose();
        _hud?.Close();
        _settingsWindow?.Close();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
