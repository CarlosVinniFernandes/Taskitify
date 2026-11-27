using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Taskitify.Core.Lyrics;

/// <summary>
/// Parses LRC text: "[mm:ss.xx]text", multiple time tags per line, the [offset:±ms] tag and
/// Enhanced LRC word tags ("&lt;mm:ss.xx&gt;"), retained as text-position anchors.
/// </summary>
public static partial class LrcParser
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex TimeTag();

    [GeneratedRegex(@"^\[offset:\s*([+-]?\d+)\s*\]$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetTag();

    [GeneratedRegex(@"<(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?>")]
    private static partial Regex WordTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static IReadOnlyList<LyricsLine> Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc))
            return [];

        var entries = new List<LyricsLine>();
        long offsetMs = 0;

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

            var (text, anchors) = ReadText(line[pos..]);
            foreach (var t in times)
            {
                var repeatShift = t - times[0];
                entries.Add(new LyricsLine(t, text)
                {
                    Anchors = anchors?.Select(anchor => anchor with { Time = anchor.Time + repeatShift }).ToArray(),
                });
            }
        }

        // LRC spec: a positive offset makes lyrics appear sooner.
        var shift = TimeSpan.FromMilliseconds(Math.Clamp(offsetMs, -1_000_000_000_000L, 1_000_000_000_000L));
        return entries
            .Select(e => e with
            {
                Time = e.Time - shift < TimeSpan.Zero ? TimeSpan.Zero : e.Time - shift,
                Anchors = e.Anchors?.Select(anchor => anchor with { Time = anchor.Time - shift }).ToArray(),
            })
            .OrderBy(e => e.Time)
            .ToList();
    }

    private static (string Text, LyricAnchor[]? Anchors) ReadText(string tagged)
    {
        var found = new List<(TimeSpan Time, int Offset)>();
        var invalid = false;
        var removed = 0;
        var raw = WordTag().Replace(tagged, match =>
        {
            if (int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) < 60)
                found.Add((ToTime(match), match.Index - removed));
            else
                invalid = true;
            removed += match.Length;
            return string.Empty;
        });
        if (invalid || found.Count == 0)
            return (Whitespace().Replace(raw, " ").Trim(), null);

        var text = new StringBuilder(raw.Length);
        var anchors = new LyricAnchor[found.Count];
        var cursor = 0;
        var pendingSpace = false;
        for (var i = 0; i < raw.Length; i++)
        {
            if (char.IsWhiteSpace(raw[i]))
            {
                pendingSpace = text.Length > 0;
                continue;
            }
            if (pendingSpace)
            {
                text.Append(' ');
                pendingSpace = false;
            }
            while (cursor < found.Count && found[cursor].Offset <= i)
            {
                anchors[cursor] = new LyricAnchor(found[cursor].Time, text.Length);
                cursor++;
            }
            text.Append(raw[i]);
        }
        while (cursor < found.Count)
        {
            anchors[cursor] = new LyricAnchor(found[cursor].Time, text.Length);
            cursor++;
        }
        return (text.ToString(), anchors);
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
