namespace Taskitify.Core.Lyrics;

/// <summary>One timed line of a synced (LRC) lyric.</summary>
public sealed record LyricsLine(TimeSpan Time, string Text)
{
    /// <summary>Optional Enhanced LRC anchors, indexed into the normalized UTF-16 text.</summary>
    public IReadOnlyList<LyricAnchor>? Anchors { get; init; }

    /// <summary>When the voice stops, when the source says (karaoke sources do; LRC does not).</summary>
    public TimeSpan? End { get; init; }

    /// <summary>Which singer of a duet: 0 the first (the usual side), 1 another (the opposite side).</summary>
    public int Singer { get; init; }

    /// <summary>Backing vocals sung with this line ("(oh-oh)"), each with its own timing.</summary>
    public IReadOnlyList<LyricsVocal>? Background { get; init; }
}

/// <summary>A secondary vocal part: its text, when it is sung and (optionally) word or syllable anchors.</summary>
public sealed record LyricsVocal(string Text, TimeSpan Start, TimeSpan End, IReadOnlyList<LyricAnchor>? Anchors)
{
    /// <summary>A line's backing vocals as one (shown as one smaller line), their anchors kept in place.</summary>
    public static LyricsVocal? Combine(IReadOnlyList<LyricsVocal>? vocals)
    {
        if (vocals is not { Count: > 0 })
            return null;
        if (vocals.Count == 1)
            return vocals[0];
        var text = new System.Text.StringBuilder();
        var anchors = new List<LyricAnchor>();
        var ordered = vocals.OrderBy(v => v.Start).ToList();
        foreach (var vocal in ordered)
        {
            if (text.Length > 0)
                text.Append(' ');
            var offset = text.Length;
            anchors.AddRange((vocal.Anchors ?? [new LyricAnchor(vocal.Start, 0)])
                .Select(a => a with { TextOffset = a.TextOffset + offset })
                .Where(a => anchors.Count == 0 || a.Time > anchors[^1].Time));
            text.Append(vocal.Text);
        }
        return new LyricsVocal(text.ToString(), ordered[0].Start, ordered.Max(v => v.End), anchors);
    }
}

/// <summary>How finely lyrics are timed. Ordered: a higher value is finer.</summary>
public enum LyricsSyncLevel
{
    Plain,
    Line,
    Word,
    Syllable,
}

/// <summary>
/// Lyrics timed word by word or syllable by syllable, from a karaoke source. Granularity and
/// vocal capabilities are separate: a word-timed lyric may have backing vocals, a syllable-timed
/// one may be a duet.
/// </summary>
public sealed record KaraokeLyrics(IReadOnlyList<LyricsLine> Lines, LyricsSyncLevel Sync, string Source)
{
    public bool HasBackground => Lines.Any(l => l.Background is { Count: > 0 });

    public bool HasDuet => Lines.Any(l => l.Singer != 0);

    /// <summary>Who to credit, shown where the lyrics are (a source's own terms may ask for it).</summary>
    public string? Credit { get; init; }

    /// <summary>Where the credit links to.</summary>
    public string? CreditUrl { get; init; }

    /// <summary>The source's own name for the song, to check it is the one playing.</summary>
    public string? MatchedTitle { get; init; }

    public string? MatchedArtist { get; init; }

    // A break of at least this long between two lines (or before the first) is shown as one.
    private static readonly TimeSpan InterludeGap = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Karaoke sources say when each line ends, so the instrumental breaks are known: an empty
    /// line (drawn as the break's dots) where the voice rests long enough, and before the first line.
    /// </summary>
    public static IReadOnlyList<LyricsLine> WithInterludes(IReadOnlyList<LyricsLine> lines)
    {
        var result = new List<LyricsLine>(lines.Count + 8);
        if (lines.Count > 0 && lines[0].Time >= InterludeGap)
            result.Add(new LyricsLine(TimeSpan.Zero, ""));
        for (var i = 0; i < lines.Count; i++)
        {
            result.Add(lines[i]);
            if (i + 1 < lines.Count && lines[i].End is { } end && !string.IsNullOrWhiteSpace(lines[i].Text)
                && !string.IsNullOrWhiteSpace(lines[i + 1].Text) && lines[i + 1].Time - end >= InterludeGap)
                result.Add(new LyricsLine(end, ""));
        }
        return result;
    }


    /// <summary>A score to pick the best of several: finer timing first, then backing vocals, then duets.</summary>
    public int Quality => ((int)Sync * 4) + (HasBackground ? 2 : 0) + (HasDuet ? 1 : 0);
}

public readonly record struct LyricAnchor(TimeSpan Time, int TextOffset);

/// <summary>What we know about the track when asking for lyrics.</summary>
public sealed record TrackQuery(string Title, string Artist, string? Album, TimeSpan? Duration);

public enum LyricsKind
{
    Synced,
    PlainOnly,
    Instrumental,
    NotFound,
    Error,
}

public sealed record LyricsResult(
    LyricsKind Kind,
    IReadOnlyList<LyricsLine> Lines,
    string? PlainText,
    string? MatchedTitle = null,
    string? MatchedArtist = null,
    string? ErrorMessage = null)
{
    public static LyricsResult NotFound { get; } = new(LyricsKind.NotFound, [], null);

    public static LyricsResult Error(string message) => new(LyricsKind.Error, [], null, ErrorMessage: message);

    /// <summary>Provider that supplied the lyrics ("Spotify", "LRCLIB", "Local files"); null when unknown.</summary>
    public string? Source { get; init; }

    /// <summary>Who licenses the lyrics to the source, to be credited (Spotify's partner, e.g. "Musixmatch").</summary>
    public string? Credit { get; init; }

    /// <summary>Served from the memory or disk cache rather than looked up now.</summary>
    public bool FromCache { get; init; }

    /// <summary>A stand-in found while the primary source was failing: cached only for a while.</summary>
    public bool Provisional { get; init; }
}

/// <summary>A record returned by the LRCLIB API (/api/get and /api/search).</summary>
public sealed record LrclibRecord(
    long Id,
    string? TrackName,
    string? ArtistName,
    string? AlbumName,
    double? Duration,
    bool Instrumental,
    string? PlainLyrics,
    string? SyncedLyrics)
{
    /// <summary>LRCLIB: the Lyricsfile document has word timings (lines[].words).</summary>
    public bool HasWordSync { get; init; }

    /// <summary>LRCLIB: the raw Lyricsfile YAML (every record has one; word timings when <see cref="HasWordSync"/>).</summary>
    public string? Lyricsfile { get; init; }

    /// <summary>Name of the provider that found this record; set by the provider chain, never by the API.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Source { get; init; }

    /// <summary>Who licenses the lyrics to the source (Spotify names its partner: "Musixmatch", "LyricFind"...).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Credit { get; init; }

    /// <summary>Found while the primary source was failing; set by the provider chain, never by the API.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Provisional { get; init; }
}
