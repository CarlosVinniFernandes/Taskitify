using System.Windows;
using System.Windows.Media;

namespace Taskitify.Views;

/// <summary>
/// An instrumental break, drawn as three dots that light up one after another across the break:
/// each rises a little, grows past its size and settles, brightens and glows as its share of the
/// break goes by; the group breathes while the music plays and pops away just before the voice
/// returns. Pure drawing (no layout), so the line keeps its height and nothing moves around it.
/// </summary>
internal static class InterludeDots
{
    private const int Count = 3;
    private const double Span = 0.925; // the dots fill this much of the break; the rest is the pop
    private const double PulseSeconds = 2.25;
    private const double AppearSeconds = 0.25;
    private const double PopSeconds = 0.35;

    /// <summary>The box the dots are drawn in, for a line of this font size.</summary>
    public static Size BoxFor(double fontSize)
    {
        var d = Dot(fontSize);
        return new Size((Count * d) + ((Count - 1) * d * 0.75) + (d * 1.6), fontSize * 1.3);
    }

    private static double Dot(double fontSize) => Math.Max(4, fontSize * 0.34);

    /// <param name="progress">How much of the break has gone by (0 to 1); null: not the current line (resting dots).</param>
    /// <param name="elapsed">Seconds since the break began (the group's entrance and breathing).</param>
    /// <param name="remaining">Seconds until the voice returns (the pop); null when unknown.</param>
    public static void Draw(DrawingContext dc, double fontSize, Color color, double? progress, double elapsed, double? remaining, bool playing)
    {
        var box = BoxFor(fontSize);
        var d = Dot(fontSize);
        var gap = d * 0.75;
        var middle = box.Height / 2;

        // The whole group: in with a small overshoot, breathing while it plays, a pop at the end.
        double groupScale = 1, groupOpacity = 1;
        if (progress is not null)
        {
            var appear = Math.Clamp(elapsed / AppearSeconds, 0, 1);
            groupScale = Curve(appear, (0, 0.6), (0.7, 1.05), (1, 1));
            groupOpacity = appear;
            if (playing)
                groupScale *= 1 + (0.05 * Math.Sin(2 * Math.PI * elapsed / PulseSeconds));
            if (remaining is { } left && left < PopSeconds)
            {
                var r = Math.Clamp(left / PopSeconds, 0, 1);
                groupScale *= r > 0.35 ? 1 + (0.15 * (1 - r) / 0.65) : 1.15 * (r / 0.35);
                groupOpacity *= Math.Min(1, r / 0.35);
            }
        }
        if (groupScale <= 0.001 || groupOpacity <= 0.001)
            return;

        var centre = new Point(box.Width / 2, middle);
        dc.PushTransform(new ScaleTransform(groupScale, groupScale, centre.X, centre.Y));
        dc.PushOpacity(groupOpacity);
        var step = Span / Count;
        var x = (box.Width - ((Count * d) + ((Count - 1) * gap))) / 2 + (d / 2);
        for (var i = 0; i < Count; i++, x += d + gap)
        {
            var t = progress is { } p ? Math.Clamp((p - (i * step)) / step, 0, 1) : 0;
            var g = progress is { } q ? Math.Clamp((q - (i * step)) / (1 - (i * step)), 0, 1) : 0;
            var scale = Curve(t, (0, 0.75), (0.7, 1.05), (1, 1));
            var rise = Curve(t, (0, 0.125), (0.9, -0.2), (1, 0)) * d;
            var opacity = Curve(t, (0, 0.35), (0.6, 1), (1, 1));
            var glow = Curve(g, (0, 0), (0.6, 1), (1, 1));
            var at = new Point(x, middle + rise);
            if (glow > 0.01)
            {
                var halo = new RadialGradientBrush(Color.FromArgb((byte)(150 * glow), color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B));
                var radius = d * (0.6 + (0.55 * glow));
                dc.DrawEllipse(halo, null, at, radius, radius);
            }
            var fill = new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), color.R, color.G, color.B));
            dc.DrawEllipse(fill, null, at, d / 2 * scale, d / 2 * scale);
        }
        dc.Pop();
        dc.Pop();
    }

    /// <summary>A smooth curve through the given (time, value) points, time from 0 to 1.</summary>
    internal static double Curve(double t, params (double Time, double Value)[] points)
    {
        if (t <= points[0].Time)
            return points[0].Value;
        for (var i = 1; i < points.Length; i++)
        {
            if (t <= points[i].Time)
            {
                var (t0, v0) = points[i - 1];
                var (t1, v1) = points[i];
                var u = (t - t0) / (t1 - t0);
                u = u * u * (3 - (2 * u)); // smoothstep: no corner at the points
                return v0 + ((v1 - v0) * u);
            }
        }
        return points[^1].Value;
    }
}
