using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Taskitify.Models;

namespace Taskitify.Views;

/// <summary>One line of the lyrics and when it is sung, if known.</summary>
public sealed record LyricCardLine(string Text, TimeSpan? Time);

/// <summary>What a card shows: the song, the chosen lines, its cover and colours.</summary>
public sealed record LyricCardContent(
    string Title,
    string Artist,
    string? Album,
    IReadOnlyList<LyricCardLine> Lines,
    BitmapSource? Cover,
    Color Accent,
    Color GradientStart,
    Color GradientEnd,
    TimeSpan? Duration = null)
{
    public BitmapSource? PersonalImage { get; init; }
}

/// <summary>
/// Draws a lyric card at its real pixel size (1080 or 1920 wide, whatever the window). The same
/// element is shown scaled in the card window and rendered to the PNG, so the preview is the
/// exported image. Lines that would not fit are set smaller until they do, never cut.
/// </summary>
public static partial class LyricCardRenderer
{
    private static readonly Uri FontFolder = new("pack://application:,,,/Taskitify;component/Resources/Fonts/");
    internal static readonly FontFamily Figtree = new(FontFolder, "./#Figtree");
    internal static readonly FontFamily DmSerif = new(FontFolder, "./#DM Serif Display");
    /// <summary>Inter Display: tight, heavy display type (close to the system fonts of music apps), for lyrics on screen.</summary>
    internal static readonly FontFamily InterDisplay = new(FontFolder, "./#Inter Display");
    private static readonly FontFamily Hand = new("Ink Free, Segoe Print");
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    private static readonly FontFamily Condensed = new("Bahnschrift");
    private static readonly FontFamily Classic95 = new("Tahoma, Segoe UI");
    private static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    // Keep the artwork at its designed size unless the lyrics would have to get too small.
    private static readonly (double Art, double MinimumText)[] FitPasses =
    [
        (1, 0.78), (0.78, 0.68), (0.58, 0.58), (0.34, 0.2),
    ];

    public static (int Width, int Height) SizeOf(LyricCardFormat format) => format switch
    {
        LyricCardFormat.Square => (1080, 1080),
        LyricCardFormat.Portrait => (1080, 1350),
        LyricCardFormat.Landscape => (1920, 1080),
        _ => (1080, 1920),
    };

    /// <summary>The font a style uses for its lines when the user keeps "Auto".</summary>
    public static LyricCardFont DefaultFont(LyricCardStyle style) => LyricStudioCatalog.Get(style).Font;

    public static FrameworkElement Build(LyricCardContent content, LyricCardOptions options)
    {
        var (width, height) = SizeOf(options.Format);
        FrameworkElement? card = null;
        foreach (var (art, minimumText) in FitPasses)
        {
            var ctx = new Ctx(content, options, width, height, art);
            card = Compose(ctx);
            card.Width = width;
            card.Height = height;
            card.ClipToBounds = true;
            TextOptions.SetTextFormattingMode(card, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(card, TextRenderingMode.Grayscale);
            if (ctx.FitLyrics(minimumText))
                break;
        }
        RenderOptions.SetBitmapScalingMode(card!, BitmapScalingMode.HighQuality);
        return card!;
    }

    /// <summary>The card as a bitmap at its full size, or scaled (thumbnails) by <paramref name="scale"/>.</summary>
    public static BitmapSource Render(LyricCardContent content, LyricCardOptions options, double scale = 1)
    {
        var card = Build(content, options);
        var (width, height) = SizeOf(options.Format);
        var size = new Size(width, height);
        card.Measure(size);
        card.Arrange(new Rect(size));
        card.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Round(width * scale), (int)Math.Round(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
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

    internal static double Luminance(Color c) => ((0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B)) / 255.0;

    /// <summary>A livelier version of an album colour (album colours are often muddy): more saturation, mid lightness.</summary>
    public static Color Vivid(Color c, double lightness = 0.42)
    {
        var (h, s, _) = ToHsl(c);
        return FromHsl(h, Math.Max(0.55, Math.Min(1, s * 1.6)), lightness);
    }

    private static FrameworkElement Compose(Ctx ctx) => ctx.O.Style switch
    {
        LyricCardStyle.Gradient => Gradient(ctx),
        LyricCardStyle.Blur => Blur(ctx),
        LyricCardStyle.Polaroid => Polaroid(ctx),
        LyricCardStyle.Minimal => Minimal(ctx),
        LyricCardStyle.Editorial => Editorial(ctx),
        LyricCardStyle.Player => Player(ctx),
        LyricCardStyle.Vinyl => Vinyl(ctx),
        LyricCardStyle.Karaoke => Karaoke(ctx),
        LyricCardStyle.Retro => Retro(ctx),
        LyricCardStyle.Classic => Classic(ctx),
        _ => ComposeStudio(ctx),
    };

    // ======================= Styles =======================

    /// <summary>A solid colour, the song in a corner, big bold lines (the familiar lyric card).</summary>
    private static FrameworkElement Classic(Ctx ctx)
    {
        var u = ctx.U;
        var bg = ctx.C.Accent;
        var fg = TextOn(bg);
        var root = new Grid { Background = Brush(bg) };
        var padX = ctx.Landscape ? 170 * u : 96 * u;
        var colW = ctx.Landscape ? Math.Min(1300 * u, ctx.W - (2 * padX)) : ctx.W - (2 * padX);
        var align = ctx.Align(TextAlignment.Left);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(padX, 0, 0, 0), Width = colW };
        stack.Children.Add(SongRow(ctx, fg, 100 * u, align));
        var lines = ctx.Lines(LyricCardFont.Modern, 80, fg, align, colW);
        lines.Margin = new Thickness(0, 54 * u, 0, 0);
        stack.Children.Add(lines);
        stack.Children.Add(Footer(ctx, fg, bg, align, new Thickness(0, 64 * u, 0, 0)));
        ctx.Fit(stack, ctx.H - (2 * 90 * u));
        root.Children.Add(stack);
        return root;
    }

    /// <summary>The album's own colours in a soft gradient, the cover big, everything centred.</summary>
    private static FrameworkElement Gradient(Ctx ctx)
    {
        var u = ctx.U;
        var start = Darken(ctx.C.Accent, 0.9);
        var end = Darken(ctx.C.GradientEnd, 0.45);
        var root = new Grid { Background = new LinearGradientBrush(start, end, new Point(0, 0), new Point(0.6, 1)) };
        root.Children.Add(new Border
        {
            Background = new RadialGradientBrush(Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF), Colors.Transparent)
            { Center = new Point(0.2, 0.08), GradientOrigin = new Point(0.2, 0.08), RadiusX = 0.9, RadiusY = 0.6 },
        });
        var fg = Colors.White;
        var align = ctx.Align(TextAlignment.Center);
        var showCover = ctx.O.ShowCover;
        if (ctx.Landscape)
        {
            var grid = new Grid { Margin = new Thickness(140 * u, 0, 140 * u, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var coverSize = showCover ? 640 * u * ctx.Art : 0;
            if (showCover)
                grid.Children.Add(Cover(ctx, coverSize, 28 * u, shadow: true, HorizontalAlignment.Left));
            var colW = ctx.W - (280 * u) - coverSize - (showCover ? 110 * u : 0);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(showCover ? 110 * u : 0, 0, 0, 0), Width = colW };
            Grid.SetColumn(text, 1);
            var textAlign = ctx.Align(TextAlignment.Left);
            text.Children.Add(Label(ctx.C.Title, Figtree, 46 * u, FontWeights.Bold, fg, textAlign));
            text.Children.Add(Label(ctx.C.Artist, Figtree, 36 * u, FontWeights.Normal, Alpha(fg, 0xCC), textAlign, new Thickness(0, 6 * u, 0, 0)));
            var lines = ctx.Lines(LyricCardFont.Modern, 78, fg, textAlign, colW);
            lines.Margin = new Thickness(0, 50 * u, 0, 0);
            text.Children.Add(lines);
            text.Children.Add(Footer(ctx, fg, start, textAlign, new Thickness(0, 56 * u, 0, 0)));
            ctx.Fit(text, ctx.H - (2 * 90 * u));
            grid.Children.Add(text);
            root.Children.Add(grid);
            return root;
        }
        var cover = !showCover ? 0 : ctx.Art * ctx.O.Format switch { LyricCardFormat.Story => 420 * u, LyricCardFormat.Portrait => 300 * u, _ => 230 * u };
        var width = ctx.W - (180 * u);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Width = width };
        if (showCover)
            stack.Children.Add(Cover(ctx, cover, 26 * u, shadow: true, HorizontalAlignment.Center));
        stack.Children.Add(Label(ctx.C.Title, Figtree, 46 * u, FontWeights.Bold, fg, TextAlignment.Center, new Thickness(0, showCover ? 44 * u : 0, 0, 0)));
        stack.Children.Add(Label(ctx.C.Artist, Figtree, 34 * u, FontWeights.Normal, Alpha(fg, 0xCC), TextAlignment.Center, new Thickness(0, 6 * u, 0, 0)));
        var body = ctx.Lines(LyricCardFont.Modern, 74, fg, align, width);
        body.Margin = new Thickness(0, 64 * u, 0, 0);
        stack.Children.Add(body);
        stack.Children.Add(Footer(ctx, fg, start, TextAlignment.Center, new Thickness(0, 70 * u, 0, 0)));
        ctx.Fit(stack, ctx.H - (2 * 80 * u));
        root.Children.Add(stack);
        return root;
    }

    /// <summary>The cover itself, blurred and darkened, behind a big quote.</summary>
    private static FrameworkElement Blur(Ctx ctx)
    {
        var u = ctx.U;
        var root = new Grid { Background = new LinearGradientBrush(Darken(ctx.C.GradientStart, 0.6), Darken(ctx.C.GradientEnd, 0.35), 90) };
        if (ctx.C.Cover is { } cover)
        {
            // Larger than the card: the blur fades the image's edges, which would show as a frame.
            root.Children.Add(new Image
            {
                Source = cover,
                Stretch = Stretch.UniformToFill,
                Margin = new Thickness(-200 * u),
                Effect = new BlurEffect { Radius = 80 * u, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality },
            });
        }
        root.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(0x55, 0, 0, 0), Color.FromArgb(0xC0, 0, 0, 0), 90) });
        var fg = Colors.White;
        var align = ctx.Align(TextAlignment.Left);
        var padX = ctx.Landscape ? 170 * u : 96 * u;
        var colW = ctx.Landscape ? Math.Min(1350 * u, ctx.W - (2 * padX)) : ctx.W - (2 * padX);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Width = colW, Margin = new Thickness(padX, 0, 0, 0) };
        stack.Children.Add(Label("“", DmSerif, 240 * u, FontWeights.Normal, Alpha(ctx.Highlight, 0xE6), align, new Thickness(-6 * u, 0, 0, -150 * u)));
        var lines = ctx.Lines(LyricCardFont.Modern, 76, fg, align, colW);
        stack.Children.Add(lines);
        stack.Children.Add(SongRow(ctx, fg, 96 * u, align, new Thickness(0, 72 * u, 0, 0)));
        stack.Children.Add(Footer(ctx, fg, Colors.Black, align, new Thickness(0, 52 * u, 0, 0)));
        ctx.Fit(stack, ctx.H - (2 * 80 * u));
        root.Children.Add(stack);
        return root;
    }

    /// <summary>An instant photo of the cover, the lines handwritten on its border.</summary>
    private static FrameworkElement Polaroid(Ctx ctx)
    {
        var u = ctx.U;
        var bg = ctx.C.Accent;
        var root = new Grid { Background = Brush(bg) };
        // A paper texture of light dots, so the colour does not look flat.
        root.Children.Add(new Border { Background = new RadialGradientBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), Colors.Transparent) { RadiusX = 0.8, RadiusY = 0.6 } });
        var paper = Color.FromRgb(0xFB, 0xF9, 0xF3);
        var ink = Color.FromRgb(0x26, 0x26, 0x2C);
        var align = ctx.Align(TextAlignment.Center);
        var showPhoto = ctx.O.ShowCover;
        FrameworkElement frameContent;
        double frameWidth;
        if (ctx.Landscape)
        {
            var photo = showPhoto ? 700 * u * ctx.Art : 0;
            var textW = 760 * u;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(textW) });
            if (showPhoto) grid.Children.Add(Cover(ctx, photo, 2 * u, shadow: false, HorizontalAlignment.Left));
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(showPhoto ? 56 * u : 0, 0, 0, 0) };
            Grid.SetColumn(text, 1);
            var lines = ctx.Lines(LyricCardFont.Handwritten, 62, ink, align, textW - (showPhoto ? 56 * u : 0));
            text.Children.Add(lines);
            text.Children.Add(Label($"— {ctx.C.Title}, {ctx.C.Artist}", Hand, 36 * u, FontWeights.Normal, Alpha(ink, 0xB3), align, new Thickness(0, 28 * u, 0, 0)));
            grid.Children.Add(text);
            frameContent = grid;
            frameWidth = photo + textW + (2 * 46 * u);
        }
        else
        {
            var photo = !showPhoto ? 0 : ctx.Art * ctx.O.Format switch { LyricCardFormat.Story => 740 * u, LyricCardFormat.Portrait => 500 * u, _ => 360 * u };
            var width = Math.Max(photo, 760 * u);
            var inner = new StackPanel();
            if (showPhoto) inner.Children.Add(Cover(ctx, photo, 2 * u, shadow: false, HorizontalAlignment.Center));
            var lines = ctx.Lines(LyricCardFont.Handwritten, 60, ink, align, width - (20 * u));
            lines.Margin = new Thickness(10 * u, showPhoto ? 46 * u : 10 * u, 10 * u, 0);
            inner.Children.Add(lines);
            inner.Children.Add(Label($"— {ctx.C.Title}, {ctx.C.Artist}", Hand, 36 * u, FontWeights.Normal, Alpha(ink, 0xB3), align, new Thickness(10 * u, 26 * u, 10 * u, 0)));
            frameContent = inner;
            frameWidth = width + (2 * 46 * u);
        }
        var frame = new Border
        {
            Background = Brush(paper),
            Padding = new Thickness(46 * u, 46 * u, 46 * u, 70 * u),
            Width = frameWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(-2.5),
            Effect = new DropShadowEffect { BlurRadius = 60 * u, ShadowDepth = 20 * u, Direction = 290, Opacity = 0.38, Color = Colors.Black },
            Child = frameContent,
            Margin = new Thickness(0, 0, 0, 60 * u),
        };
        ctx.Fit(frame, ctx.H - (ctx.Landscape ? 230 : 300) * u);
        root.Children.Add(frame);
        // A strip of tape holding the photo.
        root.Children.Add(new Border
        {
            Width = 220 * u, Height = 64 * u, Background = Brush(Color.FromArgb(0x99, 0xF4, 0xEC, 0xD0)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(-6),
            Margin = new Thickness(0, 0, 0, 1), IsHitTestVisible = false,
        });
        // Placed on the frame's top edge once both are measured.
        PlaceTape(root, frame, u);
        var mark = Footer(ctx, TextOn(bg), bg, TextAlignment.Center, new Thickness(0, 0, 0, 70 * u));
        mark.VerticalAlignment = VerticalAlignment.Bottom;
        root.Children.Add(mark);
        return root;
    }

    private static void PlaceTape(Grid root, Border frame, double u)
    {
        // The tape sits over the frame's top edge: frame height is only known after measuring it.
        var tape = (Border)root.Children[^1];
        frame.Measure(new Size(frame.Width, double.PositiveInfinity));
        var top = (frame.DesiredSize.Height / 2) + (30 * u);
        tape.Margin = new Thickness(0, 0, 0, top * 2);
    }

    /// <summary>Paper, a serif quote, the colour only as an accent.</summary>
    private static FrameworkElement Minimal(Ctx ctx)
    {
        var u = ctx.U;
        var paper = Color.FromRgb(0xF5, 0xF1, 0xE9);
        var ink = Color.FromRgb(0x1B, 0x1B, 0x1D);
        var accent = Luminance(ctx.C.Accent) > 0.7 ? Darken(ctx.C.Accent, 0.55) : ctx.C.Accent;
        var root = new Grid { Background = Brush(paper) };
        root.Children.Add(new Border { Background = Brush(accent), Height = 22 * u, VerticalAlignment = VerticalAlignment.Top });
        var align = ctx.Align(TextAlignment.Left);
        var padX = ctx.Landscape ? 200 * u : 110 * u;
        var colW = ctx.Landscape ? Math.Min(1250 * u, ctx.W - (2 * padX)) : ctx.W - (2 * padX);
        if (ctx.O.ShowCover && ctx.C.Cover is not null)
        {
            var cover = Cover(ctx, 150 * u, 4 * u, shadow: false, HorizontalAlignment.Right);
            cover.VerticalAlignment = VerticalAlignment.Top;
            cover.Margin = new Thickness(0, 110 * u, padX, 0);
            root.Children.Add(cover);
        }
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Width = colW, Margin = new Thickness(padX, 0, 0, 0) };
        stack.Children.Add(Label("“", DmSerif, 250 * u, FontWeights.Normal, accent, align, new Thickness(-8 * u, 0, 0, -160 * u)));
        var lines = ctx.Lines(LyricCardFont.Serif, 74, ink, align, colW);
        stack.Children.Add(lines);
        var rule = new Border { Background = Brush(accent), Height = 4 * u, Width = 120 * u, Margin = new Thickness(0, 70 * u, 0, 34 * u) };
        rule.HorizontalAlignment = align switch { TextAlignment.Center => HorizontalAlignment.Center, TextAlignment.Right => HorizontalAlignment.Right, _ => HorizontalAlignment.Left };
        stack.Children.Add(rule);
        stack.Children.Add(Spaced(ctx.C.Title.ToUpperInvariant(), Figtree, 32 * u, FontWeights.Bold, ink, align, 0.12));
        stack.Children.Add(Label(ctx.C.Artist, DmSerif, 36 * u, FontWeights.Normal, Alpha(ink, 0xB3), align, new Thickness(0, 8 * u, 0, 0), italic: true));
        ctx.Fit(stack, ctx.H - (2 * 150 * u));
        root.Children.Add(stack);
        var mark = Footer(ctx, Alpha(ink, 0xA0), paper, TextAlignment.Left, new Thickness(padX, 0, 0, 80 * u));
        mark.VerticalAlignment = VerticalAlignment.Bottom;
        root.Children.Add(mark);
        return root;
    }

    /// <summary>A magazine page: masthead, a photo with its caption, the lines as the headline.</summary>
    private static FrameworkElement Editorial(Ctx ctx)
    {
        var u = ctx.U;
        var paper = Color.FromRgb(0xF2, 0xED, 0xE3);
        var ink = Color.FromRgb(0x14, 0x14, 0x14);
        var accent = Luminance(ctx.C.Accent) > 0.65 ? Darken(ctx.C.Accent, 0.5) : ctx.C.Accent;
        var root = new Grid { Background = Brush(paper) };
        var pad = 84 * u;
        var align = ctx.Align(TextAlignment.Left);
        var issue = DateTime.Now;
        var masthead = new Grid { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(pad, 70 * u, pad, 0) };
        masthead.Children.Add(Spaced("LYRICS", Figtree, 28 * u, FontWeights.Black, accent, TextAlignment.Left, 0.3));
        masthead.Children.Add(Spaced($"Nº {issue.DayOfYear:000} · {issue.ToString("MMMM yyyy", CultureInfo.CurrentUICulture).ToUpperInvariant()}",
            Figtree, 24 * u, FontWeights.SemiBold, Alpha(ink, 0xB0), TextAlignment.Right, 0.15));
        root.Children.Add(masthead);
        root.Children.Add(new Border { BorderBrush = Brush(ink), BorderThickness = new Thickness(0, 6 * u, 0, 1.5 * u), Height = 14 * u, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(pad, 118 * u, pad, 0) });

        var bodyTop = 170 * u;
        var showPhoto = ctx.O.ShowCover;
        var byline = new StackPanel();
        byline.Children.Add(Label($"— {ctx.C.Artist}", Figtree, 34 * u, FontWeights.Bold, ink, align));
        byline.Children.Add(Label($"“{ctx.C.Title}”" + (string.IsNullOrWhiteSpace(ctx.C.Album) || ctx.C.Album == ctx.C.Title ? "" : $", {ctx.C.Album}"),
            DmSerif, 32 * u, FontWeights.Normal, Alpha(ink, 0xB3), align, new Thickness(0, 4 * u, 0, 0), italic: true));

        if (ctx.Landscape)
        {
            var photoW = showPhoto ? 760 * u * ctx.Art : 0;
            if (showPhoto)
            {
                var photo = Photo(ctx, photoW, ctx.H - bodyTop - (90 * u));
                photo.HorizontalAlignment = HorizontalAlignment.Left;
                photo.VerticalAlignment = VerticalAlignment.Top;
                photo.Margin = new Thickness(pad, bodyTop, 0, 0);
                root.Children.Add(photo);
            }
            var colW = ctx.W - (2 * pad) - photoW - (showPhoto ? 80 * u : 0);
            var text = new StackPanel { Width = colW, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, bodyTop, pad, 60 * u) };
            var lines = ctx.Lines(LyricCardFont.Serif, 92, ink, align, colW);
            text.Children.Add(lines);
            byline.Margin = new Thickness(0, 44 * u, 0, 0);
            text.Children.Add(byline);
            text.Children.Add(Footer(ctx, ink, paper, align, new Thickness(0, 40 * u, 0, 0)));
            ctx.Fit(text, ctx.H - bodyTop - (80 * u));
            root.Children.Add(text);
            return root;
        }
        var photoH = !showPhoto || ctx.Art < 0.4 ? 0 : ctx.Art * ctx.O.Format switch { LyricCardFormat.Story => 780 * u, LyricCardFormat.Portrait => 400 * u, _ => 260 * u };
        var stack = new StackPanel { Margin = new Thickness(pad, bodyTop, pad, 0), VerticalAlignment = VerticalAlignment.Top };
        if (photoH > 0)
        {
            stack.Children.Add(Photo(ctx, ctx.W - (2 * pad), photoH));
            stack.Children.Add(Label($"Fig. 1 — {ctx.C.Album ?? ctx.C.Title}", Figtree, 22 * u, FontWeights.SemiBold, Alpha(ink, 0x99), TextAlignment.Left, new Thickness(0, 14 * u, 0, 0)));
        }
        var headline = ctx.Lines(LyricCardFont.Serif, 88, ink, align, ctx.W - (2 * pad));
        headline.Margin = new Thickness(0, photoH > 0 ? 46 * u : 40 * u, 0, 0);
        stack.Children.Add(headline);
        byline.Margin = new Thickness(0, 40 * u, 0, 0);
        stack.Children.Add(byline);
        stack.Width = ctx.W - (2 * pad);
        ctx.Fit(stack, ctx.H - bodyTop - (170 * u));
        root.Children.Add(stack);
        var footer = Footer(ctx, ink, paper, TextAlignment.Left, new Thickness(pad, 0, pad, 64 * u));
        footer.VerticalAlignment = VerticalAlignment.Bottom;
        root.Children.Add(footer);
        root.Children.Add(new Border { Background = Brush(ink), Height = 1.5 * u, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(pad, 0, pad, 140 * u) });
        return root;
    }

    /// <summary>A "now playing" screen: cover, progress at the line's moment, controls, the lines as the lyrics panel.</summary>
    private static FrameworkElement Player(Ctx ctx)
    {
        var u = ctx.U;
        var top = Darken(ctx.C.Accent, 0.85);
        var bottom = Color.FromRgb(0x10, 0x10, 0x12);
        var root = new Grid { Background = new LinearGradientBrush(top, bottom, 90) };
        var fg = Colors.White;
        var highlight = ctx.Highlight;
        var align = ctx.Align(TextAlignment.Left);
        var horizontal = ctx.O.Format != LyricCardFormat.Story;
        var pad = (ctx.Landscape ? 130 : 84) * u;

        FrameworkElement Header(double width)
        {
            var header = new Grid { Width = width, Margin = new Thickness(0, 0, 0, 36 * u) };
            header.Children.Add(Glyph("", 30 * u, fg, HorizontalAlignment.Left));
            var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            center.Children.Add(Spaced(LocalizationText("CardPlayingFrom"), Figtree, 20 * u, FontWeights.Bold, Alpha(fg, 0xB3), TextAlignment.Center, 0.12));
            center.Children.Add(Label(ctx.C.Album ?? ctx.C.Artist, Figtree, 26 * u, FontWeights.Bold, fg, TextAlignment.Center, new Thickness(0, 2 * u, 0, 0)));
            header.Children.Add(center);
            header.Children.Add(Glyph("", 30 * u, fg, HorizontalAlignment.Right));
            return header;
        }

        FrameworkElement NowPlaying(double width, double coverSize)
        {
            var panel = new StackPanel { Width = width };
            panel.Children.Add(Header(width));
            if (ctx.O.ShowCover)
                panel.Children.Add(Cover(ctx, coverSize, 18 * u, shadow: true, HorizontalAlignment.Center));
            var titleRow = new Grid { Margin = new Thickness(0, ctx.O.ShowCover ? 44 * u : 0, 0, 0) };
            var names = new StackPanel { Margin = new Thickness(0, 0, 70 * u, 0) };
            names.Children.Add(Label(ctx.C.Title, Figtree, 46 * u, FontWeights.Bold, fg, TextAlignment.Left));
            names.Children.Add(Label(ctx.C.Artist, Figtree, 32 * u, FontWeights.Normal, Alpha(fg, 0xB3), TextAlignment.Left, new Thickness(0, 4 * u, 0, 0)));
            titleRow.Children.Add(names);
            var heart = Glyph("", 44 * u, Color.FromRgb(0x1E, 0xD7, 0x60), HorizontalAlignment.Right);
            heart.VerticalAlignment = VerticalAlignment.Center;
            titleRow.Children.Add(heart);
            panel.Children.Add(titleRow);
            // Progress at the moment of the first chosen line.
            var duration = ctx.C.Duration ?? TimeSpan.Zero;
            var at = ctx.FirstTime ?? TimeSpan.Zero;
            var progress = duration > TimeSpan.Zero ? Math.Clamp(at.TotalSeconds / duration.TotalSeconds, 0, 1) : 0.35;
            var bar = new Grid { Margin = new Thickness(0, 36 * u, 0, 0), Height = 8 * u };
            bar.Children.Add(new Border { Background = Brush(Alpha(fg, 0x40)), CornerRadius = new CornerRadius(4 * u) });
            bar.Children.Add(new Border { Background = Brush(fg), CornerRadius = new CornerRadius(4 * u), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(8 * u, width * progress) });
            panel.Children.Add(bar);
            var times = new Grid { Margin = new Thickness(0, 12 * u, 0, 0) };
            times.Children.Add(Label(Clock(at), Figtree, 22 * u, FontWeights.SemiBold, Alpha(fg, 0xB3), TextAlignment.Left));
            times.Children.Add(Label(duration > TimeSpan.Zero ? "-" + Clock(duration - at) : "", Figtree, 22 * u, FontWeights.SemiBold, Alpha(fg, 0xB3), TextAlignment.Right));
            panel.Children.Add(times);
            var controls = new Grid { Margin = new Thickness(0, 22 * u, 0, 0) };
            foreach (var (glyph, size, col) in new[] { ("", 36.0, 0), ("", 46.0, 1), ("", 0.0, 2), ("", 46.0, 3), ("", 36.0, 4) })
            {
                controls.ColumnDefinitions.Add(new ColumnDefinition());
                FrameworkElement el = size > 0
                    ? Glyph(glyph, size * u, col is 0 ? highlight : fg, HorizontalAlignment.Center)
                    : new Border
                    {
                        Width = 112 * u, Height = 112 * u, CornerRadius = new CornerRadius(56 * u), Background = Brush(fg),
                        Child = Glyph(glyph, 44 * u, bottom, HorizontalAlignment.Center),
                    };
                el.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(el, col);
                controls.Children.Add(el);
            }
            panel.Children.Add(controls);
            return panel;
        }

        Border LyricsPanel(double width, out StackPanel lines, double size)
        {
            var panelBg = Vivid(ctx.C.Accent, 0.32);
            var inner = new StackPanel();
            inner.Children.Add(Spaced(LocalizationText("CardLyricsLabel"), Figtree, 26 * u, FontWeights.Bold, fg, TextAlignment.Left, 0.04));
            lines = ctx.Lines(LyricCardFont.Modern, size, fg, align, width - (2 * 44 * u));
            lines.Margin = new Thickness(0, 26 * u, 0, 0);
            inner.Children.Add(lines);
            return new Border { Width = width, CornerRadius = new CornerRadius(26 * u), Background = Brush(panelBg), Padding = new Thickness(44 * u, 38 * u, 44 * u, 44 * u), Child = inner };
        }

        if (!horizontal)
        {
            var width = ctx.W - (2 * pad);
            var stack = new StackPanel { Margin = new Thickness(pad, 80 * u, pad, 0), VerticalAlignment = VerticalAlignment.Top };
            var coverSize = 640 * u * ctx.Art;
            stack.Children.Add(NowPlaying(width, coverSize));
            var panel = LyricsPanel(width, out var lines, 58);
            panel.Margin = new Thickness(0, 46 * u, 0, 0);
            stack.Children.Add(panel);
            stack.Width = width;
            ctx.Fit(stack, ctx.H - (80 * u) - (130 * u));
            root.Children.Add(stack);
            var footer = Footer(ctx, fg, bottom, TextAlignment.Center, new Thickness(0, 0, 0, 56 * u));
            footer.VerticalAlignment = VerticalAlignment.Bottom;
            root.Children.Add(footer);
            return root;
        }
        var leftW = ctx.Landscape ? 640 * u : 430 * u;
        var gap = (ctx.Landscape ? 110 : 56) * u;
        var rightW = ctx.W - (2 * pad) - leftW - gap;
        var grid = new Grid { Margin = new Thickness(pad, 0, pad, 0), VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftW + gap) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var cover = (ctx.Landscape ? 420 * u : 330 * u) * Math.Max(0.6, ctx.Art);
        var left = NowPlaying(leftW, cover);
        left.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(left);
        var right = LyricsPanel(rightW, out var rightLines, ctx.Landscape ? 60 : 50);
        right.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        ctx.Fit(right, ctx.H - (2 * 100 * u));
        root.Children.Add(grid);
        var mark = Footer(ctx, fg, bottom, TextAlignment.Right, new Thickness(0, 0, pad, 40 * u));
        mark.VerticalAlignment = VerticalAlignment.Bottom;
        mark.HorizontalAlignment = HorizontalAlignment.Right;
        root.Children.Add(mark);
        return root;
    }

    /// <summary>A record sliding out of its sleeve (the cover), the cover again as the label.</summary>
    private static FrameworkElement Vinyl(Ctx ctx)
    {
        var u = ctx.U;
        var bgTop = Darken(ctx.C.Accent, 0.55);
        var root = new Grid
        {
            Background = new RadialGradientBrush(Darken(ctx.C.Accent, 0.75), Color.FromRgb(0x0B, 0x0B, 0x0D))
            { Center = new Point(0.5, 0.25), GradientOrigin = new Point(0.5, 0.25), RadiusX = 1.1, RadiusY = 0.8 },
        };
        var fg = Colors.White;
        var align = ctx.Align(ctx.Landscape ? TextAlignment.Left : TextAlignment.Center);
        var size = ctx.Art * ctx.O.Format switch { LyricCardFormat.Story => 620 * u, LyricCardFormat.Portrait => 430 * u, LyricCardFormat.Landscape => 600 * u, _ => 360 * u };
        var art = RecordAndSleeve(ctx, size, bgTop);
        if (ctx.Landscape)
        {
            art.HorizontalAlignment = HorizontalAlignment.Left;
            art.VerticalAlignment = VerticalAlignment.Center;
            art.Margin = new Thickness(120 * u, 0, 0, 0);
            root.Children.Add(art);
            var colW = ctx.W - (120 * u) - art.Width - (110 * u) - (130 * u);
            var text = new StackPanel { Width = colW, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 130 * u, 0) };
            AddVinylText(ctx, text, fg, align, colW, bgTop);
            ctx.Fit(text, ctx.H - (2 * 100 * u));
            root.Children.Add(text);
            return root;
        }
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        art.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(art);
        var width = ctx.W - (2 * 100 * u);
        var block = new StackPanel { Width = width, Margin = new Thickness(0, 70 * u, 0, 0) };
        AddVinylText(ctx, block, fg, align, width, bgTop);
        stack.Children.Add(block);
        ctx.Fit(block, ctx.H - size - (70 * u) - (2 * 80 * u));
        root.Children.Add(stack);
        return root;
    }

    private static void AddVinylText(Ctx ctx, StackPanel text, Color fg, TextAlignment align, double width, Color bg)
    {
        var u = ctx.U;
        text.Children.Add(Spaced($"{ctx.C.Artist.ToUpperInvariant()} — {ctx.C.Title.ToUpperInvariant()}", Figtree, 26 * u, FontWeights.Bold, ctx.Highlight, align, 0.16));
        var lines = ctx.Lines(LyricCardFont.Modern, 70, fg, align, width);
        lines.Margin = new Thickness(0, 30 * u, 0, 0);
        text.Children.Add(lines);
        text.Children.Add(Footer(ctx, fg, bg, align, new Thickness(0, 56 * u, 0, 0)));
    }

    private static Grid RecordAndSleeve(Ctx ctx, double size, Color bg)
    {
        var u = ctx.U;
        var sleeve = ctx.O.ShowCover;
        var shift = sleeve ? size * 0.42 : 0;
        var art = new Grid { Width = size + shift, Height = size };
        // The record.
        var record = new Grid { Width = size * 0.96, Height = size * 0.96, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        record.Children.Add(new Ellipse { Fill = Brush(Color.FromRgb(0x0D, 0x0D, 0x0F)), Effect = new DropShadowEffect { BlurRadius = 40 * u, ShadowDepth = 10 * u, Opacity = 0.5, Color = Colors.Black } });
        for (var i = 0; i < 18; i++)
        {
            var inset = size * (0.04 + (i * 0.016));
            record.Children.Add(new Ellipse { Margin = new Thickness(inset), Stroke = Brush(Color.FromArgb((byte)(i % 3 == 0 ? 0x30 : 0x18), 0xFF, 0xFF, 0xFF)), StrokeThickness = Math.Max(1, 1.4 * u) });
        }
        record.Children.Add(new Ellipse
        {
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new(Colors.Transparent, 0.3), new(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF), 0.48), new(Colors.Transparent, 0.62),
            }, new Point(0, 0), new Point(1, 1)),
        });
        var label = size * 0.36;
        record.Children.Add(new Ellipse
        {
            Width = label, Height = label,
            Fill = ctx.C.Cover is { } c ? new ImageBrush(c) { Stretch = Stretch.UniformToFill } : Brush(Vivid(ctx.C.Accent, 0.5)),
        });
        record.Children.Add(new Ellipse { Width = size * 0.035, Height = size * 0.035, Fill = Brush(bg) });
        art.Children.Add(record);
        if (sleeve)
        {
            var cover = Cover(ctx, size, 6 * u, shadow: true, HorizontalAlignment.Left);
            art.Children.Add(cover);
        }
        return art;
    }

    /// <summary>A karaoke screen: the first chosen line lit as it is sung, the next ones waiting.</summary>
    private static FrameworkElement Karaoke(Ctx ctx)
    {
        var u = ctx.U;
        var glow = ctx.Highlight;
        var root = new Grid { Background = Brush(Color.FromRgb(0x08, 0x08, 0x10)) };
        root.Children.Add(new Border
        {
            Background = new RadialGradientBrush(Color.FromArgb(0x70, glow.R, glow.G, glow.B), Colors.Transparent)
            { Center = new Point(0.5, 1.05), GradientOrigin = new Point(0.5, 1.05), RadiusX = 0.9, RadiusY = 0.55 },
        });
        root.Children.Add(new Border
        {
            Background = new RadialGradientBrush(Color.FromArgb(0x38, 0x8A, 0x5C, 0xFF), Colors.Transparent)
            { Center = new Point(0.1, -0.05), GradientOrigin = new Point(0.1, -0.05), RadiusX = 0.8, RadiusY = 0.5 },
        });
        var align = ctx.Align(TextAlignment.Center);
        var width = ctx.W - (2 * (ctx.Landscape ? 220 : 100) * u);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = width };

        var head = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = ToHorizontal(align), Margin = new Thickness(0, 0, 0, 70 * u) };
        if (ctx.O.ShowCover)
            head.Children.Add(new Ellipse { Width = 92 * u, Height = 92 * u, Fill = ctx.C.Cover is { } c ? new ImageBrush(c) { Stretch = Stretch.UniformToFill } : Brush(glow), Margin = new Thickness(0, 0, 24 * u, 0) });
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Label(ctx.C.Title, Figtree, 38 * u, FontWeights.Bold, Colors.White, TextAlignment.Left));
        names.Children.Add(Label(ctx.C.Artist, Figtree, 30 * u, FontWeights.Normal, Alpha(Colors.White, 0x99), TextAlignment.Left));
        head.Children.Add(names);
        stack.Children.Add(head);

        var face = ctx.Face(LyricCardFont.Modern);
        var size = ctx.LyricSize(82);
        var lines = new StackPanel { Width = width };
        for (var i = 0; i < ctx.C.Lines.Count; i++)
        {
            var block = ctx.LineBlock(ctx.C.Lines[i].Text, face, size, Colors.White, align, width);
            if (i == 0)
            {
                // Sung up to here: lit, with the "wipe" edge partway through the line.
                block.Foreground = new LinearGradientBrush(new GradientStopCollection
                {
                    new(glow, 0), new(Lighten(glow, 0.35), 0.62), new(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0.62), new(Colors.White, 1),
                }, new Point(0, 0.5), new Point(1, 0.5));
                block.Effect = new DropShadowEffect { BlurRadius = 34 * u, ShadowDepth = 0, Color = glow, Opacity = 0.9 };
            }
            else
            {
                block.Foreground = Brush(Alpha(Colors.White, (byte)Math.Max(0x45, 0xA0 - (i * 0x18))));
                block.FontSize = size * 0.86;
                block.LineHeight = size * 0.86 * face.LineHeight;
            }
            block.Margin = new Thickness(0, i == 0 ? 0 : size * 0.32, 0, 0);
            lines.Children.Add(block);
        }
        stack.Children.Add(lines);

        // Beat dots under the lines.
        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = ToHorizontal(align), Margin = new Thickness(0, 60 * u, 0, 0) };
        for (var i = 0; i < 4; i++)
            dots.Children.Add(new Ellipse { Width = 18 * u, Height = 18 * u, Margin = new Thickness(0, 0, 18 * u, 0), Fill = Brush(i < 3 ? glow : Alpha(Colors.White, 0x40)) });
        stack.Children.Add(dots);
        stack.Children.Add(Footer(ctx, Colors.White, Color.FromRgb(0x08, 0x08, 0x10), align, new Thickness(0, 60 * u, 0, 0)));
        ctx.Fit(stack, ctx.H - (2 * 80 * u));
        root.Children.Add(stack);
        return root;
    }

    /// <summary>A classic desktop: a bevelled text window with the lines, a small player window, a task bar.</summary>
    private static FrameworkElement Retro(Ctx ctx)
    {
        var u = ctx.U * (ctx.O.Format == LyricCardFormat.Square ? 0.92 : 1);
        var desktop = Luminance(ctx.C.Accent) > 0.6 ? Darken(ctx.C.Accent, 0.6) : ctx.C.Accent;
        var root = new Grid { Background = Brush(desktop) };
        var face = Color.FromRgb(0xC3, 0xC3, 0xC3);
        var taskbarH = 64 * u;

        // Task bar.
        var taskbar = new Border { Height = taskbarH, VerticalAlignment = VerticalAlignment.Bottom, Background = Brush(face), BorderBrush = Brush(Colors.White), BorderThickness = new Thickness(0, 3 * u, 0, 0) };
        var bar = new Grid();
        var start = Bevel(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock { Text = "♫", FontSize = 30 * u, FontWeight = FontWeights.Bold, Foreground = Brush(Color.FromRgb(0, 0, 0x80)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10 * u, 0) },
                new TextBlock { Text = "Taskitify", FontFamily = Classic95, FontSize = 26 * u, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center },
            },
        }, u, raised: true);
        start.HorizontalAlignment = HorizontalAlignment.Left;
        start.Margin = new Thickness(8 * u, 7 * u, 0, 7 * u);
        bar.Children.Add(start);
        var clock = Bevel(new TextBlock { Text = ctx.FirstTime is { } t ? Clock(t) : DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture), FontFamily = Classic95, FontSize = 24 * u, VerticalAlignment = VerticalAlignment.Center }, u, raised: false);
        clock.HorizontalAlignment = HorizontalAlignment.Right;
        clock.Margin = new Thickness(0, 7 * u, 8 * u, 7 * u);
        bar.Children.Add(clock);
        taskbar.Child = bar;
        root.Children.Add(taskbar);

        // Desktop signature, like an icon label.
        var mark = Footer(ctx, Colors.White, desktop, TextAlignment.Left, new Thickness(50 * u, 0, 0, taskbarH + (36 * u)));
        mark.VerticalAlignment = VerticalAlignment.Bottom;
        root.Children.Add(mark);

        // The text window.
        var landscape = ctx.Landscape;
        var winW = landscape ? 1100 * u : ctx.W - (2 * 70 * u);
        var textW = winW - (2 * 50 * u);
        var lines = ctx.Lines(LyricCardFont.Mono, 74, Color.FromRgb(0x10, 0x10, 0x10), ctx.Align(TextAlignment.Left), textW);
        // A blinking caret after the last line.
        if (lines.Children.Count > 0 && lines.Children[^1] is TextBlock last)
            last.Inlines.Add(new System.Windows.Documents.Run("█") { Foreground = Brush(Color.FromRgb(0, 0, 0x80)) });
        var page = new Border { Background = Brush(Colors.White), Padding = new Thickness(30 * u, 26 * u, 30 * u, 34 * u), Child = lines };
        var content = new StackPanel();
        var menu = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6 * u, 4 * u, 0, 8 * u) };
        foreach (var item in new[] { "File", "Edit", "Search", "Help" })
            menu.Children.Add(new TextBlock { Text = item, FontFamily = Classic95, FontSize = 24 * u, Margin = new Thickness(0, 0, 28 * u, 0) });
        content.Children.Add(menu);
        content.Children.Add(Bevel(page, u, raised: false, padding: 0));
        var window = Window95($"{ctx.C.Title}.txt", content, winW, u, active: true);
        window.HorizontalAlignment = HorizontalAlignment.Left;
        window.VerticalAlignment = VerticalAlignment.Top;
        window.Margin = new Thickness(70 * u, (landscape ? 70 : ctx.O.Format == LyricCardFormat.Story ? 170 : 70) * u, 0, 0);
        var showPlayer = ctx.Art >= 0.6;
        var playerH = !showPlayer ? 0 : ctx.O.ShowCover ? 330 * u : 220 * u;
        var windowTop = (landscape ? 70 : ctx.O.Format == LyricCardFormat.Story ? 170 : 70) * u;
        var windowRoom = ctx.H - windowTop - taskbarH - (landscape ? 90 * u : playerH + (260 * u));
        // A story has room for a taller page; the text sits at its top like in an editor.
        page.MinHeight = ctx.O.Format == LyricCardFormat.Story ? windowRoom * 0.62 : 0;
        ctx.Fit(window, windowRoom);
        root.Children.Add(window);

        // The player window.
        var playerW = landscape ? 600 * u : Math.Min(700 * u, ctx.W - (2 * 110 * u));
        var player = new StackPanel();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (ctx.O.ShowCover)
            row.Children.Add(Bevel(new Border { Width = 190 * u, Height = 190 * u, Background = ctx.C.Cover is { } img ? new ImageBrush(img) { Stretch = Stretch.UniformToFill } : Brush(desktop) }, u, raised: false, padding: 0));
        var info = new StackPanel { Margin = new Thickness(ctx.O.ShowCover ? 24 * u : 0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Width = playerW - (ctx.O.ShowCover ? 190 * u + 24 * u : 0) - (60 * u) };
        info.Children.Add(new TextBlock { Text = ctx.C.Title, FontFamily = Classic95, FontWeight = FontWeights.Bold, FontSize = 30 * u, TextTrimming = TextTrimming.CharacterEllipsis });
        info.Children.Add(new TextBlock { Text = ctx.C.Artist, FontFamily = Classic95, FontSize = 26 * u, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4 * u, 0, 16 * u) });
        var seek = new Grid { Height = 18 * u };
        seek.Children.Add(Bevel(new Border(), u, raised: false, padding: 0));
        seek.Children.Add(new Border { Width = 30 * u, Height = 26 * u, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(info.Width * 0.38, 0, 0, 0), Background = Brush(face), BorderBrush = Brush(Colors.Black), BorderThickness = new Thickness(0, 0, 2 * u, 2 * u) });
        info.Children.Add(seek);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18 * u, 0, 0) };
        foreach (var g in new[] { "⏮", "▶", "⏸", "⏭" })
            buttons.Children.Add(Bevel(new TextBlock { Text = g, FontSize = 22 * u, Width = 34 * u, TextAlignment = TextAlignment.Center }, u, raised: true, margin: new Thickness(0, 0, 8 * u, 0)));
        info.Children.Add(buttons);
        row.Children.Add(info);
        player.Children.Add(row);
        var playerWindow = Window95(LocalizationText("CardNowPlaying"), player, playerW, u, active: false);
        playerWindow.HorizontalAlignment = HorizontalAlignment.Right;
        playerWindow.VerticalAlignment = VerticalAlignment.Bottom;
        playerWindow.Margin = new Thickness(0, 0, 60 * u, taskbarH + ((landscape ? 120 : 150) * u));
        if (showPlayer)
            root.Children.Add(playerWindow);
        return root;
    }

    private static Border Window95(string title, UIElement content, double width, double u, bool active)
    {
        var titleBar = new Grid { Height = 50 * u };
        titleBar.Background = active
            ? new LinearGradientBrush(Color.FromRgb(0x00, 0x00, 0x80), Color.FromRgb(0x10, 0x84, 0xD0), 0)
            : new LinearGradientBrush(Color.FromRgb(0x80, 0x80, 0x80), Color.FromRgb(0xB5, 0xB5, 0xB5), 0);
        titleBar.Children.Add(new TextBlock { Text = title, FontFamily = Classic95, FontWeight = FontWeights.Bold, FontSize = 26 * u, Foreground = Brush(Colors.White), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14 * u, 0, 150 * u, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        var caps = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 8 * u, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var g in new[] { "▁", "□", "✕" })
            caps.Children.Add(Bevel(new TextBlock { Text = g, FontSize = 18 * u, FontWeight = FontWeights.Bold, Width = 26 * u, TextAlignment = TextAlignment.Center }, u, raised: true, margin: new Thickness(4 * u, 0, 0, 0), padding: 2 * u));
        titleBar.Children.Add(caps);
        var body = new StackPanel();
        body.Children.Add(titleBar);
        body.Children.Add(new Border { Padding = new Thickness(10 * u, 6 * u, 10 * u, 12 * u), Child = content });
        var face = Color.FromRgb(0xC3, 0xC3, 0xC3);
        return new Border
        {
            Width = width,
            Background = Brush(face),
            BorderBrush = Brush(Colors.Black),
            BorderThickness = new Thickness(0, 0, 3 * u, 3 * u),
            Effect = new DropShadowEffect { BlurRadius = 0, ShadowDepth = 10 * u, Direction = 315, Opacity = 0.35, Color = Colors.Black },
            Child = new Border { BorderBrush = Brush(Colors.White), BorderThickness = new Thickness(3 * u, 3 * u, 0, 0), Padding = new Thickness(4 * u), Child = body },
        };
    }

    /// <summary>A raised (button) or sunken (field) 3D edge.</summary>
    private static Border Bevel(UIElement child, double u, bool raised, Thickness margin = default, double? padding = null)
    {
        var light = Brush(Colors.White);
        var dark = Brush(Color.FromRgb(0x80, 0x80, 0x80));
        var pad = padding ?? 8 * u;
        return new Border
        {
            Margin = margin,
            BorderBrush = raised ? light : dark,
            BorderThickness = new Thickness(3 * u, 3 * u, 0, 0),
            Background = Brush(Color.FromRgb(0xC3, 0xC3, 0xC3)),
            Child = new Border
            {
                BorderBrush = raised ? Brush(Colors.Black) : light,
                BorderThickness = new Thickness(0, 0, 3 * u, 3 * u),
                Padding = new Thickness(pad),
                Child = child,
            },
        };
    }

    // ======================= Parts =======================

    /// <summary>Cover, title and artist side by side.</summary>
    private static FrameworkElement SongRow(Ctx ctx, Color fg, double cover, TextAlignment align, Thickness margin = default)
    {
        var u = ctx.U;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = margin, HorizontalAlignment = ToHorizontal(align) };
        if (ctx.O.ShowCover)
            row.Children.Add(Cover(ctx, cover, 10 * u, shadow: false, HorizontalAlignment.Left));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(ctx.O.ShowCover ? 26 * u : 0, 0, 0, 0) };
        var nameAlign = ctx.O.ShowCover ? TextAlignment.Left : align;
        text.Children.Add(Label(ctx.C.Title, Figtree, 40 * u, FontWeights.Bold, fg, nameAlign));
        text.Children.Add(Label(ctx.C.Artist, Figtree, 32 * u, FontWeights.Medium, Alpha(fg, 0xCC), nameAlign, new Thickness(0, 2 * u, 0, 0)));
        row.Children.Add(text);
        return row;
    }

    private static FrameworkElement Cover(Ctx ctx, double size, double radius, bool shadow, HorizontalAlignment align)
    {
        var border = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(radius),
            HorizontalAlignment = align,
            Background = ctx.C.Cover is { } cover
                ? new ImageBrush(cover) { Stretch = Stretch.UniformToFill }
                : new LinearGradientBrush(ctx.C.GradientStart, ctx.C.GradientEnd, 45),
        };
        if (ctx.C.Cover is null)
            border.Child = new TextBlock { Text = "♪", FontSize = size * 0.45, Foreground = Brush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (shadow)
            border.Effect = new DropShadowEffect { BlurRadius = 46 * ctx.U, ShadowDepth = 14 * ctx.U, Direction = 270, Opacity = 0.42, Color = Colors.Black };
        return border;
    }

    /// <summary>A cover cropped to a rectangle (the editorial photo).</summary>
    private static FrameworkElement Photo(Ctx ctx, double width, double height) => new Border
    {
        Width = width,
        Height = height,
        Background = ctx.C.Cover is { } cover
            ? new ImageBrush(cover) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Center }
            : new LinearGradientBrush(ctx.C.GradientStart, ctx.C.GradientEnd, 45),
    };

    /// <summary>The signature and, when chosen, the line's time, in one row.</summary>
    private static FrameworkElement Footer(Ctx ctx, Color fg, Color bg, TextAlignment align, Thickness margin)
    {
        var u = ctx.U;
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = ToHorizontal(align), Margin = margin };
        if (Mark(ctx, fg, bg) is { } mark)
            row.Children.Add(mark);
        if (ctx.O.ShowTimestamp && ctx.FirstTime is { } time)
        {
            // Quiet, in the signature's own type: a detail, not a badge.
            var text = (row.Children.Count > 0 ? "\u00B7  " : "") + Clock(time);
            row.Children.Add(new TextBlock
            {
                Text = text,
                FontFamily = Figtree,
                FontSize = 27 * u,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(Alpha(fg, 0xA6)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(row.Children.Count > 0 ? 18 * u : 0, 0, 0, 3 * u),
            });
        }
        return row;
    }

    /// <summary>Spotify's or Taskitify's signature, or nothing.</summary>
    private static FrameworkElement? Mark(Ctx ctx, Color fg, Color bg)
    {
        var u = ctx.U;
        var height = 36 * u;
        if (ctx.O.Mark == LyricCardMark.None)
            return null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (ctx.O.Mark == LyricCardMark.Spotify)
        {
            row.Children.Add(SpotifyLogo(height, fg, bg));
            row.Children.Add(new TextBlock { Text = "Spotify", FontFamily = Figtree, FontSize = 31 * u, FontWeight = FontWeights.SemiBold, Foreground = Brush(fg), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12 * u, 0, 0, 3 * u) });
            return row;
        }
        row.Children.Add(new Border
        {
            Width = height, Height = height, CornerRadius = new CornerRadius(height / 2), Background = Brush(fg),
            Child = new TextBlock { Text = "♫", FontSize = 27 * u, FontWeight = FontWeights.Bold, Foreground = Brush(bg), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2 * u, 0, 0) },
        });
        row.Children.Add(new TextBlock { Text = "Taskitify", FontFamily = Figtree, FontSize = 29 * u, FontWeight = FontWeights.SemiBold, Foreground = Brush(fg), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12 * u, 0, 0, 3 * u) });
        return row;
    }

    /// <summary>Spotify's round mark in one colour: a disc with three arcs cut out of it.</summary>
    internal static FrameworkElement SpotifyLogo(double size, Color fg, Color bg)
    {
        var canvas = new Canvas { Width = 100, Height = 100 };
        canvas.Children.Add(new Ellipse { Width = 100, Height = 100, Fill = Brush(fg) });
        foreach (var (data, thickness) in new[]
        {
            ("M 22,36.5 C 41,30 63,31.5 79,41.5", 9.6),
            ("M 26,53 C 42,48 60,49 73.5,57.5", 7.8),
            ("M 29.5,67.5 C 42,64 56,65 67,71.5", 6.2),
        })
        {
            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(data),
                Stroke = Brush(bg),
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
        }
        return new Viewbox { Width = size, Height = size, Child = canvas, VerticalAlignment = VerticalAlignment.Center };
    }

    private static TextBlock Label(string text, FontFamily font, double size, FontWeight weight, Color color, TextAlignment align, Thickness margin = default, bool italic = false) => new()
    {
        Text = text,
        FontFamily = font,
        FontSize = size,
        FontWeight = weight,
        FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
        Foreground = Brush(color),
        TextAlignment = align,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = margin,
    };

    /// <summary>Small capitals with tracking (letter spacing), drawn as spaced runs.</summary>
    private static TextBlock Spaced(string text, FontFamily font, double size, FontWeight weight, Color color, TextAlignment align, double tracking)
    {
        var block = Label(string.Empty, font, size, weight, color, align);
        // WPF has no letter spacing: thin spaces between the letters do the same.
        var spacer = tracking >= 0.2 ? " " : " ";
        block.Text = string.Join(spacer, text.EnumerateRunes().Select(r => r.ToString()));
        return block;
    }

    private static TextBlock Glyph(string glyph, double size, Color color, HorizontalAlignment align) => new()
    {
        Text = glyph,
        FontFamily = Icons,
        FontSize = size,
        Foreground = Brush(color),
        HorizontalAlignment = align,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static string Clock(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    private static HorizontalAlignment ToHorizontal(TextAlignment align) => align switch
    {
        TextAlignment.Center => HorizontalAlignment.Center,
        TextAlignment.Right => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Left,
    };

    private static string LocalizationText(string key) => Services.LocalizationService.Get(key);

    private static Color Darken(Color c, double factor) => Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

    private static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + ((255 - c.R) * amount)), (byte)(c.G + ((255 - c.G) * amount)), (byte)(c.B + ((255 - c.B) * amount)));

    private static Color Alpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static SolidColorBrush Brush(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    private static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max == min) return (0, 0, l);
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? ((g - b) / d) + (g < b ? 6 : 0) : max == g ? ((b - r) / d) + 2 : ((r - g) / d) + 4;
        return (h / 6, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            return t < 1 / 6.0 ? p + ((q - p) * 6 * t) : t < 0.5 ? q : t < 2 / 3.0 ? p + ((q - p) * ((2 / 3.0) - t) * 6) : p;
        }
        var q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
        var p = (2 * l) - q;
        return Color.FromRgb((byte)Math.Round(Hue(p, q, h + (1 / 3.0)) * 255), (byte)Math.Round(Hue(p, q, h) * 255), (byte)Math.Round(Hue(p, q, h - (1 / 3.0)) * 255));
    }

    internal readonly record struct Face(FontFamily Family, FontWeight Weight, FontStyle Style, FontStretch Stretch, double SizeFactor, double LineHeight);

    /// <summary>One build of a card: its size, options, and the parts that must fit.</summary>
    private sealed class Ctx(LyricCardContent content, LyricCardOptions options, double width, double height, double art)
    {
        private readonly List<(FrameworkElement Element, double MaxHeight)> _fits = [];
        private readonly List<TextBlock> _lyrics = [];

        public LyricCardContent C { get; } = content;
        public LyricCardOptions O { get; } = options;
        public double W { get; } = width;
        public double H { get; } = height;

        /// <summary>One pixel of the 1080 px design, on this format.</summary>
        public double U { get; } = Math.Min(width, height) / 1080.0;
        public bool Landscape => W > H;

        /// <summary>How large the artwork (cover, photo, record) is drawn: smaller when the lines need the room.</summary>
        public double Art { get; } = art;

        /// <summary>The time of the first chosen line, as sung.</summary>
        public TimeSpan? FirstTime => C.Lines.Count > 0 ? C.Lines[0].Time : null;

        /// <summary>A bright version of the card colour, for glows and accents on dark backgrounds.</summary>
        public Color Highlight => Vivid(C.Accent, 0.6);

        public TextAlignment Align(TextAlignment styleDefault) => O.Align switch
        {
            LyricCardAlign.Left => TextAlignment.Left,
            LyricCardAlign.Center => TextAlignment.Center,
            LyricCardAlign.Right => TextAlignment.Right,
            _ => styleDefault,
        };

        public Face Face(LyricCardFont styleDefault) => (O.Font == LyricCardFont.Auto ? styleDefault : O.Font) switch
        {
            LyricCardFont.Serif => new(DmSerif, FontWeights.Normal, FontStyles.Normal, FontStretches.Normal, 1.06, 1.1),
            LyricCardFont.Handwritten => new(Hand, FontWeights.Normal, FontStyles.Normal, FontStretches.Normal, 0.98, 1.24),
            LyricCardFont.Mono => new(Mono, FontWeights.SemiBold, FontStyles.Normal, FontStretches.Normal, 0.8, 1.32),
            LyricCardFont.Condensed => new(Condensed, FontWeights.Bold, FontStyles.Normal, FontStretches.SemiCondensed, 1.08, 1.08),
            _ => new(Figtree, FontWeights.ExtraBold, FontStyles.Normal, FontStretches.Normal, 1, 1.08),
        };

        /// <summary>The style's preferred size before measuring the wrapped lines.</summary>
        public double LyricSize(double large)
        {
            var format = O.Format switch { LyricCardFormat.Story => 1.12, LyricCardFormat.Landscape => 1.12, LyricCardFormat.Square => 0.94, _ => 1.0 };
            return large * U * format * Math.Clamp(O.FontScale, 0.6, 1.5);
        }

        public StackPanel Lines(LyricCardFont styleDefault, double large, Color color, TextAlignment align, double width, Face? fixedFace = null, bool dropInitial = false)
        {
            var face = fixedFace ?? Face(styleDefault);
            var size = LyricSize(large) * face.SizeFactor;
            var panel = new StackPanel { Width = width };
            foreach (var line in C.Lines)
            {
                var text = line.Text;
                if (dropInitial && !string.IsNullOrWhiteSpace(text))
                {
                    text = text.TrimStart();
                    text = text[StringInfo.GetNextTextElement(text).Length..];
                    dropInitial = false;
                }
                var block = LineBlock(text, face, size, color, align, width);
                if (panel.Children.Count > 0)
                    block.Margin = new Thickness(0, size * 0.14, 0, 0);
                panel.Children.Add(block);
            }
            return panel;
        }

        public TextBlock LineBlock(string text, Face face, double size, Color color, TextAlignment align, double width)
        {
            var block = new TextBlock
            {
                Text = text,
                FontFamily = face.Family,
                FontWeight = face.Weight,
                FontStyle = face.Style,
                FontStretch = face.Stretch,
                FontSize = size,
                Foreground = Brush(color),
                TextAlignment = align,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = size * face.LineHeight,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Width = width,
            };
            _lyrics.Add(block);
            return block;
        }

        /// <summary>This part must be no taller than <paramref name="maxHeight"/>, or the lines are set smaller.</summary>
        public void Fit(FrameworkElement element, double maxHeight) => _fits.Add((element, Math.Max(maxHeight, 120 * U)));

        public bool FitLyrics(double minimum)
        {
            if (AllFit())
                return true;

            // Capture after composing: Karaoke gives adjacent verses their own size and spacing.
            var original = _lyrics.Select(line => (Line: line, line.FontSize, line.LineHeight, line.Margin)).ToArray();
            void Apply(double scale)
            {
                foreach (var (line, size, height, margin) in original)
                {
                    line.FontSize = size * scale;
                    line.LineHeight = height * scale;
                    line.Margin = new Thickness(margin.Left, margin.Top * scale, margin.Right, margin.Bottom * scale);
                    // This card is still detached, so layout invalidation has no window to propagate it.
                    for (DependencyObject? parent = line; parent is UIElement element; parent = VisualTreeHelper.GetParent(element))
                        element.InvalidateMeasure();
                }
            }

            Apply(minimum);
            if (!AllFit())
                return false;

            var upper = 1.0;
            for (var i = 0; i < 6; i++)
            {
                var scale = (minimum + upper) / 2;
                Apply(scale);
                if (AllFit())
                    minimum = scale;
                else
                    upper = scale;
            }
            Apply(minimum);
            return true;
        }

        private bool AllFit() => _fits.All(f =>
        {
            f.Element.Measure(new Size(f.Element.Width is > 0 and var w ? w : W, double.PositiveInfinity));
            return f.Element.DesiredSize.Height <= f.MaxHeight;
        });
    }
}
