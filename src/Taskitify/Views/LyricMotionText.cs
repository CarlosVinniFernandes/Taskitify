using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Taskitify.Views;

/// <summary>Interpolates the existing lyric visual without changing its measure or wrapping.</summary>
public sealed class LyricMotionText : TextBlock
{
    public static readonly DependencyProperty TargetOpacityProperty = DependencyProperty.Register(
        nameof(TargetOpacity), typeof(double), typeof(LyricMotionText), new PropertyMetadata(1.0, OnTargetChanged));
    public static readonly DependencyProperty TargetScaleProperty = DependencyProperty.Register(
        nameof(TargetScale), typeof(double), typeof(LyricMotionText), new PropertyMetadata(1.0, OnTargetChanged));
    public static readonly DependencyProperty TransitionMillisecondsProperty = DependencyProperty.Register(
        nameof(TransitionMilliseconds), typeof(double), typeof(LyricMotionText), new PropertyMetadata(0.0, OnDurationChanged));
    public static readonly DependencyProperty ExpressiveProperty = DependencyProperty.Register(
        nameof(Expressive), typeof(bool), typeof(LyricMotionText), new PropertyMetadata(false));

    // An instrumental break drawn as dots (see InterludeDots): the line's text is a blank of the
    // same height, the dots its background, redrawn each frame only while the break is current.
    public static readonly DependencyProperty InterludeProperty = DependencyProperty.Register(
        nameof(Interlude), typeof(bool), typeof(LyricMotionText), new PropertyMetadata(false, OnInterludeChanged));
    public static readonly DependencyProperty InterludeActiveProperty = DependencyProperty.Register(
        nameof(InterludeActive), typeof(bool), typeof(LyricMotionText), new PropertyMetadata(false, OnInterludeChanged));
    public static readonly DependencyProperty InterludeStartProperty = DependencyProperty.Register(
        nameof(InterludeStart), typeof(TimeSpan), typeof(LyricMotionText), new PropertyMetadata(TimeSpan.Zero));
    public static readonly DependencyProperty InterludeEndProperty = DependencyProperty.Register(
        nameof(InterludeEnd), typeof(TimeSpan), typeof(LyricMotionText), new PropertyMetadata(TimeSpan.Zero));
    public static readonly DependencyProperty PositionClockProperty = DependencyProperty.Register(
        nameof(PositionClock), typeof(Func<TimeSpan>), typeof(LyricMotionText), new PropertyMetadata(null));
    public static readonly DependencyProperty PlayingProperty = DependencyProperty.Register(
        nameof(Playing), typeof(bool), typeof(LyricMotionText), new PropertyMetadata(false, OnInterludeChanged));

    private readonly ScaleTransform _zoom = new(1, 1);
    private DrawingGroup? _dots;
    private bool _ticking;

    public LyricMotionText()
    {
        RenderTransform = _zoom;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
                ApplyImmediately();
            UpdateInterlude();
        };
        Unloaded += (_, _) => StopTicking();
    }

    public bool Interlude { get => (bool)GetValue(InterludeProperty); set => SetValue(InterludeProperty, value); }
    public bool InterludeActive { get => (bool)GetValue(InterludeActiveProperty); set => SetValue(InterludeActiveProperty, value); }
    public TimeSpan InterludeStart { get => (TimeSpan)GetValue(InterludeStartProperty); set => SetValue(InterludeStartProperty, value); }
    /// <summary>When the voice returns (zero: unknown).</summary>
    public TimeSpan InterludeEnd { get => (TimeSpan)GetValue(InterludeEndProperty); set => SetValue(InterludeEndProperty, value); }
    public Func<TimeSpan>? PositionClock { get => (Func<TimeSpan>?)GetValue(PositionClockProperty); set => SetValue(PositionClockProperty, value); }
    public bool Playing { get => (bool)GetValue(PlayingProperty); set => SetValue(PlayingProperty, value); }

    private static void OnInterludeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) => ((LyricMotionText)sender).UpdateInterlude();

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // The dots' box follows the type and the alignment.
        if (_dots is not null && (e.Property == TextAlignmentProperty || e.Property == FontSizeProperty))
        {
            ClearValue(BackgroundProperty);
            _dots = null;
            UpdateInterlude();
        }
    }

    private void UpdateInterlude()
    {
        if (!Interlude)
        {
            StopTicking();
            if (_dots is not null)
            {
                ClearValue(BackgroundProperty);
                _dots = null;
            }
            return;
        }
        if (_dots is null)
        {
            _dots = new DrawingGroup();
            var box = InterludeDots.BoxFor(FontSize);
            Background = new DrawingBrush(_dots)
            {
                Stretch = Stretch.None,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(box),
                AlignmentX = TextAlignment switch { TextAlignment.Left => AlignmentX.Left, TextAlignment.Right => AlignmentX.Right, _ => AlignmentX.Center },
                AlignmentY = AlignmentY.Center,
            };
        }
        if (InterludeActive && Playing && IsVisible && SystemParameters.ClientAreaAnimation)
            StartTicking();
        else
        {
            StopTicking();
            DrawDots();
        }
    }

    private void StartTicking()
    {
        if (_ticking)
            return;
        _ticking = true;
        CompositionTarget.Rendering += OnFrame;
    }

    private void StopTicking()
    {
        if (!_ticking)
            return;
        _ticking = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e) => DrawDots();

    private void DrawDots()
    {
        if (_dots is null)
            return;
        var color = Foreground is SolidColorBrush brush ? brush.Color : Colors.White;
        double? progress = null, remaining = null;
        double elapsed = 0;
        if (InterludeActive && PositionClock is { } clock)
        {
            var now = clock();
            elapsed = Math.Max(0, (now - InterludeStart).TotalSeconds);
            if (InterludeEnd > InterludeStart)
            {
                var length = (InterludeEnd - InterludeStart).TotalSeconds;
                progress = Math.Clamp(elapsed / length, 0, 1);
                remaining = Math.Max(0, (InterludeEnd - now).TotalSeconds);
            }
            else
                progress = Math.Clamp(elapsed / 8, 0, 1); // the end is unknown: fill over a few seconds
        }
        using var dc = _dots.Open();
        // The full box, transparent: the brush keeps its alignment while the dots grow and shrink.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(InterludeDots.BoxFor(FontSize)));
        InterludeDots.Draw(dc, FontSize, color, progress, elapsed, remaining, Playing);
    }

    public double TargetOpacity { get => (double)GetValue(TargetOpacityProperty); set => SetValue(TargetOpacityProperty, value); }
    public double TargetScale { get => (double)GetValue(TargetScaleProperty); set => SetValue(TargetScaleProperty, value); }
    public double TransitionMilliseconds { get => (double)GetValue(TransitionMillisecondsProperty); set => SetValue(TransitionMillisecondsProperty, value); }
    public bool Expressive { get => (bool)GetValue(ExpressiveProperty); set => SetValue(ExpressiveProperty, value); }

    private void ApplyImmediately()
    {
        Tween(this, OpacityProperty, TargetOpacity, 0, Expressive);
        Tween(_zoom, ScaleTransform.ScaleXProperty, TargetScale, 0, Expressive);
        Tween(_zoom, ScaleTransform.ScaleYProperty, TargetScale, 0, Expressive);
    }

    private static void OnDurationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if ((double)e.NewValue == 0)
            ((LyricMotionText)sender).ApplyImmediately();
    }

    private static void OnTargetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var text = (LyricMotionText)sender;
        var duration = text.IsVisible && SystemParameters.ClientAreaAnimation ? text.TransitionMilliseconds : 0;
        var target = (double)e.NewValue;
        if (e.Property == TargetOpacityProperty)
            Tween(text, OpacityProperty, target, duration * 0.72, text.Expressive);
        else
        {
            Tween(text._zoom, ScaleTransform.ScaleXProperty, target, duration * 0.86, text.Expressive);
            Tween(text._zoom, ScaleTransform.ScaleYProperty, target, duration * 0.86, text.Expressive);
        }
    }

    private static readonly IEasingFunction Cubic = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
    private static readonly IEasingFunction Quartic = Freeze(new QuarticEase { EasingMode = EasingMode.EaseOut });

    private static T Freeze<T>(T easing) where T : EasingFunctionBase
    {
        easing.Freeze();
        return easing;
    }

    internal static void Tween(IAnimatable visual, DependencyProperty property, double target, double milliseconds, bool expressive)
    {
        var element = (DependencyObject)visual;
        var current = (double)element.GetValue(property);
        if (milliseconds <= 0 || Math.Abs(current - target) < 0.001)
        {
            visual.BeginAnimation(property, null);
            element.SetCurrentValue(property, target);
            return;
        }
        // The animation holds its end value. With FillBehavior.Stop the property fell back to its
        // base value when the animation ended: SetCurrentValue does not survive an animation, so
        // the mini player's list jumped back to the first line and lines lost their emphasis.
        visual.BeginAnimation(property, new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = expressive ? Quartic : Cubic,
            FillBehavior = FillBehavior.HoldEnd,
        }, HandoffBehavior.SnapshotAndReplace);
    }
}
