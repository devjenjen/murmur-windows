using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Murmur.App.Controls;

/// <summary>
/// Analog VU meter with damped spring ballistics (300ms attack, 420ms release, 6% overshoot)
/// and discrete 16-segment instrumentation bar.
/// </summary>
public sealed class VUMeterControl : Control
{
    /// <summary>Styled property for audio level (0..1).</summary>
    public static readonly StyledProperty<float> LevelProperty =
        AvaloniaProperty.Register<VUMeterControl, float>(nameof(Level));

    /// <summary>Styled property for active meter state.</summary>
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<VUMeterControl, bool>(nameof(IsActive));

    /// <summary>Styled property for using HUD theme palette.</summary>
    public static readonly StyledProperty<bool> UseHudPaletteProperty =
        AvaloniaProperty.Register<VUMeterControl, bool>(nameof(UseHudPalette));

    static VUMeterControl()
    {
        LevelProperty.Changed.AddClassHandler<VUMeterControl>((c, _) => c.OnLevelOrActiveChanged());
        IsActiveProperty.Changed.AddClassHandler<VUMeterControl>((c, _) => c.OnLevelOrActiveChanged());
        UseHudPaletteProperty.Changed.AddClassHandler<VUMeterControl>((c, _) => c.OnPaletteModeChanged());
        IsVisibleProperty.Changed.AddClassHandler<VUMeterControl>((c, _) => c.OnVisibilityChanged());
    }

    /// <summary>Gets or sets the current normalized audio level.</summary>
    public float Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    /// <summary>Gets or sets whether the meter is actively tracking.</summary>
    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>Gets or sets whether to use HUD palette brushes and geometry.</summary>
    public bool UseHudPalette
    {
        get => GetValue(UseHudPaletteProperty);
        set => SetValue(UseHudPaletteProperty, value);
    }

    private double _position;
    private double _velocity;
    private DispatcherTimer? _timer;
    private bool _isAttached;
    private int _lastReportedLevelBucket = -1;

    /// <summary>Internal flag indicating if animation timer is running (for tests only).</summary>
    internal bool IsAnimationRunning => _timer?.IsEnabled == true;

    internal const int SegmentCount = 16;
    private static readonly IBrush UnlitBrush = new SolidColorBrush(Color.Parse("#262422"));
    private static readonly IBrush GreenBrush = new SolidColorBrush(Color.Parse("#6F9E45"));
    private static readonly IBrush AmberBrush = new SolidColorBrush(Color.Parse("#D39A2E"));
    private static readonly IBrush RedPeakBrush = new SolidColorBrush(Color.Parse("#C0392B"));
    private static readonly IBrush BorderBrush = new SolidColorBrush(Color.Parse("#000000"));
    private static readonly IBrush WellBrush = new SolidColorBrush(Color.Parse("#1A1917"));

    private bool _paletteDirty = true;
    private IBrush _cachedUnlitBrush = UnlitBrush;
    private IBrush _cachedGreenBrush = GreenBrush;
    private IBrush _cachedAmberBrush = AmberBrush;
    private IBrush _cachedPeakBrush = AmberBrush;
    private IBrush _cachedWellBrush = Brushes.Transparent;
    private IPen? _cachedWellPen;
    private double _padX;
    private double _padY = 8.0;
    private double _segGap = 3.0;
    private double _segRadius = 2.0;
    private double _wellRadius = 9.0;
    private double _borderThickness;

    private IBrush ResolveBrush(string key, IBrush fallback)
    {
        return this.TryFindResource(key, ActualThemeVariant, out var resource) && resource is IBrush brush
            ? brush
            : (this.TryFindResource(key, out resource) && resource is IBrush fallbackBrush ? fallbackBrush : fallback);
    }

    private double ResolveDouble(string key, double fallback)
    {
        if (this.TryFindResource(key, ActualThemeVariant, out var resource))
        {
            return resource switch
            {
                double value => value,
                CornerRadius radius => radius.TopLeft,
                Thickness thickness => thickness.Left,
                _ => fallback
            };
        }

        if (this.TryFindResource(key, out resource))
        {
            return resource switch
            {
                double value => value,
                CornerRadius radius => radius.TopLeft,
                Thickness thickness => thickness.Left,
                _ => fallback
            };
        }

        return fallback;
    }

    private void OnPaletteModeChanged()
    {
        _paletteDirty = true;
        InvalidateVisual();
    }

    private void EnsureCachedResources()
    {
        if (!_paletteDirty) return;

        if (UseHudPalette)
        {
            _cachedUnlitBrush = ResolveBrush("HudMeterUnlitBrush", UnlitBrush);
            _cachedGreenBrush = ResolveBrush("MeterGreenBrush", GreenBrush);
            _cachedAmberBrush = ResolveBrush("MeterAmberBrush", AmberBrush);
            _cachedPeakBrush = ResolveBrush("HudMeterPeakBrush", AmberBrush);
            _cachedWellBrush = ResolveBrush("HudMeterWellBrush", Brushes.Transparent);
            _padX = ResolveDouble("HudMeterPaddingX", 4.0);
            _padY = ResolveDouble("HudMeterPaddingY", 4.0);
            _segGap = ResolveDouble("HudMeterSegmentGap", 2.0);
            _segRadius = ResolveDouble("HudMeterSegmentRadius", 1.5);
            _wellRadius = ResolveDouble("HudMeterWellRadius", 3.0);
            _borderThickness = ResolveDouble("HudMeterBorderThickness", 1.0);
            _cachedWellPen = _borderThickness > 0 && _cachedWellBrush is ISolidColorBrush scb && scb.Color.A > 0
                ? new Pen(_cachedWellBrush, _borderThickness)
                : null;
        }
        else
        {
            _cachedUnlitBrush = ResolveBrush("LensMeterUnlitBrush", ResolveBrush("MeterUnlitBrush", UnlitBrush));
            _cachedGreenBrush = ResolveBrush("LensMeterGreenBrush", ResolveBrush("MeterGreenBrush", GreenBrush));
            _cachedAmberBrush = ResolveBrush("LensMeterAmberBrush", ResolveBrush("MeterAmberBrush", AmberBrush));
            _cachedPeakBrush = ResolveBrush("LensMeterPeakBrush", ResolveBrush("MeterPeakBrush", AmberBrush));
            _cachedWellBrush = ResolveBrush("LensWellBrush", Brushes.Transparent);
            _padX = ResolveDouble("SettingsMeterPaddingX", 8.0);
            _padY = ResolveDouble("SettingsMeterPaddingY", 8.0);
            _segGap = ResolveDouble("SettingsMeterSegmentGap", 3.0);
            _segRadius = ResolveDouble("SettingsMeterSegmentRadius", 2.0);
            _wellRadius = ResolveDouble("SettingsMeterWellRadius", 9.0);
            _borderThickness = ResolveDouble("SettingsMeterBorderThickness", 1.0);
            _cachedWellPen = _borderThickness > 0 && _cachedWellBrush is ISolidColorBrush scb && scb.Color.A > 0
                ? new Pen(_cachedWellBrush, _borderThickness)
                : null;
        }

        _paletteDirty = false;
    }

    /// <summary>Initializes a new instance of VUMeterControl.</summary>
    public VUMeterControl()
    {
        AutomationProperties.SetName(this, "Microphone level");
        AutomationProperties.SetItemStatus(this, "0%");
        ActualThemeVariantChanged += (_, _) =>
        {
            _paletteDirty = true;
            InvalidateVisual();
        };
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        _paletteDirty = true;
        EvaluateTimerState();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        StopTimer();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnVisibilityChanged()
    {
        EvaluateTimerState();
    }

    private void OnLevelOrActiveChanged()
    {
        UpdateItemStatus();
        EvaluateTimerState();
        if (!_isAttached || !IsVisible)
        {
            InvalidateVisual();
        }
    }

    private void UpdateItemStatus()
    {
        var rawLevel = IsActive ? Math.Clamp(Level, 0.0f, 1.0f) : 0.0f;
        var bucket = (int)Math.Round(rawLevel * 10.0f) * 10;
        if (bucket != _lastReportedLevelBucket)
        {
            _lastReportedLevelBucket = bucket;
            AutomationProperties.SetItemStatus(this, $"{bucket}%");
        }
    }

    private void EvaluateTimerState()
    {
        if (!_isAttached || !IsVisible)
        {
            StopTimer();
            return;
        }

        var target = IsActive ? Math.Clamp((double)Level, 0.0, 1.0) : 0.0;
        var needsAnimation = IsActive || _position > 0.001 || Math.Abs(target - _position) > 0.001 || Math.Abs(_velocity) > 0.0001;

        if (needsAnimation)
        {
            StartTimer();
        }
        else
        {
            StopTimer();
        }
    }

    private void StartTimer()
    {
        if (_timer == null)
        {
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / 60.0), DispatcherPriority.Render, (_, _) =>
            {
                AdvanceNeedle();
                InvalidateVisual();
            });
        }

        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    private void StopTimer()
    {
        if (_timer != null && _timer.IsEnabled)
        {
            _timer.Stop();
        }
    }

    private void AdvanceNeedle()
    {
        var target = IsActive ? Math.Clamp((double)Level, 0.0, 1.0) : 0.0;
        var delta = target - _position;
        var rising = target > _position;
        var time = rising ? 0.30 : 0.42;
        var stiffness = 1.0 / time;

        _velocity += delta * stiffness * 0.16;
        _velocity *= 0.72;
        _position += _velocity;
        _position = Math.Clamp(_position, 0.0, 1.06);

        if (!IsActive && _position < 0.002 && Math.Abs(_velocity) < 0.0005)
        {
            _position = 0.0;
            _velocity = 0.0;
            StopTimer();
        }
        else if (Math.Abs(delta) < 0.001 && Math.Abs(_velocity) < 0.0005)
        {
            _position = target;
            _velocity = 0.0;
            if (!IsActive || target == 0.0)
            {
                StopTimer();
            }
        }
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        EnsureCachedResources();

        if (_cachedWellBrush != Brushes.Transparent || _cachedWellPen != null)
        {
            context.DrawRectangle(
                _cachedWellBrush,
                _cachedWellPen,
                new RoundedRect(bounds, _wellRadius));
        }

        var innerWidth = bounds.Width - (_padX * 2);
        var innerHeight = bounds.Height - (_padY * 2);
        var segWidth = Math.Max(2.0, (innerWidth - (_segGap * (SegmentCount - 1))) / SegmentCount);
        var activeSegments = (int)Math.Round(_position * SegmentCount);

        for (var i = 0; i < SegmentCount; i++)
        {
            var x = _padX + (i * (segWidth + _segGap));
            var rect = new Rect(x, _padY, segWidth, innerHeight);

            IBrush brush;
            if (i < activeSegments)
            {
                if (i >= 14) brush = _cachedPeakBrush;
                else if (i >= 11) brush = _cachedAmberBrush;
                else brush = _cachedGreenBrush;
            }
            else
            {
                brush = _cachedUnlitBrush;
            }

            context.DrawRectangle(brush, null, new RoundedRect(rect, _segRadius));
        }
    }
}
