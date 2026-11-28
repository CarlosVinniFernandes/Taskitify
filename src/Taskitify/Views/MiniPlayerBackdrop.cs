using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Taskitify.Core.Imaging;
using Taskitify.Models;

namespace Taskitify.Views;

/// <summary>
/// The mini player's atmospheric backgrounds, made from the album's own colours. Each one is a
/// small static image (a few hundred pixels, all of it soft gradients and blur, so it scales up
/// cleanly), drawn once per cover and style and cached; nothing is animated or redrawn while
/// the player is open, and nothing at all while it is closed.
/// </summary>
/// <summary>How strong a background is drawn; the defaults are the backgrounds as they always were.</summary>
public readonly record struct BackdropTuning(double Effect, double Dim, double Blur)
{
    public static BackdropTuning Default { get; } = new(1, 0, 1);

    public static BackdropTuning From(MiniPlayerStyle look) =>
        new(MiniPlayerStyle.ClampEffect(look.BackgroundEffect), MiniPlayerStyle.ClampDim(look.BackgroundDim), MiniPlayerStyle.ClampBlur(look.BackgroundBlur));
}

public static class MiniPlayerBackdrop
{
    private const int CacheSize = 8;
    private static readonly LinkedList<(object Key, BitmapSource Image)> Cache = new();

    /// <summary>
    /// The brightest parts (85th percentile) are held to this relative luminance: white text on
    /// them stays at about 4:1, and the rest is darker still.
    /// </summary>
    private const double MaxLuminance = 0.2;

    public static bool IsAtmospheric(MiniPlayerBackground style) =>
        style is MiniPlayerBackground.Aurora or MiniPlayerBackground.Atmosphere or MiniPlayerBackground.Velvet or MiniPlayerBackground.BlurredCover;

    internal static MiniPlayerBackground ResolveStyle(MiniPlayerBackground style, PlayerSurface surface) =>
        surface == PlayerSurface.Player && style == MiniPlayerBackground.Default ? MiniPlayerBackground.Aurora : style;

    internal static Brush AlbumBrush(BitmapSource? cover, MiniPlayerBackground style, (Color Start, Color End) palette,
        BackdropTuning tuning, int width, int height)
    {
        Brush brush;
        if (IsAtmospheric(style))
        {
            var image = Render(cover, style, width, height, tuning);
            brush = new ImageBrush(image) { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        }
        else
        {
            brush = new LinearGradientBrush(Dimmed(palette.End, tuning.Dim), Dimmed(palette.Start, tuning.Dim),
                new Point(0, 0), new Point(1, 1));
        }
        brush.Freeze();
        return brush;
    }

    /// <summary>The background for this cover, <paramref name="width"/> × <paramref name="height"/> pixels (small: it is stretched).</summary>
    public static BitmapSource Render(BitmapSource? cover, MiniPlayerBackground style, int width, int height, BackdropTuning? tuning = null)
    {
        var t = tuning ?? BackdropTuning.Default;
        var key = (cover, style, width, height, t);
        foreach (var entry in Cache)
        {
            if (entry.Key.Equals(key))
                return entry.Image;
        }
        var swatches = Swatches(cover);
        var image = style switch
        {
            MiniPlayerBackground.Atmosphere => Atmosphere(swatches, width, height, t),
            MiniPlayerBackground.Velvet => Velvet(swatches, width, height, t),
            MiniPlayerBackground.BlurredCover when cover is not null => BlurredCover(cover, width, height, t),
            _ => Aurora(swatches, width, height, t),
        };
        Cache.AddFirst((key, image));
        if (Cache.Count > CacheSize)
            Cache.RemoveLast();
        return image;
    }

    // ---- the styles ----

    /// <summary>Several wide clouds of the album's colours over a dark tone of its main one.</summary>
    private static BitmapSource Aurora(IReadOnlyList<(Rgb Color, double Share)> swatches, int w, int h, BackdropTuning t)
    {
        // The colours that say the most about the cover: present and colourful, and distinct.
        var ranked = swatches.OrderByDescending(s => s.Share * (0.25 + AlbumPalette.ToHsl(s.Color).S)).Select(s => s.Color).ToList();
        var picked = new List<Rgb>();
        foreach (var c in ranked)
        {
            if (picked.All(p => AlbumPalette.Distance(p, c) > 90))
                picked.Add(c);
        }
        var colors = Variants(picked.Select(c => (c, 1.0)).ToList(), 4);
        var baseColor = Shade(swatches[0].Color, maxS: 0.7, minL: 0.05, maxL: 0.12);
        (double X, double Y, double R)[] spots = [(0.1, 0.12, 0.62), (0.95, 0.3, 0.58), (0.15, 0.88, 0.6), (0.85, 0.95, 0.52)];
        return Compose(w, h, baseColor, dc =>
        {
            for (var i = 0; i < colors.Count; i++)
            {
                var (_, sat, _) = AlbumPalette.ToHsl(colors[i]);
                // Colourful ones a touch richer; greys stay grey.
                var c = Shade(colors[i], maxS: 0.9, minL: 0.34, maxL: 0.56, saturation: sat > 0.25 ? 1.15 : 1);
                Blob(dc, w, h, spots[i], c, t.Effect);
            }
        }, blur: 0.13, dim: t.Dim);
    }

    /// <summary>The main colour, lit softly in two places by its neighbours: calm, low contrast.</summary>
    private static BitmapSource Atmosphere(IReadOnlyList<(Rgb Color, double Share)> swatches, int w, int h, BackdropTuning t)
    {
        var main = swatches[0].Color;
        var second = swatches.Count > 1 && AlbumPalette.Distance(swatches[1].Color, main) < 200 ? swatches[1].Color : Hue(main, 0.03);
        var baseColor = Shade(main, maxS: 0.6, minL: 0.14, maxL: 0.22);
        return Compose(w, h, baseColor, dc =>
        {
            Blob(dc, w, h, (0.2, 0.05, 1.0), Shade(main, maxS: 0.65, minL: 0.28, maxL: 0.36), 0.7 * t.Effect);
            Blob(dc, w, h, (0.95, 0.95, 0.9), Shade(second, maxS: 0.5, minL: 0.22, maxL: 0.3), 0.6 * t.Effect);
        }, blur: 0.25, dim: t.Dim);
    }

    /// <summary>Muted, deep colours in wide sweeps, a vignette and a fine grain: matte.</summary>
    private static BitmapSource Velvet(IReadOnlyList<(Rgb Color, double Share)> swatches, int w, int h, BackdropTuning t)
    {
        var colors = Variants(swatches, 2);
        var a = Shade(colors[0], maxS: 0.42, minL: 0.2, maxL: 0.28, saturation: 0.6);
        var b = Shade(colors.Count > 1 ? colors[1] : Hue(colors[0], -0.04), maxS: 0.42, minL: 0.1, maxL: 0.18, saturation: 0.6);
        var image = Compose(w, h, b, dc =>
        {
            dc.DrawRectangle(new LinearGradientBrush(a, b, new Point(0, 0), new Point(1, 1)), null, new Rect(0, 0, w, h));
            Blob(dc, w, h, (0.3, 0.0, 0.9), Color.FromArgb(0xFF, (byte)Math.Min(255, a.R + 18), (byte)Math.Min(255, a.G + 18), (byte)Math.Min(255, a.B + 18)), 0.5 * t.Effect);
            var vignette = new RadialGradientBrush(Colors.Transparent, Color.FromArgb((byte)Math.Min(255, 0x70 * t.Effect), 0, 0, 0)) { RadiusX = 0.75, RadiusY = 0.75 };
            dc.DrawRectangle(vignette, null, new Rect(0, 0, w, h));
        }, blur: 0.12, dim: t.Dim);
        return Grain(image, 5);
    }

    /// <summary>The cover itself, blurred past recognition, a little desaturated and darkened.</summary>
    private static BitmapSource BlurredCover(BitmapSource cover, int w, int h, BackdropTuning t)
    {
        var size = new Size(w, h);
        var radius = Math.Max(w, h) * 0.22 * t.Blur;
        var host = new Grid { Width = w, Height = h, ClipToBounds = true, Background = Brushes.Black };
        host.Children.Add(new Image
        {
            Source = cover,
            Stretch = Stretch.UniformToFill,
            Margin = new Thickness(-radius),
            Effect = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality },
        });
        host.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x22, 0x80, 0x80, 0x80)) });
        var bitmap = RenderElement(host, size);
        return Legible(bitmap, extra: 0.1 + ((1 - 0.1) * t.Dim));
    }

    // ---- building blocks ----

    private static BitmapSource Compose(int w, int h, Color baseColor, Action<DrawingContext> draw, double blur, double dim)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(baseColor), null, new Rect(0, 0, w, h));
            draw(dc);
        }
        var raw = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        raw.Render(visual);
        // One blur pass melts the clouds together (the edges are padded so they do not darken).
        var radius = Math.Max(w, h) * blur;
        var host = new Grid { Width = w, Height = h, ClipToBounds = true, Background = new SolidColorBrush(baseColor) };
        host.Children.Add(new Image
        {
            Source = raw,
            Stretch = Stretch.Fill,
            Margin = new Thickness(-radius),
            Effect = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality },
        });
        // Darkening only ever adds to what legibility needs: the text never gets harder to read.
        return Legible(RenderElement(host, new Size(w, h)), extra: dim);
    }

    private static void Blob(DrawingContext dc, int w, int h, (double X, double Y, double R) spot, Color color, double strength)
    {
        var brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)Math.Min(255, 255 * strength), color.R, color.G, color.B), 0),
                new GradientStop(Color.FromArgb((byte)Math.Min(255, 130 * strength), color.R, color.G, color.B), 0.45),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
            },
        };
        var r = spot.R * Math.Max(w, h);
        dc.DrawEllipse(brush, null, new Point(spot.X * w, spot.Y * h), r, r * 0.8);
    }

    private static BitmapSource RenderElement(FrameworkElement element, Size size)
    {
        element.Measure(size);
        element.Arrange(new Rect(size));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>A gradient's colour darkened by <paramref name="dim"/> (0: as is).</summary>
    public static Color Dimmed(Color c, double dim) =>
        Color.FromRgb((byte)(c.R * (1 - dim)), (byte)(c.G * (1 - dim)), (byte)(c.B * (1 - dim)));

    /// <summary>Darkened just enough (if at all) that white lyrics and controls read well on it.</summary>
    private static BitmapSource Legible(BitmapSource image, double extra)
    {
        var pixels = Pixels(image, out var stride);
        var levels = new List<double>(pixels.Length / 16);
        for (var i = 0; i < pixels.Length; i += 16)
            levels.Add(new Rgb(pixels[i + 2], pixels[i + 1], pixels[i]).Luminance);
        levels.Sort();
        var bright = levels.Count == 0 ? 0 : levels[(int)(levels.Count * 0.85)];
        var keep = Math.Min(1, MaxLuminance / Math.Max(bright, 1e-6)) * (1 - extra);
        if (keep >= 0.999)
            return Frozen(image);
        // Relative luminance is roughly the square of the stored value, hence the square root.
        var factor = Math.Sqrt(keep);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)(pixels[i] * factor);
            pixels[i + 1] = (byte)(pixels[i + 1] * factor);
            pixels[i + 2] = (byte)(pixels[i + 2] * factor);
        }
        return FromPixels(pixels, image.PixelWidth, image.PixelHeight, stride);
    }

    /// <summary>A fixed, fine grain (same pattern every time): matte instead of glossy.</summary>
    private static BitmapSource Grain(BitmapSource image, int amount)
    {
        var pixels = Pixels(image, out var stride);
        var random = new Random(7);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var d = random.Next(-amount, amount + 1);
            pixels[i] = (byte)Math.Clamp(pixels[i] + d, 0, 255);
            pixels[i + 1] = (byte)Math.Clamp(pixels[i + 1] + d, 0, 255);
            pixels[i + 2] = (byte)Math.Clamp(pixels[i + 2] + d, 0, 255);
        }
        return FromPixels(pixels, image.PixelWidth, image.PixelHeight, stride);
    }

    private static byte[] Pixels(BitmapSource image, out int stride)
    {
        var bgra = image.Format == PixelFormats.Pbgra32 || image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Pbgra32, null, 0);
        stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static BitmapSource FromPixels(byte[] pixels, int w, int h, int stride)
    {
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource Frozen(BitmapSource image)
    {
        if (!image.IsFrozen)
            image.Freeze();
        return image;
    }

    /// <summary>The cover's main colours, from a small copy of it.</summary>
    internal static IReadOnlyList<(Rgb Color, double Share)> Swatches(BitmapSource? cover)
    {
        if (cover is null)
            return [(new Rgb(0x2A, 0x2E, 0x3A), 0.6), (new Rgb(0x44, 0x3A, 0x5A), 0.4)];
        var scale = 48.0 / Math.Max(cover.PixelWidth, cover.PixelHeight);
        BitmapSource small = scale < 1 ? new TransformedBitmap(cover, new ScaleTransform(scale, scale)) : cover;
        var pixels = Pixels(small, out var stride);
        return AlbumPalette.Swatches(pixels, small.PixelWidth, small.PixelHeight, stride);
    }

    /// <summary>
    /// Up to <paramref name="count"/> colours to paint with: the album's, and for a cover with
    /// fewer, lighter and darker tones of the same ones (never a hue it does not have).
    /// </summary>
    private static List<Rgb> Variants(IReadOnlyList<(Rgb Color, double Share)> swatches, int count)
    {
        var colors = swatches.Select(s => s.Color).Take(count).ToList();
        var i = 0;
        while (colors.Count < count)
        {
            var (h, s, l) = AlbumPalette.ToHsl(swatches[i % swatches.Count].Color);
            colors.Add(AlbumPalette.FromHsl(h, s, Math.Clamp(l + (colors.Count % 2 == 0 ? 0.12 : -0.1), 0.05, 0.9)));
            i++;
        }
        return colors;
    }

    /// <summary>The colour with its lightness held in a range and saturation capped (and optionally scaled).</summary>
    private static Color Shade(Rgb c, double maxS, double minL, double maxL, double saturation = 1)
    {
        var (h, s, l) = AlbumPalette.ToHsl(c);
        var result = AlbumPalette.FromHsl(h, Math.Min(s * saturation, maxS), Math.Clamp(l, minL, maxL));
        return Color.FromRgb(result.R, result.G, result.B);
    }

    private static Rgb Hue(Rgb c, double shift)
    {
        var (h, s, l) = AlbumPalette.ToHsl(c);
        return AlbumPalette.FromHsl((h + shift + 1) % 1, s, l);
    }
}
