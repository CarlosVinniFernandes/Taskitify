using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Taskitify.Core.Imaging;
using Taskitify.Core.Media;

namespace Taskitify.Linux;

internal static class Ui
{
    public static readonly IBrush White = Brushes.White;
    public static readonly IBrush SoftWhite = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));
    public static readonly IBrush DimWhite = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));

    public static Color ToColor(Rgb c) => Color.FromRgb(c.R, c.G, c.B);

    /// <summary>The Spotify-like gradient from the cover colours (dark left/top, accent right/bottom).</summary>
    public static IBrush Gradient(Cover? cover, bool diagonal = false)
    {
        var (start, end) = cover is null ? AlbumPalette.Fallback : (cover.Start, cover.End);
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, diagonal ? 0 : 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, diagonal ? 1 : 0.5, RelativeUnit.Relative),
            GradientStops = [new GradientStop(ToColor(start), 0), new GradientStop(ToColor(end), 1)],
        };
    }

    public static Button IconButton(string path, string tooltip, Action onClick, double size = 16, bool stroke = false)
    {
        var button = new Button
        {
            Content = Icons.Make(path, White, size, stroke),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => onClick();
        return button;
    }

    public static Control CoverView(Cover? cover, double size, double radius)
    {
        var holder = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(radius),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
        };
        if (cover is not null)
            holder.Child = new Image { Source = cover.Image, Stretch = Stretch.UniformToFill };
        return holder;
    }

    public static string Format(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    public static string PlayPathFor(MediaPlaybackStatus status) => status == MediaPlaybackStatus.Playing ? Icons.Pause : Icons.Play;
}
