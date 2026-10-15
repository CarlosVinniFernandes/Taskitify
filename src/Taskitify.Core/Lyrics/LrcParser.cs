using System.Globalization;
using System.Text.RegularExpressions;

namespace Taskitify.Core.Lyrics;

/// <summary>
/// Parses LRC text: "[mm:ss.xx]text", multiple time tags per line, the [offset:±ms] tag and
/// enhanced-LRC word tags ("&lt;mm:ss.xx&gt;"), which are stripped.
/// </summary>
public static partial class LrcParser
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex TimeTag();

    [GeneratedRegex(@"^\[offset:\s*([+-]?\d+)\s*\]$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetTag();

    [GeneratedRegex(@"<\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?>")]
    private static partial Regex WordTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static IReadOnlyList<LyricsLine> Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc))
            return [];

        var entries = new List<(TimeSpan Time, int Order, string Text)>();
        long offsetMs = 0;
        var order = 0;

        foreach (var raw in lrc.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            var offset = OffsetTag().Match(line);
            if (offset.Success)
            {
                long.TryParse(offset.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out offsetMs);
                continue;
            }

            var times = new List<TimeSpan>();
            var pos = 0;
            while (pos < line.Length)
            {
                var m = TimeTag().Match(line, pos);
                if (!m.Success || m.Index != pos)
                    break;
                times.Add(ToTime(m));
                pos = m.Index + m.Length;
                while (pos < line.Length && char.IsWhiteSpace(line[pos]))
                    pos++;
            }

            // Lines without a leading time tag are metadata ([ar:], [ti:]...) or junk.
            if (times.Count == 0)
                continue;

            var text = Whitespace().Replace(WordTag().Replace(line[pos..], string.Empty), " ").Trim();
            foreach (var t in times)
                entries.Add((t, order++, text));
        }

        // LRC spec: a positive offset makes lyrics appear sooner.
        var shift = TimeSpan.FromMilliseconds(offsetMs);
        return entries
            .Select(e => e with { Time = e.Time - shift < TimeSpan.Zero ? TimeSpan.Zero : e.Time - shift })
            .OrderBy(e => e.Time)
            .ThenBy(e => e.Order)
            .Select(e => new LyricsLine(e.Time, e.Text))
            .ToList();
    }

    private static TimeSpan ToTime(Match m)
    {
        var minutes = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var ms = 0;
        if (m.Groups[3].Success)
        {
            var frac = m.Groups[3].Value;
            var value = int.Parse(frac, CultureInfo.InvariantCulture);
            ms = frac.Length switch
            {
                1 => value * 100,
                2 => value * 10,
                _ => value,
            };
        }
        return new TimeSpan(0, 0, minutes, seconds, ms);
    }
}
