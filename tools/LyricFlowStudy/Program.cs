using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Taskitify.Core.Lyrics;

// Lyric Flow study on JamendoLyrics (word-annotated, CC-licensed songs). Prints only error statistics.
var data = JsonSerializer.Deserialize<List<Song>>(File.ReadAllText(args[0]))!;

if (args.Length > 1 && args[1] == "grid")
{
    var results = new List<(double All, double A, double B, string Name)>();
    var even = data.Where((_, k) => k % 2 == 0).ToList(); var odd = data.Where((_, k) => k % 2 == 1).ToList();
    foreach (var tempo in new[] { .95, .9, .85 })
    foreach (var frac in new[] { .45 })
    foreach (var boost in new[] { 1.8, 2.4, 3.0, 4.0 })
    foreach (var cap in new[] { .2, .3, .4 })
    {
        var o = new Options { Hold = true, HoldCap = cap, FinalBoost = boost, Tempo = tempo, CoreFrac = frac, SilentE = true };
        var n = $"tempo {tempo} core {frac} final {boost} cap {cap}";
        results.Add((Mae(data, o), Mae(even, o), Mae(odd, o), n));
    }
    var b = new Options();
    Console.WriteLine($"baseline: all {Mae(data, b):0} even {Mae(even, b):0} odd {Mae(odd, b):0}");
    foreach (var r in results.OrderBy(r => r.All).Take(8)) Console.WriteLine($"{r.Name,-40} all {r.All:0}  even {r.A:0}  odd {r.B:0}");
    var bestEven = results.MinBy(r => r.A); var bestOdd = results.MinBy(r => r.B);
    Console.WriteLine($"cross-check: best on even ({bestEven.Name}) scores {bestEven.B:0} on odd; best on odd ({bestOdd.Name}) scores {bestOdd.A:0} on even");
    return;
}
Report("app: LyricFlow Adaptive", data, song => AppFlow(song));
var variants = new (string Name, Options O)[]
{
    ("chosen (no silent-e)", new Options { Hold = true, HoldCap = .3, FinalBoost = 2.4, Tempo = .95, CoreFrac = .45 }),
    ("chosen + silent-e", new Options { Hold = true, HoldCap = .3, FinalBoost = 2.4, Tempo = .95, CoreFrac = .45, SilentE = true }),
};
foreach (var (name, o) in variants)
    Report(name, data, song => new PortFlow(song, o));

static double Mae(List<Song> songs, Options o)
{
    var sum = 0.0; var n = 0;
    foreach (var song in songs)
    {
        var f = new PortFlow(song, o);
        for (var i = 0; i < song.lines.Count; i++)
        {
            var line = song.lines[i];
            var offsets = Regex.Matches(line.text, @"\S+").Select(m => (m.Index, End: m.Index + m.Length)).ToList();
            if (offsets.Count != line.words.Count) continue;
            double Reach(double off) { for (var t = line.start; t < line.start + 30; t += 0.01) if (f.At(i, t) >= off - 1e-6) return t; return line.start + 30; }
            for (var w = 0; w < offsets.Count; w++)
            {
                sum += Math.Abs(Reach(offsets[w].Index + .5) - line.words[w][0]) + Math.Abs(Reach(offsets[w].End) - line.words[w][1]);
                n += 2;
            }
        }
    }
    return sum / n * 1000;
}

static Func<int, double, double> AppFlow(Song song)
{
    var timeline = new LyricsTimeline(song.lines.Select(l => new LyricsLine(TimeSpan.FromSeconds(l.start), l.text)).ToList());
    var flow = new LyricFlow(timeline, TimeSpan.FromSeconds(song.lines[^1].end + 3));
    return (i, t) =>
    {
        var fill = flow.At(i, TimeSpan.FromSeconds(t), LyricFlowMode.Adaptive);
        return fill.IsComplete ? double.MaxValue : fill.ByText ? fill.Value : fill.Value * song.lines[i].text.Length;
    };
}

static void Report(string name, List<Song> songs, Func<Song, object> make)
{
    var errors = new List<double>();
    var bias = 0.0;
    var byLang = new Dictionary<string, List<double>>();
    foreach (var song in songs)
    {
        var flow = make(song);
        Func<int, double, double> at = flow switch { Func<int, double, double> f => f, PortFlow p => p.At, _ => throw new() };
        for (var i = 0; i < song.lines.Count; i++)
        {
            var line = song.lines[i];
            var offsets = Regex.Matches(line.text, @"\S+").Select(m => (m.Index, End: m.Index + m.Length)).ToList();
            if (offsets.Count != line.words.Count) continue;
            // Time the estimated fill first reaches an offset, sampled every 10 ms through the line.
            double Reach(double offset)
            {
                for (var t = line.start; t < line.start + 30; t += 0.01)
                    if (at(i, t) >= offset - 1e-6) return t;
                return line.start + 30;
            }
            for (var w = 0; w < offsets.Count; w++)
            {
                var (s, e) = (line.words[w][0], line.words[w][1]);
                var es = Reach(offsets[w].Index + 0.5);
                var ee = Reach(offsets[w].End);
                foreach (var d in new[] { es - s, ee - e })
                {
                    errors.Add(Math.Abs(d));
                    bias += d;
                    (byLang.TryGetValue(song.lang, out var l) ? l : byLang[song.lang] = []).Add(Math.Abs(d));
                }
            }
        }
    }
    errors.Sort();
    var within = errors.Count(e => e <= .3) * 100.0 / errors.Count;
    Console.WriteLine($"{name,-38} MAE {errors.Average() * 1000,5:0} ms  median {errors[errors.Count / 2] * 1000,4:0} ms  p90 {errors[errors.Count * 9 / 10] * 1000,5:0} ms  ≤300ms {within,4:0.0}%  bias {bias / errors.Count * 1000,+5:+0;-0} ms  | "
        + string.Join(" ", byLang.OrderBy(k => k.Key).Select(k => $"{k.Key[..2]} {k.Value.Average() * 1000:0}")));
}

record Line(double start, double end, string text, List<double[]> words);
record Song(string lang, List<Line> lines);

record Options
{
    public bool Hold { get; init; }
    public double HoldCap { get; init; } = .6;
    public double FinalBoost { get; init; }
    public bool SilentE { get; init; }
    public double Onset { get; init; }
    public double Tempo { get; init; } = 1;
    public double CoreFrac { get; init; } = .6;
}

/// <summary>A copy of LyricFlow's adaptive estimate with the variants under study.</summary>
sealed class PortFlow
{
    private readonly (double Start, (double T, int O)[] Points)[] _p;

    public PortFlow(Song song, Options o)
    {
        var lines = song.lines;
        var words = lines.Select(l => GetWords(l.text, o, song.lang)).ToArray();
        var units = words.Select(w => w.Sum(x => x.Weight)).ToArray();
        var windows = new double[lines.Count];
        var rate = new double[lines.Count];
        var next = lines[^1].end + 3;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            windows[i] = next > lines[i].start ? next - lines[i].start : 0;
            var c = units[i] > 0 ? windows[i] / units[i] : 0;
            if (c is >= .13 and <= .95 && windows[i] <= 10 && i + 1 < lines.Count) rate[i] = c;
            next = lines[i].start;
        }
        var typical = Median(rate);
        _p = new (double, (double, int)[])[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var first = Math.Max(0, i - 4); var last = Math.Min(rate.Length, i + 5);
            var nearby = rate[first..last]; nearby[i - first] = 0;
            var spu = (Median(nearby) ?? typical ?? .42) * o.Tempo;
            var expected = Math.Clamp(spu * units[i] + .25, .65, 12);
            var window = windows[i];
            var adaptive = window <= 0 ? expected : window;
            if (adaptive > Math.Max(4, expected * 2.2)) adaptive = expected;
            double core = adaptive, hold = 0;
            if (o.Hold && window > 0 && adaptive == window && window > expected)
            {
                // Words at the neighbours' pace; the slack becomes a held last word, the rest silence.
                core = Math.Max(expected, window * o.CoreFrac);
                hold = Math.Min(window - core, core * o.HoldCap);
            }
            _p[i] = (lines[i].start + o.Onset, Estimate(words[i], units[i], core, hold));
        }
    }

    public double At(int i, double t)
    {
        var (start, pts) = _p[i];
        if (pts.Length == 0) return double.MaxValue;
        var e = t - start;
        if (e >= pts[^1].T) return double.MaxValue;
        if (e <= 0) return 0;
        for (var k = 1; k < pts.Length; k++)
            if (pts[k].T > e)
                return pts[k - 1].O + (pts[k].O - pts[k - 1].O) * (e - pts[k - 1].T) / (pts[k].T - pts[k - 1].T);
        return double.MaxValue;
    }

    static (double T, int O)[] Estimate((int End, double Weight)[] w, double total, double seconds, double hold)
    {
        if (w.Length == 0) return [];
        var p = new (double, int)[w.Length + 1];
        var el = 0.0;
        for (var i = 0; i < w.Length; i++)
        {
            el += w[i].Weight / total * seconds + (i == w.Length - 1 ? hold : 0);
            p[i + 1] = (el, w[i].End);
        }
        return p;
    }

    static (int End, double Weight)[] GetWords(string text, Options o, string lang)
    {
        var tokens = Regex.Matches(text, @"\S+");
        if (tokens.Count == 0) return [];
        var r = new (int, double)[tokens.Count];
        for (var i = 0; i < tokens.Count; i++)
        {
            var word = tokens[i].Value;
            var letters = new StringInfo(word).LengthInTextElements;
            int groups = 0; var prev = false;
            foreach (var ch in word.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark) continue;
                var v = "aeiouy".Contains(char.ToLowerInvariant(ch));
                if (v && !prev) groups++;
                prev = v;
            }
            if (o.SilentE && lang == "English")
            {
                var bare = word.TrimEnd(',', '.', '!', '?', ';', ':', '"', '\'').ToLowerInvariant();
                if (groups > 1 && bare.Length > 2 && bare.EndsWith('e') && !bare.EndsWith("le") && !"aeiouy".Contains(bare[^2])) groups--;
            }
            var syl = groups > 0 ? groups : Math.Max(1, letters * .65);
            var pause = word[^1] switch { ',' or ';' or ':' => .25, '.' or '?' or '!' or '…' => .42, _ => 0 };
            var end = i + 1 < tokens.Count ? tokens[i + 1].Index : text.Length;
            var weight = Math.Max(.8, syl * .78 + letters * .1) + pause + (i == tokens.Count - 1 ? o.FinalBoost * .78 : 0);
            r[i] = (end, weight);
        }
        return r;
    }

    static double? Median(double[] s)
    {
        var x = s.Where(v => v > 0).Order().ToArray();
        return x.Length == 0 ? null : (x[x.Length / 2] + x[(x.Length - 1) / 2]) / 2;
    }
}
