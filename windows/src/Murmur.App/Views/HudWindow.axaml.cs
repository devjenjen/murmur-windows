using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Platform;
using Avalonia.Threading;
using Murmur.App.Localization;
using Murmur.Core.Settings;
using Murmur.Core.State;
using Murmur.Platform.Windows.Native;

namespace Murmur.App.Views;

/// <summary>
/// Non-activating floating HUD window displaying live transcription status and audio waveform.
/// </summary>
public partial class HudWindow : Window
{
    private static readonly ITransform TransformTranslate0 = TransformOperations.Parse("translateY(0px)");
    private static readonly ITransform TransformTranslate2 = TransformOperations.Parse("translateY(2px)");
    private static readonly ITransform TransformTranslate4 = TransformOperations.Parse("translateY(4px)");
    private static readonly ITransform TransformScale1 = TransformOperations.Parse("scale(1)");
    private static readonly ITransform TransformScaleOrbStart = TransformOperations.Parse("scale(0.86)");

    private string _placement = AppSettings.DefaultHudPlacement;
    private DispatcherTimer? _hideTimer;
    private int _hideGeneration;
    private DispatcherTimer? _processingTimer;
    private DispatcherTimer? _contentRevealTimer;
    private bool _contentVisible;
    private double _processingPhase;
    private DictationStatus? _lastStatus;
    private string? _lastTranscript;
    private string? _lastErrorMessage;
    private double _recordingWidth;
    private (nint Handle, PixelRect Bounds, int CornerDiameter)? _appliedNativeRegion;
    private nint? _platformHandle;
    private int _levelUpdatePending;
    private float _pendingLevel;
    private DictationStatus _pendingLevelStatus;
    private readonly ConicGradientBrush _rainbowBrush;
    private bool _rainbowAnimating;
    private bool _rainbowFrameRequested;

    private const double RainbowRestAngle = -90;
    private const double RainbowDegreesPerSecond = 120;

    /// <summary>Reports whether Windows animation effects are off; replaceable in tests.</summary>
    internal Func<bool> IsReducedMotion { get; set; } = ReadSystemReducedMotion;

    /// <summary>True while the rainbow rim is requesting animation frames.</summary>
    internal bool IsRainbowAnimating => _rainbowAnimating;

    /// <summary>Initializes a new instance of HudWindow.</summary>
    public HudWindow()
    {
        InitializeComponent();
        _rainbowBrush = (ConicGradientBrush)RainbowRim.Resources["HudRainbowBrush"]!;
        ApplyPlacementAlignment();
        LayoutUpdated += OnLayoutUpdated;
    }

    /// <summary>Sets the HUD placement anchor.</summary>
    public void SetPlacement(string placement)
    {
        _placement = string.IsNullOrWhiteSpace(placement) ? AppSettings.DefaultHudPlacement : placement;
        ApplyPlacementAlignment();
        if (IsVisible)
        {
            PositionForForegroundWindow();
        }
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _platformHandle = TryGetPlatformHandle()?.Handle;
        var handle = _platformHandle;
        if (handle.HasValue && handle.Value != 0)
        {
            var current = Win32.GetWindowLongPtrW(handle.Value, Win32.GWL_EXSTYLE);
            var updated = current
                | Win32.WS_EX_NOACTIVATE
                | Win32.WS_EX_TOOLWINDOW
                | Win32.WS_EX_TRANSPARENT;
            Win32.SetWindowLongPtrW(handle.Value, Win32.GWL_EXSTYLE, updated);
        }

        if (ActualTransparencyLevel == WindowTransparencyLevel.None)
        {
            HudSurface.Background = GetBrush("HudSolidFallbackBrush", Brushes.Black);
        }
        else
        {
            HudSurface.Background = GetBrush("HudChassisBrush", Brushes.Transparent);
        }

        ApplyNativeRegion();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        CancelContentReveal();
        StopProcessingAnimation();
        _hideTimer?.Stop();
        LayoutUpdated -= OnLayoutUpdated;
        _appliedNativeRegion = null;
        _platformHandle = null;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        ApplyNativeRegion();
    }

    private void ApplyPlacementAlignment()
    {
        HudSurface.HorizontalAlignment = _placement.EndsWith("left", StringComparison.Ordinal)
            ? Avalonia.Layout.HorizontalAlignment.Left
            : _placement.EndsWith("right", StringComparison.Ordinal)
                ? Avalonia.Layout.HorizontalAlignment.Right
                : Avalonia.Layout.HorizontalAlignment.Center;

        HudContentSurface.HorizontalAlignment = HudSurface.HorizontalAlignment;
        HudSurface.VerticalAlignment = _placement.StartsWith("top-", StringComparison.Ordinal)
            ? Avalonia.Layout.VerticalAlignment.Top
            : Avalonia.Layout.VerticalAlignment.Bottom;
        HudContentSurface.VerticalAlignment = HudSurface.VerticalAlignment;

        var originX = HudSurface.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Left
            ? 0
            : HudSurface.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Right ? 1 : 0.5;
        var originY = HudSurface.VerticalAlignment == Avalonia.Layout.VerticalAlignment.Top ? 0 : 1;
        var origin = new RelativePoint(originX, originY, RelativeUnit.Relative);
        HudSurface.RenderTransformOrigin = origin;
        HudContentSurface.RenderTransformOrigin = origin;
    }

    private void PositionForForegroundWindow()
    {
        var screen = GetTargetScreen();
        if (screen == null) return;

        var area = screen.WorkingArea;
        var pixelWidth = (int)Math.Ceiling(HudViewport.Bounds.Width * screen.Scaling);
        var pixelHeight = (int)Math.Ceiling(HudViewport.Bounds.Height * screen.Scaling);
        if (pixelWidth <= 0)
        {
            pixelWidth = (int)Math.Ceiling(GetDouble("HudViewportWidth", 488) * screen.Scaling);
        }
        if (pixelHeight <= 0)
        {
            pixelHeight = (int)Math.Ceiling(GetDouble("HudViewportHeight", 83) * screen.Scaling);
        }

        var x = _placement switch
        {
            "top-left" or "bottom-left" => area.X + 24,
            "top-right" or "bottom-right" => area.Right - pixelWidth - 24,
            _ => area.X + (area.Width - pixelWidth) / 2
        };

        var y = _placement.StartsWith("top-", StringComparison.Ordinal)
            ? area.Y + 24
            : area.Bottom - pixelHeight - 24;

        Position = new PixelPoint(x, y);
    }

    private Screen? GetTargetScreen()
    {
        try
        {
            var fgHwnd = Win32.GetForegroundWindow();
            if (fgHwnd != 0 && Win32.GetWindowRect(fgHwnd, out var rect))
            {
                var centerX = rect.Left + (rect.Right - rect.Left) / 2;
                var centerY = rect.Top + (rect.Bottom - rect.Top) / 2;
                var targetScreen = Screens.ScreenFromPoint(new PixelPoint(centerX, centerY));
                if (targetScreen != null) return targetScreen;
            }
        }
        catch
        {
            // Fall back to primary screen
        }

        return Screens.Primary;
    }

    private IBrush GetBrush(string key, IBrush fallback)
    {
        if (this.TryFindResource(key, out var resource) && resource is IBrush brush)
        {
            return brush;
        }
        return fallback;
    }

    private double GetDouble(string key, double fallback)
    {
        return this.TryFindResource(key, out var resource) && resource is double value
            ? value
            : fallback;
    }

    private CornerRadius GetCornerRadius(string key, CornerRadius fallback)
    {
        return this.TryFindResource(key, out var resource) && resource is CornerRadius value
            ? value
            : fallback;
    }

    internal static (PixelRect Bounds, int CornerDiameter) CalculateNativeRegion(
        Rect surfaceBounds,
        CornerRadius cornerRadius,
        double scaling)
    {
        const int nativeEdgePadding = 2;
        var safeScaling = scaling > 0 ? scaling : 1;
        var left = (int)Math.Floor(surfaceBounds.X * safeScaling) - nativeEdgePadding;
        var top = (int)Math.Floor(surfaceBounds.Y * safeScaling) - nativeEdgePadding;
        var right = (int)Math.Ceiling((surfaceBounds.X + surfaceBounds.Width) * safeScaling)
            + nativeEdgePadding;
        var bottom = (int)Math.Ceiling((surfaceBounds.Y + surfaceBounds.Height) * safeScaling)
            + nativeEdgePadding;
        var width = Math.Max(1, right - left);
        var height = Math.Max(1, bottom - top);
        var radius = Math.Max(
            Math.Max(cornerRadius.TopLeft, cornerRadius.TopRight),
            Math.Max(cornerRadius.BottomLeft, cornerRadius.BottomRight));
        var cornerDiameter = Math.Clamp(
            (int)Math.Round(radius * 2 * safeScaling),
            1,
            Math.Min(width, height));

        return (new PixelRect(left, top, width, height), cornerDiameter);
    }

    private void ApplyNativeRegion()
    {
        if (!IsVisible)
        {
            return;
        }

        var handle = _platformHandle ?? TryGetPlatformHandle()?.Handle;
        var bounds = HudSurface.Bounds;
        if (!handle.HasValue || handle.Value == 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var region = CalculateNativeRegion(bounds, HudSurface.CornerRadius, RenderScaling);
        var applied = (handle.Value, region.Bounds, region.CornerDiameter);
        if (_appliedNativeRegion == applied)
        {
            return;
        }

        var nativeRegion = Win32.CreateRoundRectRgn(
            region.Bounds.X,
            region.Bounds.Y,
            region.Bounds.Right,
            region.Bounds.Bottom,
            region.CornerDiameter,
            region.CornerDiameter);
        if (nativeRegion == 0)
        {
            return;
        }

        if (Win32.SetWindowRgn(handle.Value, nativeRegion, true))
        {
            _appliedNativeRegion = applied;
        }
        else
        {
            Win32.DeleteObject(nativeRegion);
        }
    }

    private void SetSurfaceTarget(double width, double height, CornerRadius radius, IBrush edgeBrush)
    {
        HudSurface.Width = width;
        HudSurface.Height = height;
        HudSurface.CornerRadius = radius;
        HudSurface.BorderBrush = edgeBrush;
    }

    private void SetContentTarget(double width, double height)
    {
        HudContentSurface.Width = width;
        HudContentSurface.Height = height;
    }

    private void SetContentVisible(bool visible)
    {
        _contentVisible = visible;

        HudContentSurface.Opacity = visible
            ? GetDouble("HudContentRestingOpacity", 1)
            : GetDouble("HudContentStartOpacity", 0);

        HudContentSurface.RenderTransform = visible ? TransformTranslate0 : TransformTranslate4;
    }

    private void CancelContentReveal()
    {
        _contentRevealTimer?.Stop();
        _contentRevealTimer = null;
    }

    private void ScheduleContentReveal()
    {
        CancelContentReveal();

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(
                GetDouble("HudContentRevealDelayMs", 45))
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();

            if (ReferenceEquals(_contentRevealTimer, timer))
            {
                _contentRevealTimer = null;
            }

            if (IsVisible &&
                _lastStatus.HasValue &&
                _lastStatus.Value != DictationStatus.Idle)
            {
                SetContentVisible(true);
            }
        };

        _contentRevealTimer = timer;
        timer.Start();
    }

    private void StartProcessingAnimation()
    {
        ProcessingDots.IsVisible = true;
        _processingPhase = 0;
        _processingTimer?.Stop();
        _processingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(
                GetDouble("HudProcessingDotIntervalMs", 50))
        };
        _processingTimer.Tick += OnProcessingTick;
        _processingTimer.Start();
        StartRainbowRim();
    }

    private void OnProcessingTick(object? sender, EventArgs e)
    {
        _processingPhase += 0.16;
        SetDotOpacity(ProcessingDot1, _processingPhase, 0);
        SetDotOpacity(ProcessingDot2, _processingPhase, 1.7);
        SetDotOpacity(ProcessingDot3, _processingPhase, 3.4);
    }

    private static void SetDotOpacity(Ellipse dot, double phase, double offset)
    {
        var pulse = (Math.Sin(phase + offset) + 1) / 2;
        dot.Opacity = 0.35 + (pulse * 0.65);
    }

    private void StartRainbowRim()
    {
        RainbowRim.Classes.Set("active", true);
        _rainbowAnimating = !IsReducedMotion();
        if (_rainbowAnimating)
        {
            RequestRainbowFrame();
        }
        else
        {
            // Reduced motion: calm, static rainbow.
            _rainbowBrush.Angle = RainbowRestAngle;
        }
    }

    private void StopRainbowRim()
    {
        _rainbowAnimating = false;
        RainbowRim.Classes.Set("active", false);
    }

    private void RequestRainbowFrame()
    {
        if (_rainbowFrameRequested) return;
        _rainbowFrameRequested = true;
        RequestAnimationFrame(OnRainbowFrame);
    }

    private void OnRainbowFrame(TimeSpan time)
    {
        _rainbowFrameRequested = false;
        if (!_rainbowAnimating || !IsVisible)
        {
            _rainbowAnimating = false;
            return;
        }

        _rainbowBrush.Angle = RainbowRestAngle + (time.TotalSeconds * RainbowDegreesPerSecond % 360);
        RequestRainbowFrame();
    }

    private static bool ReadSystemReducedMotion()
    {
        try
        {
            return Win32.SystemParametersInfoW(Win32.SPI_GETCLIENTAREAANIMATION, 0, out var enabled, 0)
                && !enabled;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private void StopProcessingAnimation()
    {
        _processingTimer?.Stop();
        _processingTimer = null;
        StopRainbowRim();
        ProcessingDots.IsVisible = false;
        ProcessingDot1.Opacity = 0.35;
        ProcessingDot2.Opacity = 0.35;
        ProcessingDot3.Opacity = 0.35;
    }

    internal static string BuildTranscriptTail(string? text, int maxWords = 15)
    {
        if (string.IsNullOrWhiteSpace(text) || maxWords <= 0) return string.Empty;

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return string.Empty;

        if (words.Length <= maxWords)
        {
            return string.Join(' ', words);
        }

        var tail = words.Skip(words.Length - maxWords);
        return "… " + string.Join(' ', tail);
    }

    private void UpdateTranscriptText(string text)
    {
        if (string.Equals(TranscriptText.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        TranscriptText.Text = text;
        TranscriptText.Opacity = 1;
        TranscriptText.RenderTransform = TransformTranslate0;
    }

    private static string BuildErrorText(string? message, string? transcript)
    {
        var summary = string.IsNullOrWhiteSpace(message) ? UiText.HudError : message;
        return string.IsNullOrWhiteSpace(transcript)
            ? summary
            : $"{summary}{Environment.NewLine}{transcript}";
    }

    private void BeginOrbEntry()
    {
        ApplyPlacementAlignment();
        CancelContentReveal();

        var diameter = GetDouble("HudOrbDiameter", 52);
        var orbRadius = GetCornerRadius(
            "HudOrbRadius",
            new CornerRadius(26));

        SetSurfaceTarget(
            diameter,
            diameter,
            orbRadius,
            GetBrush("HudHairlineBrush", Brushes.Gray));

        SetContentVisible(false);

        HudSurface.Opacity = 0;
        HudSurface.RenderTransform = TransformScaleOrbStart;

        if (!IsVisible)
        {
            PositionForForegroundWindow();
            Show();
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!IsVisible ||
                _lastStatus == DictationStatus.Idle)
            {
                return;
            }

            HudSurface.Opacity = 1;
            HudSurface.RenderTransform = TransformScale1;

            ScheduleContentReveal();
        }, DispatcherPriority.Render);
    }

    private void CancelPendingHide()
    {
        _hideTimer?.Stop();
        _hideTimer = null;
        _hideGeneration++;
    }

    private void ScheduleHide()
    {
        var generation = _hideGeneration;
        var hideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(GetDouble("HudExitDurationMs", 260))
        };
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            if (ReferenceEquals(_hideTimer, hideTimer))
            {
                _hideTimer = null;
            }

            if (generation == _hideGeneration && IsVisible)
            {
                Hide();
            }
        };
        _hideTimer = hideTimer;
        hideTimer.Start();
    }

    /// <summary>Updates the HUD profile badge text.</summary>
    public void UpdateProfileBadge(DictationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Dispatcher.UIThread.Post(() =>
        {
            ProfileBadge.Text = profile.FormatterKind == ProfileFormatterKind.Math
                ? "MATH"
                : "STD";
        });
    }

    /// <summary>Refreshes all localized labels currently displayed on the HUD without clearing the transcript.</summary>
    public void RefreshLocalizedLabels()
    {
        Dispatcher.UIThread.Post(() =>
        {
            switch (_lastStatus)
            {
                case DictationStatus.Starting:
                    TelemetryBadge.Text = UiText.HudArm;
                    UpdateTranscriptText(UiText.HudHoldToSpeak);
                    break;
                case DictationStatus.Listening:
                    TelemetryBadge.Text = UiText.Text("HudRecording");
                    if (string.IsNullOrWhiteSpace(_lastTranscript))
                    {
                        UpdateTranscriptText(UiText.HudListening);
                    }
                    break;
                case DictationStatus.Finishing:
                    TelemetryBadge.Text = UiText.HudProc;
                    if (string.IsNullOrWhiteSpace(_lastTranscript))
                    {
                        UpdateTranscriptText(UiText.HudTranscribing);
                    }
                    break;
                case DictationStatus.Error:
                    TelemetryBadge.Text = UiText.Text("HudErrorBadge");
                    UpdateTranscriptText(BuildErrorText(_lastErrorMessage, _lastTranscript));
                    break;
            }
        });
    }


    /// <summary>Updates only the HUD meter volume level and active state without triggering layout or state morphs.</summary>
    public void UpdateLevel(DictationStatus status, float level)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            HudMeter.Level = Math.Clamp(level, 0.0f, 1.0f);
            HudMeter.IsActive = status == DictationStatus.Listening;
            return;
        }
        _pendingLevel = level;
        _pendingLevelStatus = status;
        if (Interlocked.CompareExchange(ref _levelUpdatePending, 1, 0) == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var lvl = _pendingLevel;
                var st = _pendingLevelStatus;
                Interlocked.Exchange(ref _levelUpdatePending, 0);

                HudMeter.Level = Math.Clamp(lvl, 0.0f, 1.0f);
                HudMeter.IsActive = st == DictationStatus.Listening;
            }, DispatcherPriority.Input);
        }
    }

    /// <summary>Updates the HUD display with current dictation status, audio volume, and transcript text.</summary>
    public void UpdateState(DictationState state, float level, string transcript)
    {
        ArgumentNullException.ThrowIfNull(state);

        void ApplyState()
        {
            HudMeter.Level = Math.Clamp(level, 0.0f, 1.0f);
            HudMeter.IsActive = state.Status == DictationStatus.Listening;

            if (state.Status == DictationStatus.Idle)
            {
                CancelContentReveal();
                StopProcessingAnimation();

                _lastStatus = DictationStatus.Idle;
                _lastTranscript = null;
                _lastErrorMessage = null;
                _recordingWidth = 0;

                SetSurfaceTarget(
                    GetDouble("HudOrbDiameter", 52),
                    GetDouble("HudArmedHeight", 44),
                    GetCornerRadius("HudOrbRadius", new CornerRadius(26)),
                    GetBrush("HudHairlineBrush", Brushes.Gray));
                SetContentTarget(
                    GetDouble("HudArmedWidth", 245),
                    GetDouble("HudArmedHeight", 44));

                UpdateTranscriptText(UiText.HudHoldToSpeak);
                RecordLamp.Fill = GetBrush("HudIdleLampBrush", Brushes.DarkGray);
                TelemetryBadge.Text = UiText.HudArm;
                AutomationProperties.SetLiveSetting(
                    TranscriptText,
                    AutomationLiveSetting.Polite);

                if (IsVisible)
                {
                    ScheduleHide();
                }
                return;
            }

            CancelPendingHide();

            var statusChanged = _lastStatus != state.Status;
            var transcriptChanged = !string.Equals(_lastTranscript, transcript, StringComparison.Ordinal);
            var errorChanged = !string.Equals(_lastErrorMessage, state.Message, StringComparison.Ordinal);
            var entering = !IsVisible ||
                           _lastStatus is null ||
                           _lastStatus == DictationStatus.Idle;

            if (!statusChanged && !transcriptChanged && !errorChanged && !entering)
            {
                return;
            }

            _lastStatus = state.Status;
            _lastTranscript = transcript;
            _lastErrorMessage = state.Message;

            var idleLamp = GetBrush("HudIdleLampBrush", Brushes.DarkGray);
            var recordingEdge = GetBrush("HudRecordingEdgeBrush", Brushes.Red);
            var processingBrush = GetBrush("HudProcessingBrush", Brushes.DodgerBlue);
            var errorBrush = GetBrush("HudErrorBrush", Brushes.DarkOrange);
            var hairlineBrush = GetBrush("HudHairlineBrush", Brushes.Gray);

            if (entering)
            {
                BeginOrbEntry();
            }

            switch (state.Status)
            {
                case DictationStatus.Starting:
                    _recordingWidth = 0;
                    StopProcessingAnimation();
                    RecordLamp.Fill = idleLamp;
                    TelemetryBadge.Text = UiText.HudArm;
                    UpdateTranscriptText(UiText.HudHoldToSpeak);
                    AutomationProperties.SetLiveSetting(TranscriptText, AutomationLiveSetting.Polite);
                    var orbDiameter = GetDouble("HudOrbDiameter", 52);
                    var orbRadius = GetCornerRadius("HudOrbRadius", new CornerRadius(26));
                    SetSurfaceTarget(orbDiameter, orbDiameter, orbRadius, hairlineBrush);
                    var armedWidth = GetDouble("HudArmedWidth", 245);
                    var armedHeight = GetDouble("HudArmedHeight", 44);
                    SetContentTarget(armedWidth, armedHeight);
                    break;

                case DictationStatus.Listening:
                    StopProcessingAnimation();
                    RecordLamp.Fill = GetBrush("RecordLampBrush", recordingEdge);
                    TelemetryBadge.Text = UiText.Text("HudRecording");
                    var preview = BuildTranscriptTail(transcript, 15);
                    UpdateTranscriptText(string.IsNullOrEmpty(preview) ? UiText.HudListening : preview);
                    AutomationProperties.SetLiveSetting(TranscriptText, AutomationLiveSetting.Polite);

                    var maxTranscriptWidth = GetDouble("HudTranscriptMaxWidth", 288);
                    TranscriptText.Measure(new Size(maxTranscriptWidth, double.PositiveInfinity));
                    var measuredTranscriptWidth = Math.Clamp(
                        TranscriptText.DesiredSize.Width,
                        0,
                        maxTranscriptWidth);

                    var desiredWidth = Math.Clamp(
                        GetDouble("HudRecordingMinWidth", 245) + measuredTranscriptWidth,
                        GetDouble("HudRecordingMinWidth", 245),
                        GetDouble("HudRecordingMaxWidth", 416));

                    var recordingHeight = GetDouble("HudArmedHeight", 44);
                    var recordingRadius = GetCornerRadius("HudRadiusRecording", new CornerRadius(26));
                    var widthStep = GetDouble("HudTranscriptWidthStep", 12);

                    if (_recordingWidth <= 0 || Math.Abs(desiredWidth - _recordingWidth) >= widthStep)
                    {
                        _recordingWidth = desiredWidth;
                        SetSurfaceTarget(_recordingWidth, recordingHeight, recordingRadius, recordingEdge);
                        SetContentTarget(_recordingWidth, recordingHeight);
                    }
                    break;

                case DictationStatus.Finishing:
                    _recordingWidth = 0;
                    HudMeter.IsActive = false;
                    RecordLamp.Fill = idleLamp;
                    TelemetryBadge.Text = UiText.HudProc;

                    preview = BuildTranscriptTail(transcript, 15);
                    UpdateTranscriptText(
                        string.IsNullOrWhiteSpace(preview)
                            ? UiText.HudTranscribing
                            : preview);

                    if (statusChanged)
                    {
                        StartProcessingAnimation();
                    }

                    SetSurfaceTarget(
                        GetDouble("HudProcessingWidth", 301),
                        GetDouble("HudProcessingHeight", 38),
                        GetCornerRadius(
                            "HudRadiusProcessing",
                            new CornerRadius(19)),
                        processingBrush);

                    SetContentTarget(
                        GetDouble("HudProcessingWidth", 301),
                        GetDouble("HudProcessingHeight", 38));

                    AutomationProperties.SetLiveSetting(
                        TranscriptText,
                        AutomationLiveSetting.Polite);

                    break;

                case DictationStatus.Error:
                    CancelContentReveal();
                    StopProcessingAnimation();

                    _recordingWidth = 0;
                    RecordLamp.Fill = errorBrush;
                    TelemetryBadge.Text = UiText.Text("HudErrorBadge");

                    UpdateTranscriptText(BuildErrorText(state.Message, transcript));

                    SetSurfaceTarget(
                        GetDouble("HudErrorWidth", 488),
                        GetDouble("HudErrorHeight", 83),
                        GetCornerRadius(
                            "HudRadiusError",
                            new CornerRadius(14)),
                        errorBrush);

                    SetContentTarget(
                        GetDouble("HudErrorWidth", 488),
                        GetDouble("HudErrorHeight", 83));

                    AutomationProperties.SetLiveSetting(
                        TranscriptText,
                        AutomationLiveSetting.Assertive);

                    SetContentVisible(true);
                    break;
            }
        }
        if (Dispatcher.UIThread.CheckAccess()) ApplyState();
        else Dispatcher.UIThread.Post(ApplyState);
    }
}
