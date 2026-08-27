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
    private LowLevelKeyboardHook? _profileToggleHook;
    private HudWindow? _hud;
    private SettingsWindow? _settingsWindow;
    private WasapiAudioCapture? _testCapture;
    private CancellationTokenSource? _testCts;
    private readonly Lock _testCaptureLock = new();
    private int _testCaptureGeneration;
    private CancellationTokenSource? _downloadCts;
    private readonly ClickSoundPlayer _clickSoundPlayer = new();
    private string? _clickSoundPath;
    private readonly ReleaseUpdateChecker _updateChecker = new();
    private readonly CancellationTokenSource _updateCts = new();
    private ReleaseInfo? _availableRelease;
    private int _updateCheckInProgress;
    private NativeMenuItem? _updateAvailableMenuItem;
    private TrayStatusKind _trayStatus = TrayStatusKind.Ready;

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

            _ = InitializeMurmurAsync();
            _ = CheckForUpdatesAsync();
        }

        base.OnFrameworkInitializationCompleted();
        AppLogger.Log("App.OnFrameworkInitializationCompleted() completed.");
    }

    private async Task InitializeMurmurAsync()
    {
        _clickSoundPath = Path.Combine(
            AppContext.BaseDirectory,
            "Sounds",
            "feedback.wav");

        _settings = _settingsStore.Load().Normalize();
        UiText.SetLanguage(_settings.Language);
        AppLogger.Log($"Settings loaded: StartWithWindows={_settings.StartWithWindows}, SetupCompleted={_settings.SetupCompleted}, HotkeyId={_settings.HotkeyId}, HudPlacement={_settings.HudPlacement}, SpeechModelId={_settings.SpeechModelId}, Language={_settings.Language}, AudioFeedbackEnabled={_settings.AudioFeedbackEnabled}, ActiveProfileId={_settings.ActiveProfileId}");
        var currentExecutable = Environment.ProcessPath;
        var registeredExecutable = WindowsStartup.GetRegisteredExecutablePath();
        var startupDiffers = currentExecutable != null && registeredExecutable != null &&
            !string.Equals(Path.GetFullPath(currentExecutable), Path.GetFullPath(registeredExecutable), StringComparison.OrdinalIgnoreCase);
        AppLogger.Log($"Startup executable differs from current: {startupDiffers}");

        if (_settings.StartWithWindows && !WindowsStartup.IsEnabled())
        {
            try
            {
                WindowsStartup.Enable();
            }
            catch (Exception ex)
            {
                AppLogger.LogException("WindowsStartup.Enable on init", ex);
            }
        }

        var devices = WasapiAudioCapture.GetActiveDevices();
        AppLogger.Log($"Active audio input devices: {devices.Count}");
        foreach (var dev in devices)
        {
            AppLogger.Log($"  Device: {dev.Name} (Default: {dev.IsDefault})");
        }

        var model = ModelManifest.Get(_settings.SpeechModelId);
        var modelAvailable = await Task.Run(() => ModelLocator.IsModelDownloaded(model));
        if (!_settings.SetupCompleted || !modelAvailable || devices.Count == 0)
        {
            AppLogger.Log("Setup incomplete or model missing. Showing settings window.");
            ShowSettings();
            return;
        }

        await StartControllerAsync();
    }

    private static ITextFormatter CreateFormatter(DictationProfile profile)
    {
        return profile.FormatterKind == ProfileFormatterKind.Math
            ? new GermanMathFormatter(profile.MathMode, profile.RemoveFillers)
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
            _profileToggleHook = new LowLevelKeyboardHook(_settings.ProfileToggleHotkeyId);
            _profileToggleHook.Pressed += OnProfileToggleHotkeyPressed;
            _profileToggleHook.Start();
            AppLogger.Log($"Profile toggle hook started: {_settings.ProfileToggleHotkeyId} (vk={toggleOption.VirtualKey})");
        }
        catch (Exception ex)
        {
            AppLogger.LogException("SetupProfileToggleHook", ex);
            _profileToggleHook?.Dispose();
            _profileToggleHook = null;
        }
    }

    private void OnProfileToggleHotkeyPressed()
    {
        Dispatcher.UIThread.Post(() =>
        {
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
        try
        {
            _profileToggleHook?.Dispose();
            _profileToggleHook = null;

            if (_controller != null)
            {
                _controller.TextInjected -= OnTextInjected;
                _controller.DictationCompleted -= OnDictationCompleted;
                _controller.Dispose();
                _controller = null;
            }

            var model = ModelManifest.Get(_settings.SpeechModelId);
            if (!await Task.Run(() => ModelLocator.IsModelDownloaded(model)))
            {
                ShowSettings();
                return;
            }

            _settings = _settings.Normalize();
            var activeProfile = _settings.ActiveProfile;
            var initialFormatter = CreateFormatter(activeProfile);

            _hud?.SetPlacement(_settings.HudPlacement);
            UpdateTrayStatus(TrayStatusKind.LoadingSpeechModel);
            _settingsWindow?.SetStatus(UiText.Text("LoadingSpeechModel"));

            var sw = Stopwatch.StartNew();
            ITranscriber transcriber = model.SupportsLivePreview
                ? await Task.Run(() => new NemotronStreamingTranscriber(ModelLocator.GetDefaultModelDirectory(model)))
                : await Task.Run(() => new SherpaOnnxTranscriber(ModelLocator.GetDefaultModelDirectory(model)));
            sw.Stop();
            AppLogger.Log($"Transcriber initialized in {sw.ElapsedMilliseconds} ms (Model: {model.Id}, live_preview={model.SupportsLivePreview})");

            var audioCapture = new WasapiAudioCapture(_settings.MicrophoneEndpointId);
            audioCapture.SessionStopped += () => LogCaptureDiagnostics(audioCapture);
            var hotkeyHook = new LowLevelKeyboardHook(_settings.HotkeyId);
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

            _controller = new DictationController(
                audioCapture,
                hotkeyHook,
                transcriber,
                injector,
                model.DisplayName,
                initialFormatter,
                dictionaryCorrector,
                activeProfile);

            _controller.DictationCompleted += OnDictationCompleted;
            _controller.TextInjected += OnTextInjected;

            _controller.StateChanged += state =>
            {
                AppLogger.Log($"StateChanged: {state.Status} (Message: {state.Message ?? "none"})");
                _hud?.UpdateState(state, _controller.Level, _controller.CurrentTranscript);
            };

            _controller.LevelChanged += level =>
            {
                _hud?.UpdateLevel(_controller.State.Status, level);
            };

            _controller.TranscriptChanged += t =>
            {
                AppLogger.Log($"TranscriptChanged: {t.Length} chars");
                _hud?.UpdateState(_controller.State, _controller.Level, t);
            };

            _controller.MetricsRecorded += m =>
            {
                AppLogger.Log($"Dictation metrics: audio_ms={m.AudioDurationMs:F0} inference_ms={m.InferenceDurationMs:F0} rtf={m.RealTimeFactor:F3} chars={m.TranscriptLength}");
            };

            _controller.Start();
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
            _settingsWindow?.SetStatus(UiText.Text("Ready"));
        }
        catch (Exception ex)
        {
            AppLogger.LogException("StartControllerAsync", ex);
            UpdateTrayStatus(TrayStatusKind.Error);
            _settingsWindow?.SetStatus(UiText.Format("AppError", ex.Message), isError: true);
            ShowSettings();
        }
    }

    private void OnTextInjected(CompletedDictation completed)
    {
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

    private void ShowSettings()
    {
        Dispatcher.UIThread.Post(() =>
        {
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
                _settingsWindow.Closed += (s, e) =>
                {
                    _ = StopMicrophoneTestAsync();
                    _settingsWindow = null;
                };
            }

            var devices = WasapiAudioCapture.GetActiveDevices();
            _settingsWindow.SetChoices(_settings);
            _settingsWindow.SetDevices(devices, _settings.MicrophoneEndpointId);
            _settingsWindow.SetStartWithWindows(_settings.StartWithWindows);
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
            _historyError = null;
            AppLogger.Log($"History entry saved: id={id} chars={completed.Text.Length}");

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
        _settings = _settings with
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
            ActiveProfileId = !string.IsNullOrEmpty(req.ActiveProfileId) ? req.ActiveProfileId : _settings.ActiveProfileId
        };
        _settings = _settings.Normalize();
        UiText.SetLanguage(_settings.Language);
        _settingsStore.Save(_settings);
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
        var devices = WasapiAudioCapture.GetActiveDevices();
        _settingsWindow?.SetDevices(devices, _settings.MicrophoneEndpointId);
    }

    private async void OnSettingsDownloadRequested(string modelId)
    {
        if (_downloadCts != null)
        {
            return;
        }

        var model = ModelManifest.Get(modelId);
        _downloadCts = new CancellationTokenSource();
        var progress = new Progress<double>(p => _settingsWindow?.SetModelProgress(p));

        try
        {
            _settingsWindow?.SetStatus(UiText.Format("DownloadingModel", model.DisplayName));
            var downloader = new ModelDownloader();
            await downloader.DownloadModelAsync(model, progress: progress, ct: _downloadCts.Token);
            _settingsWindow?.SetStatus(UiText.Text("ModelDownloaded"));
            _settingsWindow?.UpdateModelStatus(model.Id);
        }
        catch (OperationCanceledException)
        {
            _settingsWindow?.SetStatus(UiText.Text("DownloadCancelled"));
            _settingsWindow?.UpdateModelStatus(model.Id);
        }
        catch (Exception ex)
        {
            AppLogger.LogException("OnSettingsDownloadRequested", ex);
            _settingsWindow?.SetStatus(UiText.Format("DownloadFailed", ex.Message), isError: true);
            _settingsWindow?.UpdateModelStatus(model.Id);
        }
        finally
        {
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    private void OnSettingsDownloadCancelRequested(object? sender, EventArgs e)
    {
        _downloadCts?.Cancel();
    }

    private async void OnSettingsMicrophoneTestRequested()
    {
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
        AppLogger.Log("App exiting via Tray Exit.");
        _updateCts.Cancel();
        _updateChecker.Dispose();
        _updateCts.Dispose();
        await StopMicrophoneTestAsync();
        _profileToggleHook?.Dispose();
        _profileToggleHook = null;
        _controller?.Dispose();
        _clickSoundPlayer.Dispose();
        _hud?.Close();
        _settingsWindow?.Close();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
