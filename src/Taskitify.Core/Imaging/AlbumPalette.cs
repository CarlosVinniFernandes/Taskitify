namespace Taskitify.Core.Imaging;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>WCAG relative luminance (0 = black, 1 = white).</summary>
    public double Luminance
    {
        get
        {
            static double Lin(byte c)
            {
                var s = c / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return (0.2126 * Lin(R)) + (0.7152 * Lin(G)) + (0.0722 * Lin(B));
        }
    }

    /// <summary>WCAG contrast ratio against white text.</summary>
    public double ContrastWithWhite => 1.05 / (Luminance + 0.05);
}

/// <summary>
/// Builds the two colours of a Spotify-like background gradient from album art: a dark tone
/// of the dominant colour on the left and a richer tone of the most vibrant colour on the
/// right. Both are kept dark enough for white text.
/// </summary>
public static class AlbumPalette
{
    public static readonly (Rgb Start, Rgb End) Fallback = (new Rgb(0x1E, 0x1E, 0x24), new Rgb(0x34, 0x34, 0x40));

    private const double StartMaxLightness = 0.16;
    private const double EndMaxLightness = 0.30;
    private const double MinContrast = 4.5;

    /// <param name="bgra">32-bit BGRA pixels (WPF Bgra32 / Pbgra32 layout).</param>
    public static (Rgb Start, Rgb End) Extract(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        // 4 bits per channel → 4096 buckets; plenty for a 64×64 thumbnail.
        var counts = new int[4096];
        var sums = new long[4096 * 3];
        var total = 0;

        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                var i = row + (x * 4);
                if (i + 3 >= bgra.Length)
                    break;
                if (bgra[i + 3] < 128)
                    continue;
                byte b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
                var bin = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
                counts[bin]++;
                sums[bin * 3] += r;
                sums[(bin * 3) + 1] += g;
                sums[(bin * 3) + 2] += b;
                total++;
            }
        }

        if (total == 0)
            return Fallback;

        int dominant = -1, vibrant = -1;
        double vibrantScore = 0;
        for (var bin = 0; bin < counts.Length; bin++)
        {
            var n = counts[bin];
            if (n == 0)
                continue;
            if (dominant < 0 || n > counts[dominant])
                dominant = bin;

            var c = Average(sums, bin, n);
            var (_, s, l) = ToHsl(c);
            // Colourful, neither near-black nor near-white, and not a single stray pixel.
            if (l < 0.12 || l > 0.9 || n < total / 200)
                continue;
            var score = n * Math.Pow(0.05 + s, 2.5);
            if (score > vibrantScore)
            {
                vibrantScore = score;
                vibrant = bin;
            }
        }

        var baseColor = Average(sums, dominant, counts[dominant]);
        var accentColor = vibrant >= 0 ? Average(sums, vibrant, counts[vibrant]) : baseColor;

        var start = Tone(baseColor, StartMaxLightness, minLightness: 0.07);
        var end = Tone(accentColor, EndMaxLightness, minLightness: 0.14);
        return (start, end);
    }

    /// <summary>Clamp lightness (and tame saturation) so white text stays readable.</summary>
    internal static Rgb Tone(Rgb c, double maxLightness, double minLightness)
    {
        var (h, s, l) = ToHsl(c);
        l = Math.Clamp(l, minLightness, maxLightness);
        s = Math.Min(s, 0.85);
        var result = FromHsl(h, s, l);
        while (result.ContrastWithWhite < MinContrast && l > 0.02)
        {
            l -= 0.02;
            result = FromHsl(h, s, l);
        }
        return result;
    }

    private static Rgb Average(long[] sums, int bin, int n) =>
        new((byte)(sums[bin * 3] / n), (byte)(sums[(bin * 3) + 1] / n), (byte)(sums[(bin * 3) + 2] / n));

    internal static (double H, double S, double L) ToHsl(Rgb c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max - min < 1e-9)
            return (0, 0, l);
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (max == r)
            h = ((g - b) / d) + (g < b ? 6 : 0);
        else if (max == g)
            h = ((b - r) / d) + 2;
        else
            h = ((r - g) / d) + 4;
        return (h / 6, s, l);
    }

    internal static Rgb FromHsl(double h, double s, double l)
    {
        if (s < 1e-9)
        {
            var v = (byte)Math.Round(l * 255);
            return new Rgb(v, v, v);
        }
        var q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
        var p = (2 * l) - q;
        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + ((q - p) * 6 * t);
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + ((q - p) * ((2.0 / 3) - t) * 6);
            return p;
        }
        return new Rgb(
            (byte)Math.Round(Hue(p, q, h + (1.0 / 3)) * 255),
            (byte)Math.Round(Hue(p, q, h) * 255),
            (byte)Math.Round(Hue(p, q, h - (1.0 / 3)) * 255));
    }
}
