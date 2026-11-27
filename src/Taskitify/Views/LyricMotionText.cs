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

    private readonly ScaleTransform _zoom = new(1, 1);

    public LyricMotionText()
    {
        RenderTransform = _zoom;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
                ApplyImmediately();
        };
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
