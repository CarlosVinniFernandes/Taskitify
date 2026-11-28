using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Taskitify.Core.Lyrics;

/// <summary>
/// Builds a karaoke line's text and anchors from timed pieces (words or syllables). A piece's
/// start is an anchor at its first character. A pause after a piece (the singer stops before
/// the next one) adds an anchor at its end on the space that follows, so the fill holds during
/// the pause instead of creeping through it. The last piece ends the line.
/// </summary>
internal sealed class KaraokeTextBuilder
{
    // A gap at least this long between two pieces is a pause the fill should hold through.
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(150);

    private readonly StringBuilder _text = new();
    private readonly List<LyricAnchor> _anchors = [];
    private TimeSpan? _start;
    private TimeSpan _end;
    private bool _spacePending;
    private (TimeSpan End, int Offset)? _last;

    public bool IsEmpty => _text.Length == 0;

    /// <summary>A separator between words (the next piece starts a new word).</summary>
    public void Space()
    {
        if (_text.Length > 0)
            _spacePending = true;
    }

    public void Add(string piece, TimeSpan begin, TimeSpan end)
    {
        piece = piece.Replace('\n', ' ').Replace('\r', ' ');
        if (piece.Length == 0)
            return;
        // Leading/trailing blanks in the piece itself mark word boundaries too (Lyricsfile keeps them).
        if (char.IsWhiteSpace(piece[0]))
            Space();
        var trailing = char.IsWhiteSpace(piece[^1]);
        piece = piece.Trim();
        if (piece.Length == 0)
            return;
        if (end < begin)
            end = begin;
        if (_spacePending && _text.Length > 0)
        {
            // Pause: hold the last piece's end on the space, then start this one after it.
            if (_last is { } last && begin - last.End >= Pause && last.End > (_anchors.Count > 0 ? _anchors[^1].Time : TimeSpan.Zero))
                _anchors.Add(new LyricAnchor(last.End, _text.Length));
            _text.Append(' ');
        }
        _spacePending = false;
        if (_anchors.Count == 0 || begin > _anchors[^1].Time)
            _anchors.Add(new LyricAnchor(begin, _text.Length));
        _start ??= begin;
        _text.Append(piece);
        _end = end > _end ? end : _end;
        _last = (end, _text.Length);
        if (trailing)
            Space();
    }

    /// <summary>The text, its first time, its last time and its anchors (ending at the last piece's end).</summary>
    public (string Text, TimeSpan Start, TimeSpan End, LyricAnchor[] Anchors)? Build()
    {
        if (_text.Length == 0 || _start is not { } start)
            return null;
        var anchors = new List<LyricAnchor>(_anchors);
        if (_end > anchors[^1].Time)
            anchors.Add(new LyricAnchor(_end, _text.Length));
        return (_text.ToString(), start, _end, [.. anchors]);
    }
}

/// <summary>
/// TTML lyrics as Apple Music and the sources that follow it publish them: &lt;p&gt; lines with
/// timed &lt;span&gt; words or syllables (adjacent spans without a space are one word),
/// backing vocals in a &lt;span ttm:role="x-bg"&gt;, and singers as ttm:agent on each line.
/// </summary>
public static class TtmlParser
{
    private static readonly XNamespace Tt = "http://www.w3.org/ns/ttml";
    private static readonly XNamespace Ttm = "http://www.w3.org/ns/ttml#metadata";

    /// <summary>The lyric, or null when the document is malformed or has no timed lines.</summary>
    public static KaraokeLyrics? Parse(string? xml, string source)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return null;
        }
        var root = doc.Root;
        if (root is null || root.Name.LocalName != "tt")
            return null;
        var ns = root.Name.Namespace;
        var lines = new List<LyricsLine>();
        var anyTimedSpan = false;
        var syllables = false;
        string? firstAgent = null;
        foreach (var p in root.Descendants(ns + "p"))
        {
            if (ParseTime(p.Attribute("begin")?.Value) is not { } lineBegin)
                continue;
            var lineEnd = ParseTime(p.Attribute("end")?.Value);
            var main = new KaraokeTextBuilder();
            var background = new List<LyricsVocal>();
            var timed = false;
            foreach (var node in p.Nodes())
            {
                switch (node)
                {
                    case XText text when string.IsNullOrWhiteSpace(text.Value):
                        main.Space();
                        break;
                    case XText text:
                        // Untimed words in a timed line: they belong to the line's start.
                        main.Add(text.Value, lineBegin, lineBegin);
                        break;
                    case XElement span when IsBackground(span):
                        if (ReadVocal(span, ref syllables) is { } vocal)
                            background.Add(vocal);
                        break;
                    case XElement span when span.Name.LocalName == "span":
                        timed |= AddSpan(main, span, lineBegin, ref syllables);
                        break;
                }
            }
            var built = main.Build();
            if (built is null)
                continue;
            anyTimedSpan |= timed;
            var agent = p.Attribute(Ttm + "agent")?.Value;
            firstAgent ??= agent;
            lines.Add(new LyricsLine(lineBegin, built.Value.Text)
            {
                // Line-timed TTML has no word anchors: the usual estimated fill applies.
                Anchors = timed ? built.Value.Anchors : null,
                End = lineEnd ?? (timed ? built.Value.End : null),
                Singer = agent is not null && firstAgent is not null && agent != firstAgent && !IsGroup(agent) ? 1 : 0,
                Background = background.Count > 0 ? background : null,
            });
        }
        if (lines.Count == 0)
            return null;
        var timing = root.Attributes().FirstOrDefault(a => a.Name.LocalName == "timing")?.Value;
        var sync = !anyTimedSpan || string.Equals(timing, "Line", StringComparison.OrdinalIgnoreCase) ? LyricsSyncLevel.Line
            : syllables ? LyricsSyncLevel.Syllable : LyricsSyncLevel.Word;
        if (sync == LyricsSyncLevel.Line)
            lines = [.. lines.Select(l => l with { Anchors = null })];
        return new KaraokeLyrics(lines.OrderBy(l => l.Time).ToArray(), sync, source);
    }

    private static bool IsBackground(XElement span) =>
        span.Name.LocalName == "span" && span.Attribute(Ttm + "role")?.Value == "x-bg";

    // Apple's "v1000" and similar mark everyone singing together, not a side.
    private static bool IsGroup(string agent) => agent.Length >= 5 && agent.StartsWith('v') && agent[1..].All(char.IsAsciiDigit) && agent.Length > 4;

    /// <returns>Whether the span carried its own timing.</returns>
    private static bool AddSpan(KaraokeTextBuilder builder, XElement span, TimeSpan lineBegin, ref bool syllables)
    {
        var begin = ParseTime(span.Attribute("begin")?.Value);
        var end = ParseTime(span.Attribute("end")?.Value);
        // A timed span right after another, with no space between them: part of one word.
        if (begin is not null && span.PreviousNode is XElement { Name.LocalName: "span" } previous
            && previous.Attribute("begin") is not null && !IsBackground(previous))
            syllables = true;
        builder.Add(span.Value, begin ?? lineBegin, end ?? begin ?? lineBegin);
        if (span.NextNode is XText { Value: var after } && after.Length > 0 && string.IsNullOrWhiteSpace(after))
            builder.Space();
        return begin is not null;
    }

    private static LyricsVocal? ReadVocal(XElement container, ref bool syllables)
    {
        var builder = new KaraokeTextBuilder();
        var begin = ParseTime(container.Attribute("begin")?.Value) ?? TimeSpan.Zero;
        foreach (var node in container.Nodes())
        {
            if (node is XText text && string.IsNullOrWhiteSpace(text.Value))
                builder.Space();
            else if (node is XElement span && span.Name.LocalName == "span")
                AddSpan(builder, span, begin, ref syllables);
            else if (node is XText words)
                builder.Add(words.Value, begin, begin);
        }
        if (builder.Build() is not { } built)
            return null;
        var start = ParseTime(container.Attribute("begin")?.Value) ?? built.Start;
        var end = ParseTime(container.Attribute("end")?.Value) ?? built.End;
        return new LyricsVocal(built.Text, start, end > start ? end : built.End, built.Anchors);
    }

    /// <summary>TTML clock values: "12.345", "1:02.345", "00:01:02.345", "12.3s", "1500ms".</summary>
    internal static TimeSpan? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        try
        {
            if (value.EndsWith("ms", StringComparison.Ordinal))
                return TimeSpan.FromMilliseconds(double.Parse(value[..^2], CultureInfo.InvariantCulture));
            if (value.EndsWith('s'))
                return TimeSpan.FromSeconds(double.Parse(value[..^1], CultureInfo.InvariantCulture));
            var parts = value.Split(':');
            double seconds = 0;
            foreach (var part in parts)
                seconds = (seconds * 60) + double.Parse(part, CultureInfo.InvariantCulture);
            return seconds is >= 0 and < 360_000 ? TimeSpan.FromSeconds(seconds) : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}

/// <summary>
/// LRCLIB's Lyricsfile (YAML): lines with start_ms/end_ms and, when word-synced, words with their
/// own start_ms/end_ms. Read with a small reader for exactly this schema (no YAML dependency):
/// block lists, "key: value" pairs, plain, single- and double-quoted scalars, folded continuations.
/// </summary>
public static class LyricsfileParser
{
    private sealed class Node
    {
        public Dictionary<string, string> Values { get; } = [];
        public List<Node> Words { get; } = [];
    }

    public static KaraokeLyrics? Parse(string? yaml, string source)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return null;
        var lines = ReadLines(yaml);
        if (lines.Count == 0)
            return null;
        var result = new List<LyricsLine>();
        var anyWords = false;
        foreach (var node in lines)
        {
            if (Ms(node.Values, "start_ms") is not { } start)
                continue;
            var end = Ms(node.Values, "end_ms");
            var text = node.Values.GetValueOrDefault("text")?.Trim() ?? "";
            if (node.Words.Count > 0)
            {
                var builder = new KaraokeTextBuilder();
                var words = node.Words.Where(w => Ms(w.Values, "start_ms") is not null).OrderBy(w => Ms(w.Values, "start_ms")).ToList();
                for (var i = 0; i < words.Count; i++)
                {
                    var wStart = Ms(words[i].Values, "start_ms")!.Value;
                    var wEnd = Ms(words[i].Values, "end_ms")
                        ?? (i + 1 < words.Count ? Ms(words[i + 1].Values, "start_ms") : end) ?? wStart;
                    builder.Add(words[i].Values.GetValueOrDefault("text") ?? "", wStart, wEnd);
                }
                if (builder.Build() is { } built)
                {
                    anyWords = true;
                    result.Add(new LyricsLine(start, built.Text) { Anchors = built.Anchors, End = end ?? built.End });
                    continue;
                }
            }
            result.Add(new LyricsLine(start, text) { End = end });
        }
        if (result.Count == 0)
            return null;
        return new KaraokeLyrics(result.OrderBy(l => l.Time).ToArray(), anyWords ? LyricsSyncLevel.Word : LyricsSyncLevel.Line, source);
    }

    private static TimeSpan? Ms(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var raw) && long.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) && ms >= 0
            ? TimeSpan.FromMilliseconds(ms) : null;

    /// <summary>The items of the top-level "lines:" list (with their "words:" items).</summary>
    private static List<Node> ReadLines(string yaml)
    {
        var lines = new List<Node>();
        var raw = yaml.Replace("\r\n", "\n").Split('\n');
        var inLines = false;
        var lineIndent = -1;
        Node? line = null, word = null;
        var inWords = false;
        var wordIndent = -1;
        (Dictionary<string, string> Values, string Key, int Indent)? last = null;
        foreach (var rawLine in raw)
        {
            if (rawLine.Trim().Length == 0 || rawLine.TrimStart().StartsWith('#'))
                continue;
            var indent = rawLine.Length - rawLine.TrimStart(' ').Length;
            var content = rawLine.Trim();
            if (indent == 0 && !content.StartsWith('-'))
            {
                inLines = content.StartsWith("lines:", StringComparison.Ordinal);
                line = word = null;
                inWords = false;
                last = null;
                continue;
            }
            if (!inLines)
                continue;
            var isItem = content.StartsWith("- ", StringComparison.Ordinal) || content == "-";
            if (isItem && (lineIndent < 0 || indent <= lineIndent))
            {
                lineIndent = indent;
                line = new Node();
                lines.Add(line);
                word = null;
                inWords = false;
                content = content.Length > 1 ? content[2..] : "";
                indent += 2;
            }
            else if (isItem && inWords && line is not null)
            {
                wordIndent = indent;
                word = new Node();
                line.Words.Add(word);
                content = content.Length > 1 ? content[2..] : "";
                indent += 2;
            }
            if (line is null || content.Length == 0)
                continue;
            var colon = KeyEnd(content);
            if (colon < 0)
            {
                // A folded continuation of the last value.
                if (last is { } l && indent > l.Indent)
                    l.Values[l.Key] = (l.Values[l.Key] + " " + Scalar(content)).Trim();
                continue;
            }
            var key = content[..colon].Trim();
            var value = content[(colon + 1)..].Trim();
            if (key == "words" && value.Length == 0)
            {
                inWords = true;
                continue;
            }
            var target = inWords && word is not null && indent > lineIndent + 1 && (wordIndent < 0 || indent > wordIndent) ? word.Values : line.Values;
            if (target == line.Values && word is not null && indent <= lineIndent + 2 && key != "text")
                inWords = key == "words";
            target[key] = Scalar(value);
            last = (target, key, indent);
        }
        return lines;
    }

    /// <summary>Index of the ':' ending a key (outside quotes), or -1.</summary>
    private static int KeyEnd(string content)
    {
        if (content.StartsWith('\'') || content.StartsWith('"'))
            return -1;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == ':' && (i + 1 == content.Length || content[i + 1] == ' '))
                return i;
        }
        return -1;
    }

    private static string Scalar(string value)
    {
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            return value[1..^1].Replace("''", "'");
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            var inner = value[1..^1];
            var sb = new StringBuilder(inner.Length);
            for (var i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '\\' && i + 1 < inner.Length)
                {
                    i++;
                    sb.Append(inner[i] switch { 'n' => ' ', 't' => ' ', '"' => '"', '\\' => '\\', '\'' => '\'', _ => inner[i] });
                }
                else
                    sb.Append(inner[i]);
            }
            return sb.ToString();
        }
        return value is "~" or "null" ? "" : value;
    }
}
