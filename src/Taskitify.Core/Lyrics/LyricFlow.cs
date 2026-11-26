using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Taskitify.Core.Lyrics;

public enum LyricFlowMode
{
    Off,
    Linear,
    Adaptive,
    Automatic,
}

/// <summary>A fraction of rendered distance, or a (possibly fractional) UTF-16 text offset.</summary>
public readonly record struct LyricFill(double Value, bool ByText, bool IsComplete)
{
    public static LyricFill Full => new(1, false, true);
}

/// <summary>Immutable timing prepared per song. Sampling uses only the caller's playback position.</summary>
public sealed partial class LyricFlow
{
    private readonly Phrase[] _phrases;

    private readonly record struct Point(double Seconds, int Offset);
    private readonly record struct Phrase(TimeSpan Start, double LinearDuration, Point[] Estimated,
        Point[]? Actual, bool CanEstimate);

    [GeneratedRegex(@"\S+")]
    private static partial Regex Tokens();

    public LyricFlow(LyricsTimeline timeline, TimeSpan? duration)
    {
        var lines = timeline.Lines;
        _phrases = new Phrase[lines.Count];
        var words = lines.Select(line => GetWords(line.Text)).ToArray();
        var units = words.Select(parts => parts.Sum(word => word.Weight)).ToArray();
        var windows = new double[lines.Count];
        var rate = new double[lines.Count];
        var next = duration ?? TimeSpan.Zero;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var start = lines[i].Time;
            // Earlier copies at the same timestamp never become active.
            windows[i] = next > start ? (next - start).TotalSeconds : 0;
            var candidate = units[i] > 0 ? windows[i] / units[i] : 0;
            if (candidate is >= 0.13 and <= 0.95 && windows[i] <= 10 && i + 1 < lines.Count)
                rate[i] = candidate;
            next = start;
        }
        var typical = Median(rate);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            // Leave the line's own duration out so a silence cannot establish its own expected tempo.
            var first = Math.Max(0, i - 4);
            var last = Math.Min(rate.Length, i + 5);
            var nearby = rate[first..last];
            nearby[i - first] = 0;
            var secondsPerUnit = Median(nearby) ?? typical ?? 0.42;
            var expected = Math.Clamp(secondsPerUnit * units[i] + 0.25, 0.65, 12);
            var window = windows[i];
            var linear = window > 0 ? window : expected;
            var adaptive = window <= 0 ? expected : window;
            if (adaptive > Math.Max(4, expected * 2.2))
                adaptive = expected;

            _phrases[i] = new Phrase(line.Time, linear, Estimate(words[i], units[i], adaptive),
                ReadAnchors(line, window), units[i] > 0 && (window > 0 || typical is not null));
        }
    }

    public LyricFill At(int index, TimeSpan position, LyricFlowMode mode)
    {
        if ((uint)index >= (uint)_phrases.Length || mode == LyricFlowMode.Off)
            return LyricFill.Full;

        var phrase = _phrases[index];
        var elapsed = (position - phrase.Start).TotalSeconds;
        if (phrase.Estimated.Length == 0)
            return LyricFill.Full;
        if (mode == LyricFlowMode.Automatic && phrase.Actual is { } actual)
            return Interpolate(actual, elapsed);
        if (mode == LyricFlowMode.Automatic && !phrase.CanEstimate)
            return LyricFill.Full;
        if (mode == LyricFlowMode.Linear)
        {
            if (elapsed >= phrase.LinearDuration)
                return LyricFill.Full;
            return new LyricFill(Math.Clamp(elapsed / phrase.LinearDuration, 0, 1), false, false);
        }
        return Interpolate(phrase.Estimated, elapsed);
    }

    private static LyricFill Interpolate(Point[] points, double elapsed)
    {
        if (elapsed >= points[^1].Seconds)
            return LyricFill.Full;
        if (elapsed <= points[0].Seconds)
            return new LyricFill(points[0].Offset, true, false);

        var lo = 0;
        var hi = points.Length - 1;
        while (hi - lo > 1)
        {
            var middle = (hi + lo) / 2;
            if (points[middle].Seconds <= elapsed)
                lo = middle;
            else
                hi = middle;
        }
        var a = points[lo];
        var b = points[hi];
        return new LyricFill(a.Offset + ((b.Offset - a.Offset) * (elapsed - a.Seconds) / (b.Seconds - a.Seconds)), true, false);
    }

    private static Point[]? ReadAnchors(LyricsLine line, double window)
    {
        if (line.Anchors is not { Count: > 0 } anchors)
            return null;
        var points = new List<Point> { new(0, 0) };
        var boundaries = StringInfo.ParseCombiningCharacters(line.Text).ToHashSet();
        boundaries.Add(line.Text.Length);
        foreach (var anchor in anchors)
        {
            var seconds = (anchor.Time - line.Time).TotalSeconds;
            if (seconds < 0 || (window > 0 && seconds > window + 0.03) || !boundaries.Contains(anchor.TextOffset))
                return null;
            if (seconds == 0 && anchor.TextOffset == 0 && points.Count == 1)
                continue;
            if (seconds <= points[^1].Seconds || anchor.TextOffset < points[^1].Offset
                || (anchor.TextOffset == points[^1].Offset && anchor.TextOffset != 0))
                return null;
            points.Add(new Point(seconds, anchor.TextOffset));
        }
        if (points.Count < 2)
            return null;
        if (points[^1].Offset < line.Text.Length)
        {
            // Enhanced LRC may omit the closing tag. Estimate only the last span.
            var last = points[^1].Seconds;
            var estimatedTail = Math.Clamp(last * (line.Text.Length - points[^1].Offset) / Math.Max(1, points[^1].Offset), 0.2, 2.0);
            var end = window > 0 ? Math.Min(window, last + estimatedTail) : last + estimatedTail;
            if (end <= last)
                return null;
            points.Add(new Point(end, line.Text.Length));
        }
        return points.ToArray();
    }

    private static Point[] Estimate((int End, double Weight)[] words, double total, double seconds)
    {
        if (words.Length == 0)
            return [];
        var points = new Point[words.Length + 1];
        var elapsed = 0.0;
        for (var i = 0; i < words.Length; i++)
        {
            elapsed += words[i].Weight / total * seconds;
            points[i + 1] = new Point(elapsed, words[i].End);
        }
        return points;
    }

    private static (int End, double Weight)[] GetWords(string text)
    {
        var tokens = Tokens().Matches(text);
        if (tokens.Count == 0 || !text.EnumerateRunes().Any(System.Text.Rune.IsLetterOrDigit))
            return [];
        var result = new (int, double)[tokens.Count];
        for (var i = 0; i < tokens.Count; i++)
        {
            var word = tokens[i].Value;
            var letters = new StringInfo(word).LengthInTextElements;
            var vowelGroups = 0;
            var previousVowel = false;
            foreach (var ch in word.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
                    continue;
                var vowel = "aeiouy".Contains(char.ToLowerInvariant(ch));
                if (vowel && !previousVowel)
                    vowelGroups++;
                previousVowel = vowel;
            }
            var syllables = vowelGroups > 0 ? vowelGroups : Math.Max(1, letters * 0.65);
            var pause = word[^1] switch { ',' or ';' or ':' => 0.25, '.' or '?' or '!' or '…' => 0.42, _ => 0 };
            var end = i + 1 < tokens.Count ? tokens[i + 1].Index : text.Length;
            result[i] = (end, Math.Max(0.8, syllables * 0.78 + letters * 0.1) + pause);
        }
        return result;
    }

    private static double? Median(double[] samples)
    {
        var sorted = samples.Where(sample => sample > 0).Order().ToArray();
        return sorted.Length == 0 ? null : (sorted[sorted.Length / 2] + sorted[(sorted.Length - 1) / 2]) / 2;
    }
}
