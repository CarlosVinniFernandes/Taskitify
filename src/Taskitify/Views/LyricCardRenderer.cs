using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Taskitify.Views;

public enum LyricCardStyle
{
    Classic,
    Gradient,
    Blur,
    Polaroid,
    Minimal,
}

public enum LyricCardFormat
{
    /// <summary>1080 × 1920, for stories.</summary>
    Story,

    /// <summary>1080 × 1080, for posts.</summary>
    Square,
}

/// <summary>What a card shows: the song, the chosen lines, its cover and colours.</summary>
public sealed record LyricCardContent(
    string Title,
    string Artist,
    IReadOnlyList<string> Lines,
    BitmapSource? Cover,
    Color Accent,
    Color GradientStart,
    Color GradientEnd);

/// <summary>
/// Draws a lyric card in its real pixel size (1080 wide). The same element is shown scaled in the
/// card window and rendered to a PNG, so the preview is exactly the exported image.
/// </summary>
public static class LyricCardRenderer
{
    public const int Width = 1080;

    private static readonly FontFamily Display = new("Segoe UI Variable Display, Segoe UI");
    private static readonly FontFamily Text = new("Segoe UI Variable Text, Segoe UI");
    private static readonly FontFamily Hand = new("Ink Free, Segoe Print, Segoe Script");
    private static readonly FontFamily Serif = new("Georgia, Cambria, Times New Roman");

    public static int HeightOf(LyricCardFormat format) => format == LyricCardFormat.Story ? 1920 : 1080;

    public static FrameworkElement Build(LyricCardContent content, LyricCardStyle style, LyricCardFormat format)
    {
        var card = style switch
        {
            LyricCardStyle.Gradient => Gradient(content, format),
            LyricCardStyle.Blur => Blur(content, format),
            LyricCardStyle.Polaroid => Polaroid(content, format),
            LyricCardStyle.Minimal => Minimal(content, format),
            _ => Classic(content, format),
        };
        card.Width = Width;
        card.Height = HeightOf(format);
        card.ClipToBounds = true;
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(card, TextRenderingMode.Grayscale);
        RenderOptions.SetBitmapScalingMode(card, BitmapScalingMode.HighQuality);
        return card;
    }

    /// <summary>The card as a PNG-ready bitmap at its full size.</summary>
    public static BitmapSource Render(LyricCardContent content, LyricCardStyle style, LyricCardFormat format)
    {
        var card = Build(content, style, format);
        var size = new Size(Width, HeightOf(format));
        card.Measure(size);
        card.Arrange(new Rect(size));
        card.UpdateLayout();
        var bitmap = new RenderTargetBitmap(Width, HeightOf(format), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(card);
        bitmap.Freeze();
        return bitmap;
    }

    public static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Black or white, whichever reads better on <paramref name="background"/>.</summary>
    public static Color TextOn(Color background) => Luminance(background) > 0.55 ? Color.FromRgb(0x12, 0x12, 0x12) : Colors.White;

    private static double Luminance(Color c) => ((0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B)) / 255.0;

    /// <summary>Smaller type the more there is to fit.</summary>
    private static double LyricSize(LyricCardContent content, LyricCardFormat format, double large)
    {
        var chars = content.Lines.Sum(l => l.Length) + (content.Lines.Count * 12);
        var size = chars switch
        {
            <= 70 => large,
            <= 130 => large * 0.86,
            <= 200 => large * 0.74,
            <= 280 => large * 0.64,
            _ => large * 0.55,
        };
        return format == LyricCardFormat.Square ? size * 0.8 : size;
    }

    // ---- Classic: a solid colour, the song in a corner, big bold lines ----

    private static FrameworkElement Classic(LyricCardContent c, LyricCardFormat format)
    {
        var fg = TextOn(c.Accent);
        var story = format == LyricCardFormat.Story;
        var root = new Grid { Background = Brush(c.Accent) };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(96, 0, 96, 0) };
        stack.Children.Add(SongRow(c, fg, story ? 96 : 84));
        stack.Children.Add(Lines(c, Display, LyricSize(c, format, 76), FontWeights.Bold, fg, TextAlignment.Left, new Thickness(0, story ? 56 : 40, 0, 0)));
        stack.Children.Add(Wordmark(fg, new Thickness(0, story ? 72 : 48, 0, 0), HorizontalAlignment.Left));
        root.Children.Add(stack);
        return root;
    }

    // ---- Gradient: the album's own colours, everything centred ----

    private static FrameworkElement Gradient(LyricCardContent c, LyricCardFormat format)
    {
        var story = format == LyricCardFormat.Story;
        var root = new Grid
        {
            Background = new LinearGradientBrush(Darken(c.GradientStart, 0.85), Darken(c.GradientEnd, 0.6), new Point(0, 0), new Point(1, 1)),
        };
        // A soft light in the upper corner, so the gradient does not look flat.
        root.Children.Add(new Border
        {
            Background = new RadialGradientBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF), Colors.Transparent)
            {
                Center = new Point(0.15, 0.1), GradientOrigin = new Point(0.15, 0.1), RadiusX = 0.9, RadiusY = 0.6,
            },
        });
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(90, 0, 90, 0) };
        var cover = story ? 300 : 190;
        stack.Children.Add(Cover(c, cover, 20, shadow: true, HorizontalAlignment.Center));
        stack.Children.Add(Label(c.Title, Display, story ? 44 : 36, FontWeights.SemiBold, Colors.White, TextAlignment.Center, new Thickness(0, story ? 40 : 26, 0, 0)));
        stack.Children.Add(Label(c.Artist, Text, story ? 34 : 28, FontWeights.Normal, Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF), TextAlignment.Center, new Thickness(0, 6, 0, 0)));
        stack.Children.Add(Lines(c, Display, LyricSize(c, format, 70), FontWeights.Bold, Colors.White, TextAlignment.Center, new Thickness(0, story ? 70 : 40, 0, 0)));
        stack.Children.Add(Wordmark(Colors.White, new Thickness(0, story ? 80 : 44, 0, 0), HorizontalAlignment.Center));
        root.Children.Add(stack);
        return root;
    }

    // ---- Blur: the cover itself, blurred and darkened, behind the lines ----

    private static FrameworkElement Blur(LyricCardContent c, LyricCardFormat format)
    {
        var story = format == LyricCardFormat.Story;
        var root = new Grid { Background = Brush(Darken(c.GradientStart, 0.5)) };
        if (c.Cover is { } cover)
        {
            // Larger than the card: the blur fades the image's edges, which would show as a frame.
            root.Children.Add(new Image
            {
                Source = cover,
                Stretch = Stretch.UniformToFill,
                Margin = new Thickness(-160),
                Effect = new BlurEffect { Radius = 70, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality },
            });
        }
        root.Children.Add(new Border
        {
            Background = new LinearGradientBrush(Color.FromArgb(0x66, 0, 0, 0), Color.FromArgb(0xB3, 0, 0, 0), 90),
        });
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(96, 0, 96, 0) };
        stack.Children.Add(Label("“", Serif, story ? 220 : 160, FontWeights.Bold, Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF), TextAlignment.Left,
            new Thickness(-8, 0, 0, story ? -110 : -80)));
        stack.Children.Add(Lines(c, Display, LyricSize(c, format, 72), FontWeights.Bold, Colors.White, TextAlignment.Left, new Thickness(0)));
        stack.Children.Add(SongRow(c, Colors.White, story ? 96 : 80, new Thickness(0, story ? 72 : 44, 0, 0)));
        stack.Children.Add(Wordmark(Colors.White, new Thickness(0, story ? 56 : 36, 0, 0), HorizontalAlignment.Left));
        root.Children.Add(stack);
        return root;
    }

    // ---- Polaroid: an instant photo of the cover, the lines handwritten under it ----

    private static FrameworkElement Polaroid(LyricCardContent c, LyricCardFormat format)
    {
        var story = format == LyricCardFormat.Story;
        var root = new Grid { Background = Brush(c.Accent) };
        var photo = story ? 700 : 470;
        var paper = Color.FromRgb(0xFA, 0xF8, 0xF2);
        var ink = Color.FromRgb(0x2A, 0x2A, 0x2E);
        var inner = new StackPanel();
        inner.Children.Add(Cover(c, photo, 2, shadow: false, HorizontalAlignment.Center));
        inner.Children.Add(Lines(c, Hand, LyricSize(c, format, 60), FontWeights.Normal, ink, TextAlignment.Center, new Thickness(10, story ? 48 : 26, 10, 0)));
        inner.Children.Add(Label($"— {c.Title}, {c.Artist}", Hand, story ? 36 : 28, FontWeights.Normal, Color.FromArgb(0xB3, ink.R, ink.G, ink.B),
            TextAlignment.Center, new Thickness(10, story ? 30 : 16, 10, 0)));
        var frame = new Border
        {
            Background = Brush(paper),
            Padding = new Thickness(story ? 44 : 30, story ? 44 : 30, story ? 44 : 30, story ? 60 : 34),
            Width = photo + (story ? 88 : 60),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(-2.5),
            Effect = new DropShadowEffect { BlurRadius = 50, ShadowDepth = 18, Direction = 290, Opacity = 0.35, Color = Colors.Black },
            Child = inner,
        };
        var layout = new DockPanel { LastChildFill = true };
        var mark = Wordmark(TextOn(c.Accent), new Thickness(0, 0, 0, story ? 90 : 40), HorizontalAlignment.Center);
        DockPanel.SetDock(mark, Dock.Bottom);
        layout.Children.Add(mark);
        layout.Children.Add(frame);
        root.Children.Add(layout);
        return root;
    }

    // ---- Minimal: paper, a serif quote, the colour only as an accent ----

    private static FrameworkElement Minimal(LyricCardContent c, LyricCardFormat format)
    {
        var story = format == LyricCardFormat.Story;
        var paper = Color.FromRgb(0xF4, 0xF0, 0xE8);
        var ink = Color.FromRgb(0x1C, 0x1C, 0x1E);
        // The accent must show on paper: a too light colour is darkened.
        var accent = Luminance(c.Accent) > 0.7 ? Darken(c.Accent, 0.55) : c.Accent;
        var root = new Grid { Background = Brush(paper) };
        root.Children.Add(new Border { Background = Brush(accent), Height = story ? 24 : 16, VerticalAlignment = VerticalAlignment.Top });
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(110, 0, 110, 0) };
        stack.Children.Add(Label("“", Serif, story ? 260 : 180, FontWeights.Bold, accent, TextAlignment.Left, new Thickness(-10, 0, 0, story ? -140 : -100)));
        var lines = Lines(c, Serif, LyricSize(c, format, 66), FontWeights.Normal, ink, TextAlignment.Left, new Thickness(0));
        foreach (var block in lines.Children.OfType<TextBlock>()) block.FontStyle = FontStyles.Italic;
        stack.Children.Add(lines);
        stack.Children.Add(new Border { Background = Brush(accent), Height = 4, Width = 120, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, story ? 70 : 44, 0, story ? 34 : 24) });
        var title = Label(c.Title.ToUpperInvariant(), Text, story ? 34 : 28, FontWeights.SemiBold, ink, TextAlignment.Left, new Thickness(0));
        stack.Children.Add(title);
        stack.Children.Add(Label(c.Artist, Serif, story ? 34 : 28, FontWeights.Normal, Color.FromArgb(0xB3, ink.R, ink.G, ink.B), TextAlignment.Left, new Thickness(0, 6, 0, 0)));
        root.Children.Add(stack);
        var mark = Wordmark(Color.FromArgb(0x99, ink.R, ink.G, ink.B), new Thickness(110, 0, 0, story ? 90 : 56), HorizontalAlignment.Left);
        mark.VerticalAlignment = VerticalAlignment.Bottom;
        root.Children.Add(mark);
        return root;
    }

    // ---- Parts ----

    private static StackPanel Lines(LyricCardContent c, FontFamily font, double size, FontWeight weight, Color color, TextAlignment align, Thickness margin)
    {
        var panel = new StackPanel { Margin = margin };
        foreach (var line in c.Lines)
        {
            panel.Children.Add(new TextBlock
            {
                Text = line,
                FontFamily = font,
                FontSize = size,
                FontWeight = weight,
                Foreground = Brush(color),
                TextAlignment = align,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = size * 1.18,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            });
        }
        return panel;
    }

    private static TextBlock Label(string text, FontFamily font, double size, FontWeight weight, Color color, TextAlignment align, Thickness margin) => new()
    {
        Text = text,
        FontFamily = font,
        FontSize = size,
        FontWeight = weight,
        Foreground = Brush(color),
        TextAlignment = align,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = margin,
    };

    /// <summary>Cover, title and artist side by side.</summary>
    private static FrameworkElement SongRow(LyricCardContent c, Color fg, double cover, Thickness margin = default)
    {
        var row = new Grid { Margin = margin };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var art = Cover(c, cover, 8, shadow: false, HorizontalAlignment.Left);
        row.Children.Add(art);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(26, 0, 0, 0) };
        text.Children.Add(Label(c.Title, Display, cover * 0.4, FontWeights.Bold, fg, TextAlignment.Left, new Thickness(0)));
        text.Children.Add(Label(c.Artist, Text, cover * 0.32, FontWeights.Normal, Color.FromArgb(0xCC, fg.R, fg.G, fg.B), TextAlignment.Left, new Thickness(0, 2, 0, 0)));
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private static FrameworkElement Cover(LyricCardContent c, double size, double radius, bool shadow, HorizontalAlignment align)
    {
        var border = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(radius),
            HorizontalAlignment = align,
            Background = c.Cover is { } cover
                ? new ImageBrush(cover) { Stretch = Stretch.UniformToFill }
                : new LinearGradientBrush(c.GradientStart, c.GradientEnd, 45),
        };
        if (c.Cover is null)
        {
            border.Child = new TextBlock
            {
                Text = "♪",
                FontSize = size * 0.45,
                Foreground = Brush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        if (shadow)
            border.Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 12, Direction = 270, Opacity = 0.4, Color = Colors.Black };
        return border;
    }

    /// <summary>The app's name, small, where a card says where it came from.</summary>
    private static FrameworkElement Wordmark(Color color, Thickness margin, HorizontalAlignment align)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = align, Margin = margin };
        row.Children.Add(new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            Background = Brush(color),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "♫",
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                Foreground = Brush(Luminance(color) > 0.5 ? Color.FromRgb(0x12, 0x12, 0x12) : Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0),
            },
        });
        row.Children.Add(new TextBlock
        {
            Text = "Taskitify",
            FontFamily = Display,
            FontSize = 36,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(color),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 2),
        });
        return row;
    }

    private static Color Darken(Color c, double factor) =>
        Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

    private static SolidColorBrush Brush(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }
}
