namespace Taskitify.Core.Lyrics;

/// <summary>One timed line of a synced (LRC) lyric.</summary>
public sealed record LyricsLine(TimeSpan Time, string Text);

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
    /// <summary>Name of the provider that found this record; set by the provider chain, never by the API.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Source { get; init; }

    /// <summary>Found while the primary source was failing; set by the provider chain, never by the API.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Provisional { get; init; }
}
