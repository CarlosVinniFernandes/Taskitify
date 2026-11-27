using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Taskitify.Models;
using Taskitify.ViewModels;

namespace Taskitify.Views;

internal readonly record struct MiniPlayerLookSample(string Title, string Artist, BitmapSource? Cover,
    string? Previous, string? Current, string? Next, bool Playing, double Progress);

internal static class MiniPlayerLookThumbnails
{
    private static readonly Geometry Play = Geometry.Parse("M-3,-5 L5,0 -3,5 Z");
    private static readonly Geometry Pause = Geometry.Parse("M-4,-5 H-1 V5 H-4 Z M2,-5 H5 V5 H2 Z");
    private static readonly Geometry Previous = Geometry.Parse("M-5,-4 H-3 V4 H-5 Z M4,-5 L-3,0 4,5 Z");
    private static readonly Geometry Next = Geometry.Parse("M3,-4 H5 V4 H3 Z M-4,-5 L3,0 -4,5 Z");

    public static ImageSource Background(MiniPlayerBackground kind, BitmapSource? cover,
        (Color Start, Color End) palette, Color theme, bool albumGradient)
    {
        if (MiniPlayerBackdrop.IsAtmospheric(kind))
            return MiniPlayerBackdrop.Render(cover, kind, 72, 96);
        var useTheme = kind == MiniPlayerBackground.Theme || (kind == MiniPlayerBackground.Default && !albumGradient);
        Brush fill = useTheme ? Ink(theme) : new LinearGradientBrush(palette.End, palette.Start, new Point(0, 0), new Point(1, 1));
        return Draw(72, 96, (dc, bounds) => dc.DrawRectangle(fill, null, bounds));
    }

    public static ImageSource Player(MiniPlayerLayout layout, LookPreset look, MiniPlayerLookSample song, ImageSource background, Color color)
    {
        var wide = layout == MiniPlayerLayout.Cinema;
        var width = wide ? 160 : 96;
        var height = wide ? 84 : 116;
        var ui = MiniPlayerViewModel.FontFor(LyricsTypeface.System);
        var font = MiniPlayerViewModel.FontFor(look.Typeface);
        return Draw(width, height, (dc, bounds) =>
        {
            dc.PushClip(new RectangleGeometry(bounds, 5, 5));
            dc.DrawImage(background, bounds);
            var cover = wide ? new Rect(8, 8, 31, 31) : new Rect(8, 8, 20, 20);
            dc.DrawRectangle(Ink(color, 0x28), null, cover);
            if (song.Cover is not null)
                dc.DrawImage(song.Cover, cover);
            var headerX = wide ? 8 : 34;
            var headerY = wide ? 42 : 7;
            var headerWidth = wide ? 48 : width - headerX - 6;
            Text(dc, song.Title, ui, 8, color, headerX, headerY, headerWidth, bold: true);
            Text(dc, song.Artist, ui, 7, color, headerX, headerY + 10, headerWidth, opacity: 0xB0);

            var lyricsX = wide ? 61 : 8;
            var lyricsWidth = width - lyricsX - 8;
            var lyricsY = wide ? 10 : 36;
            var alignment = look.Alignment == LyricsAlignment.Left ? TextAlignment.Left : TextAlignment.Center;
            string?[] verses = [song.Previous, song.Current, song.Next];
            for (var i = 0; i < verses.Length; i++)
            {
                var opacity = LineOpacity(look.Highlight, i - 1);
                var y = lyricsY + i * (wide ? 14 : 17);
                var expressive = i == 1 && look.Highlight == LyricsHighlight.Expressive;
                if (verses[i] is { } verse)
                    Text(dc, verse, font, expressive ? 10.5 : 8.5, color, lyricsX, y, lyricsWidth, i == 1, opacity, alignment);
                else
                    Line(dc, lyricsX, y + 5, lyricsWidth, i == 1 ? .86 : .58, alignment, color, 0x45, expressive ? 3 : 2);
            }

            var progressY = height - 21;
            dc.DrawRoundedRectangle(Ink(color, 0x45), null, new Rect(8, progressY, width - 16, 2), 1, 1);
            if (song.Progress > 0)
                dc.DrawRoundedRectangle(Ink(color), null, new Rect(8, progressY, (width - 16) * song.Progress, 2), 1, 1);
            Transport(dc, width / 2.0, height - 10, look.Controls, color, song.Playing);
            dc.Pop();
        });
    }

    public static ImageSource Highlight(LyricsHighlight highlight, Color color) =>
        Diagram((dc, bounds) =>
        {
            for (var row = -2; row <= 2; row++)
                Line(dc, 12, bounds.Height / 2 + row * 7, bounds.Width - 24, row == 0 ? .88 : .62,
                    TextAlignment.Center, color, LineOpacity(highlight, row), row == 0 && highlight == LyricsHighlight.Expressive ? 5 : 3);
        });

    public static ImageSource Motion(LyricMotionMode motion, Color color) =>
        Diagram((dc, bounds) =>
        {
            var expressive = motion == LyricMotionMode.Expressive;
            var x = expressive ? 38 : 34;
            Line(dc, x, 12, 46, .60, TextAlignment.Center, color, 0x55, 3);
            Line(dc, x, 22, 46, expressive ? 1 : .80, TextAlignment.Center, color, 0xFF, expressive ? 5 : 3);
            Line(dc, x, 32, 46, .60, TextAlignment.Center, color, 0x66, 3);
            if (motion != LyricMotionMode.Off)
            {
                var pen = new Pen(Ink(color), 1.5);
                dc.DrawLine(pen, new Point(20, 32), new Point(20, expressive ? 12 : 16));
                var tip = expressive ? 12 : 16;
                dc.DrawLine(pen, new Point(16, tip + 4), new Point(20, tip));
                dc.DrawLine(pen, new Point(24, tip + 4), new Point(20, tip));
            }
            else
                dc.DrawRoundedRectangle(Ink(color, 0x80), null, new Rect(16, 20, 8, 8), 1, 1);
        });

    public static ImageSource Typeface(LyricsTypeface kind, Color color) =>
        Diagram((dc, bounds) => Text(dc, "Aa", MiniPlayerViewModel.FontFor(kind), 27, color, 0, 4, bounds.Width,
            kind != LyricsTypeface.System, alignment: TextAlignment.Center));

    public static ImageSource Alignment(LyricsAlignment kind, Color color) =>
        Diagram((dc, bounds) =>
        {
            var align = kind == LyricsAlignment.Left ? TextAlignment.Left : TextAlignment.Center;
            Line(dc, 20, 13, 56, 1, align, color, 0xE0, 3);
            Line(dc, 20, 22, 56, .56, align, color, 0xE0, 3);
            Line(dc, 20, 31, 56, .78, align, color, 0xE0, 3);
        });

    public static ImageSource Controls(ControlsLook kind, Color color) =>
        Diagram((dc, bounds) => Transport(dc, bounds.Width / 2, bounds.Height / 2, kind, color, playing: false));

    // ---- Minimal Mode: the lyrics over a desktop (a real photo, so clear and translucent read as such) ----

    private static BitmapSource? _wallpaper;

    private static BitmapSource Wallpaper()
    {
        if (_wallpaper is null)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri("pack://application:,,,/Resources/Tour/fuji.jpg");
            image.DecodePixelWidth = 240;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            _wallpaper = image;
        }
        return _wallpaper;
    }

    /// <summary>A desktop with a floating verse on it: the given background, text colour and legibility.</summary>
    public static ImageSource MinimalScene(MinimalBackdrop backdrop, double opacity, MinimalTextColor textColor, MinimalLegibility legibility,
        BitmapSource? cover, (Color Start, Color End) palette, string verse)
    {
        var ink = MinimalInk.Text(textColor, palette);
        var panel = MinimalInk.Backdrop(backdrop, opacity, cover, palette, ink);
        return Draw(112, 64, (dc, bounds) =>
        {
            dc.PushClip(new RectangleGeometry(bounds, 5, 5));
            var photo = Wallpaper();
            // Filled and a little zoomed in: snow and sky, light and dark parts, as a real desktop has.
            var zoom = Math.Max(bounds.Width / photo.PixelWidth, bounds.Height / photo.PixelHeight) * 1.15;
            var (w, h) = (photo.PixelWidth * zoom, photo.PixelHeight * zoom);
            dc.DrawImage(photo, new Rect((bounds.Width - w) / 2, (bounds.Height - h) * 0.65, w, h));
            var card = new Rect(10, 18, bounds.Width - 20, 30);
            if (panel is not null)
                dc.DrawRoundedRectangle(panel, null, card, 6, 6);
            var font = MiniPlayerViewModel.FontFor(LyricsTypeface.Modern);
            var halo = MinimalInk.IsDark(ink) ? Colors.White : Colors.Black;
            if (legibility == MinimalLegibility.Outline)
            {
                foreach (var (dx, dy) in new[] { (-.8, 0d), (.8, 0d), (0d, -.8), (0d, .8), (-.6, -.6), (.6, .6), (-.6, .6), (.6, -.6) })
                    Text(dc, verse, font, 11, halo, card.X + 4 + dx, card.Y + 7 + dy, card.Width - 8, bold: true, alignment: TextAlignment.Center);
            }
            else if (legibility == MinimalLegibility.Shadow)
            {
                foreach (var (dx, dy, a) in new[] { (0d, 1.6, (byte)0x70), (0d, 1d, (byte)0x60), (.8, 1.2, (byte)0x40), (-.8, 1.2, (byte)0x40) })
                    Text(dc, verse, font, 11, halo, card.X + 4 + dx, card.Y + 7 + dy, card.Width - 8, bold: true, opacity: a, alignment: TextAlignment.Center);
            }
            Text(dc, verse, font, 11, ink, card.X + 4, card.Y + 7, card.Width - 8, bold: true, alignment: TextAlignment.Center);
            dc.Pop();
        });
    }

    /// <summary>How many verses show: the current one bright, the others quieter, the rest gone.</summary>
    public static ImageSource MinimalLines(MinimalLines lines, Color color) =>
        Diagram((dc, bounds) =>
        {
            var (before, after) = lines switch
            {
                Models.MinimalLines.Current => (0, 0),
                Models.MinimalLines.Next => (0, 1),
                Models.MinimalLines.Five => (2, 2),
                _ => (1, 1),
            };
            var count = before + after + 1;
            var step = count >= 5 ? 7.5 : 9.5;
            var top = (bounds.Height - ((count - 1) * step)) / 2 - 1.5;
            for (var i = 0; i < count; i++)
            {
                var distance = i - before;
                Line(dc, 14, top + (i * step), bounds.Width - 28, distance == 0 ? .9 : .66, TextAlignment.Center, color,
                    distance == 0 ? (byte)0xFF : Math.Abs(distance) == 1 ? (byte)0x80 : (byte)0x48, distance == 0 ? 4 : 3);
            }
        });

    private static void Transport(DrawingContext dc, double x, double y, ControlsLook look, Color color, bool playing)
    {
        var classic = look == ControlsLook.Classic;
        Glyph(dc, Previous, x - 19, y, color, outline: classic);
        Glyph(dc, Next, x + 19, y, color, outline: classic);
        if (classic)
            dc.DrawEllipse(Ink(color), null, new Point(x, y), 8.5, 8.5);
        Glyph(dc, playing ? Pause : Play, x, y, classic ? LyricCardRenderer.TextOn(color) : color);
    }

    private static void Glyph(DrawingContext dc, Geometry geometry, double x, double y, Color color, bool outline = false)
    {
        var brush = Ink(color);
        dc.PushTransform(new TranslateTransform(x, y));
        dc.DrawGeometry(outline ? null : brush, outline ? new Pen(brush, 1) : null, geometry);
        dc.Pop();
    }

    private static byte LineOpacity(LyricsHighlight highlight, int distance) => distance == 0 ? (byte)0xFF
        : highlight == LyricsHighlight.Progressive && Math.Abs(distance) == 1 ? (byte)0xA0 : (byte)0x55;

    private static void Line(DrawingContext dc, double x, double y, double width, double length, TextAlignment alignment,
        Color color, byte opacity, double height)
    {
        var size = width * length;
        var left = alignment == TextAlignment.Left ? x : x + (width - size) / 2;
        dc.DrawRoundedRectangle(Ink(color, opacity), null, new Rect(left, y, size, height), height / 2, height / 2);
    }

    private static void Text(DrawingContext dc, string value, FontFamily font, double size, Color color,
        double x, double y, double width, bool bold = false, byte opacity = 0xFF, TextAlignment alignment = TextAlignment.Left)
    {
        var face = new Typeface(font, FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, Ink(color, opacity), 1)
        {
            MaxTextWidth = width,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment,
        };
        dc.DrawText(text, new Point(x, y));
    }

    private static SolidColorBrush Ink(Color color, byte opacity = 0xFF) =>
        new(Color.FromArgb((byte)(color.A * opacity / 255), color.R, color.G, color.B));

    private static ImageSource Diagram(Action<DrawingContext, Rect> paint) => Draw(96, 46, paint);

    private static ImageSource Draw(double width, double height, Action<DrawingContext, Rect> paint)
    {
        var bounds = new Rect(0, 0, width, height);
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, bounds);
            paint(dc, bounds);
        }
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }
}
