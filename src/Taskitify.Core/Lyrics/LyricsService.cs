using System.Collections.Concurrent;
using System.Text.Json;

namespace Taskitify.Core.Lyrics;

public interface ILyricsService
{
    Task<LyricsResult> GetLyricsAsync(TrackQuery query, CancellationToken ct);

    /// <summary>Drop cached results (memory and disk) for this query.</summary>
    Task ForgetAsync(TrackQuery query);
}

/// <summary>
/// Looks up lyrics on LRCLIB: exact /api/get first, then /api/search filtered by
/// <see cref="TrackMatcher"/>, preferring synced lyrics. Results go to memory and disk caches.
/// </summary>
public sealed class LyricsService : ILyricsService
{
    private const int MemoryCacheLimit = 50;

    private readonly ILrclibClient _client;
    private readonly LyricsCache? _cache;
    private readonly ConcurrentDictionary<string, LyricsResult> _memory = new();

    public LyricsService(ILrclibClient client, LyricsCache? cache)
    {
        _client = client;
        _cache = cache;
    }

    public async Task<LyricsResult> GetLyricsAsync(TrackQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Title) || string.IsNullOrWhiteSpace(query.Artist))
            return LyricsResult.NotFound;

        var key = LyricsCache.KeyFor(query);
        if (_memory.TryGetValue(key, out var cached))
            return cached;

        if (_cache is not null && await _cache.TryGetAsync(query, ct).ConfigureAwait(false) is { } fromDisk)
        {
            Remember(key, fromDisk);
            return fromDisk;
        }

        LrclibRecord? best;
        try
        {
            best = await FindBestAsync(query, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return LyricsResult.Error(ex.Message);
        }

        var result = FromRecord(best);
        Remember(key, result);
        if (_cache is not null)
        {
            try
            {
                await _cache.SaveAsync(query, result, best?.SyncedLyrics, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Cache write failures must not hide lyrics we already have.
            }
        }
        return result;
    }

    public Task ForgetAsync(TrackQuery query)
    {
        _memory.TryRemove(LyricsCache.KeyFor(query), out _);
        _cache?.Remove(query);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Search first: one request returns every version of the song with its lyrics, and LRCLIB's
    /// rate limit makes each request count. The exact /api/get (which can also make LRCLIB look
    /// in external sources) is only used when the search has nothing usable.
    /// </summary>
    private async Task<LrclibRecord?> FindBestAsync(TrackQuery query, CancellationToken ct)
    {
        var candidates = (await _client.SearchAsync(query.Title, query.Artist, ct).ConfigureAwait(false))
            .Where(r => TrackMatcher.IsMatch(query, r))
            .OrderBy(r => TrackMatcher.DurationDistance(query, r))
            .ToList();

        var synced = candidates.FirstOrDefault(HasSynced);
        if (synced is not null)
            return synced;
        var instrumental = candidates.FirstOrDefault(r => r.Instrumental);
        if (instrumental is not null)
            return instrumental;

        var exact = await _client.GetAsync(query, ct).ConfigureAwait(false);
        if (exact is not null && TrackMatcher.IsMatch(query, exact) && (HasSynced(exact) || exact.Instrumental))
            return exact;

        return candidates.FirstOrDefault() ?? (exact is not null && TrackMatcher.IsMatch(query, exact) ? exact : null);
    }

    internal static LyricsResult FromRecord(LrclibRecord? r)
    {
        if (r is null)
            return LyricsResult.NotFound;
        if (r.Instrumental)
            return new LyricsResult(LyricsKind.Instrumental, [], null, r.TrackName, r.ArtistName);
        var lines = LrcParser.Parse(r.SyncedLyrics);
        if (lines.Count > 0)
            return new LyricsResult(LyricsKind.Synced, lines, r.PlainLyrics, r.TrackName, r.ArtistName);
        if (!string.IsNullOrWhiteSpace(r.PlainLyrics))
            return new LyricsResult(LyricsKind.PlainOnly, [], r.PlainLyrics, r.TrackName, r.ArtistName);
        return LyricsResult.NotFound;
    }

    private static bool HasSynced(LrclibRecord r) => !string.IsNullOrWhiteSpace(r.SyncedLyrics);

    private void Remember(string key, LyricsResult result)
    {
        if (_memory.Count >= MemoryCacheLimit)
            _memory.Clear();
        _memory[key] = result;
    }
}
