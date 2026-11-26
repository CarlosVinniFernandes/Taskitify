using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Taskitify.Views;

/// <summary>
/// The like button's icon, drawn as vectors: a circled plus while the song is not liked, a
/// green heart once it is. <see cref="Pulse"/> plays the little pop of a fresh like.
/// </summary>
public sealed class LikeGlyph : Grid
{
    private static readonly Geometry HeartShape = Geometry.Parse(
        "M12,21.2 L10.6,19.95 C5.45,15.3 2.1,12.25 2.1,8.55 C2.1,5.5 4.5,3.1 7.55,3.1 C9.25,3.1 10.9,3.9 12,5.15 " +
        "C13.1,3.9 14.75,3.1 16.45,3.1 C19.5,3.1 21.9,5.5 21.9,8.55 C21.9,12.25 18.55,15.3 13.4,19.95 Z");
    private static readonly Geometry PlusShape = Geometry.Parse("M12,7.6 L12,16.4 M7.6,12 L16.4,12");
    private static readonly SolidColorBrush Green = Frozen(Color.FromRgb(0x1E, 0xD7, 0x60));

    public static readonly DependencyProperty IsLikedProperty = DependencyProperty.Register(
        nameof(IsLiked), typeof(bool), typeof(LikeGlyph), new PropertyMetadata(false, (d, _) => ((LikeGlyph)d).Update()));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(LikeGlyph), new PropertyMetadata(Brushes.White, (d, _) => ((LikeGlyph)d).Update()));

    private readonly Ellipse _ring = new() { Width = 19, Height = 19, StrokeThickness = 1.7 };
    private readonly Path _plus = new() { Data = PlusShape, StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly Path _heart = new() { Data = HeartShape, Fill = Green };
    private readonly ScaleTransform _scale = new(1, 1);

    public LikeGlyph()
    {
        var canvas = new Grid { Width = 24, Height = 24 };
        canvas.Children.Add(_ring);
        canvas.Children.Add(_plus);
        canvas.Children.Add(_heart);
        Children.Add(new Viewbox { Child = canvas });
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _scale;
        Width = Height = 17;
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        Update();
    }

    public bool IsLiked { get => (bool)GetValue(IsLikedProperty); set => SetValue(IsLikedProperty, value); }

    /// <summary>The circled plus's colour (the heart is always green).</summary>
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    /// <summary>The pop of a fresh like: a quick squeeze, then a springy overshoot.</summary>
    public void Pulse()
    {
        var pop = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(420) };
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(0.6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.28, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 }));
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    private void Update()
    {
        _ring.Stroke = Stroke;
        _plus.Stroke = Stroke;
        _ring.Visibility = _plus.Visibility = IsLiked ? Visibility.Collapsed : Visibility.Visible;
        _heart.Visibility = IsLiked ? Visibility.Visible : Visibility.Collapsed;
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
