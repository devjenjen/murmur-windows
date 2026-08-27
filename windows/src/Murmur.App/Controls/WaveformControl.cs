using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Murmur.App.Controls;

/// <summary>
/// 12-bar audio visualizer rendered at 60fps with phase offsets and level responsiveness.
/// Stops animation while idle or detached.
/// </summary>
public sealed class WaveformControl : Control
{
    /// <summary>Styled property for current audio level.</summary>
    public static readonly StyledProperty<float> LevelProperty =
        AvaloniaProperty.Register<WaveformControl, float>(nameof(Level));

    /// <summary>Styled property for active recording state.</summary>
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<WaveformControl, bool>(nameof(IsActive));

    static WaveformControl()
    {
        LevelProperty.Changed.AddClassHandler<WaveformControl>((c, _) => c.OnLevelOrActiveChanged());
        IsActiveProperty.Changed.AddClassHandler<WaveformControl>((c, _) => c.OnLevelOrActiveChanged());
        IsVisibleProperty.Changed.AddClassHandler<WaveformControl>((c, _) => c.OnVisibilityChanged());
    }

    /// <summary>Current normalized audio level (0..1).</summary>
    public float Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    /// <summary>Whether waveform animation is active.</summary>
    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private const int BarCount = 12;
    private static readonly double[] Phases = Enumerable.Range(0, BarCount)
        .Select(i => (i * 0.618) % 1.0)
        .ToArray();

    private DispatcherTimer? _timer;
    private readonly DateTime _startTime = DateTime.UtcNow;
    private bool _isAttached;
    private static readonly IBrush BarBrush = new SolidColorBrush(Color.Parse("#E4DED0"));

    /// <summary>Internal flag indicating if animation timer is running (for tests only).</summary>
    internal bool IsAnimationRunning => _timer?.IsEnabled == true;

    /// <summary>Initializes a new instance of WaveformControl.</summary>
    public WaveformControl()
    {
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
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
        EvaluateTimerState();
        if (!_isAttached || !IsVisible || !IsActive || Level <= 0.001f)
        {
            InvalidateVisual();
        }
    }

    private void EvaluateTimerState()
    {
        if (!_isAttached || !IsVisible)
        {
            StopTimer();
            return;
        }

        var shouldAnimate = IsActive && Level > 0.001f;
        if (shouldAnimate)
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
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / 60.0), DispatcherPriority.Render, (_, _) => InvalidateVisual());
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

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var barWidth = 3.5;
        var barGap = 3.0;
        var totalWidth = (BarCount * barWidth) + ((BarCount - 1) * barGap);
        var startX = (bounds.Width - totalWidth) / 2.0;
        var centerY = bounds.Height / 2.0;

        var elapsed = (DateTime.UtcNow - _startTime).TotalSeconds;
        var hasActiveSignal = IsActive && Level > 0.001f;

        for (var i = 0; i < BarCount; i++)
        {
            var x = startX + (i * (barWidth + barGap));
            double height;
            if (hasActiveSignal)
            {
                var phase = Phases[i];
                var wave = Math.Sin((elapsed * 6.0) + (phase * Math.PI * 2.0));
                var amp = Math.Max(0.06, (double)Level);
                height = 4.0 + (amp * (0.55 + (0.45 * wave)) * (bounds.Height - 6.0));
            }
            else
            {
                height = 4.0;
            }

            var rect = new Rect(x, centerY - (height / 2.0), barWidth, height);
            context.DrawRectangle(BarBrush, null, new RoundedRect(rect, barWidth / 2.0));
        }
    }
}
