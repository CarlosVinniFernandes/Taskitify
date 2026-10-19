namespace Taskitify.Core.Lyrics;

/// <summary>
/// One place lyrics can come from. <see cref="LyricsService"/> asks local providers first (never
/// cached, so edits to the user's own files show up at once), then the cache, then remote
/// providers in parallel until one returns valid synced lyrics.
/// </summary>
public interface ILyricsProvider
{
    /// <summary>Short name for diagnostics ("Local files", "LRCLIB").</summary>
    string Name { get; }

    /// <summary>Instant, offline source whose results are not cached.</summary>
    bool IsLocal { get; }

    /// <summary>
    /// The best match for the track, or null when this source has nothing. Records use LRCLIB's
    /// shape, which every source can fill. Network failures surface as exceptions.
    /// </summary>
    Task<LrclibRecord?> FindAsync(TrackQuery query, CancellationToken ct);

    /// <summary>Drop anything remembered about the track: a reload asks this source again.</summary>
    void Forget(TrackQuery query)
    {
    }
}

/// <summary>
/// LRCLIB: /api/search filtered by <see cref="TrackMatcher"/>, preferring synced lyrics, with
/// the exact /api/get as fallback.
/// </summary>
public sealed class LrclibProvider : ILyricsProvider
{
    private readonly ILrclibClient _client;

    public LrclibProvider(ILrclibClient client) => _client = client;

    public string Name => "LRCLIB";

    public bool IsLocal => false;

    /// <summary>
    /// Search first: one request returns every version of the song with its lyrics, and LRCLIB's
    /// rate limit makes each request count. The exact /api/get (which can also make LRCLIB look
    /// in external sources) is only used when the search has nothing usable.
    /// </summary>
    public async Task<LrclibRecord?> FindAsync(TrackQuery query, CancellationToken ct)
    {
        var candidates = (await _client.SearchAsync(query.Title, query.Artist, ct).ConfigureAwait(false))
            .Where(r => TrackMatcher.IsMatch(query, r))
            .OrderBy(r => TrackMatcher.DurationDistance(query, r))
            .ToList();

        var synced = candidates.FirstOrDefault(LyricsService.HasSynced);
        if (synced is not null)
            return synced;
        var instrumental = candidates.FirstOrDefault(r => r.Instrumental);
        if (instrumental is not null)
            return instrumental;

        var exact = await _client.GetAsync(query, ct).ConfigureAwait(false);
        if (exact is not null && TrackMatcher.IsMatch(query, exact) && (LyricsService.HasSynced(exact) || exact.Instrumental))
            return exact;

        return candidates.FirstOrDefault() ?? (exact is not null && TrackMatcher.IsMatch(query, exact) ? exact : null);
    }
}
