using System.Collections.Concurrent;

namespace Taskitify.Core.Lyrics;

public interface ILyricsService
{
    Task<LyricsResult> GetLyricsAsync(TrackQuery query, CancellationToken ct);

    /// <summary>Drop cached results (memory and disk) for this query.</summary>
    Task ForgetAsync(TrackQuery query);

    /// <summary>The remote sources a song can be looked up in one by one ("Spotify", "LRCLIB").</summary>
    IReadOnlyList<string> Sources => [];

    /// <summary>
    /// Ask only <paramref name="source"/>, skipping the caches and the other sources: the user
    /// picks the version. What it finds replaces the cached result; when it finds nothing, or
    /// fails, the cache keeps what it had.
    /// </summary>
    Task<LyricsResult> GetLyricsFromAsync(TrackQuery query, string source, CancellationToken ct) => GetLyricsAsync(query, ct);
}

/// <summary>
/// Looks up lyrics through a chain of <see cref="ILyricsProvider"/>s: local providers first
/// (instant, never cached), then the memory and disk caches, then remote providers in parallel.
/// The first valid synced (or instrumental) result wins and cancels the remaining lookups.
/// Plain lyrics are kept only when no provider has synced ones.
/// </summary>
public sealed class LyricsService : ILyricsService
{
    private const int MemoryCacheLimit = 50;

    private readonly IReadOnlyList<ILyricsProvider> _local;
    private readonly IReadOnlyList<ILyricsProvider> _remote;
    private readonly LyricsCache? _cache;
    private readonly ConcurrentDictionary<string, LyricsResult> _memory = new();

    public LyricsService(IEnumerable<ILyricsProvider> providers, LyricsCache? cache)
    {
        var list = providers.ToList();
        _local = list.Where(p => p.IsLocal).ToList();
        _remote = list.Where(p => !p.IsLocal).ToList();
        _cache = cache;
        Sources = _remote.SelectMany(p => p.Sources).Select(p => p.Name).ToList();
    }

    /// <summary>LRCLIB only, as before providers existed.</summary>
    public LyricsService(ILrclibClient client, LyricsCache? cache)
        : this([new LrclibProvider(client)], cache)
    {
    }

    public TimeSpan ProviderTimeout { get; init; } = TimeSpan.FromSeconds(90);

    public IReadOnlyList<string> Sources { get; }

    public async Task<LyricsResult> GetLyricsAsync(TrackQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Title) || string.IsNullOrWhiteSpace(query.Artist))
            return LyricsResult.NotFound;

        foreach (var provider in _local)
        {
            var record = WithSource(await provider.FindAsync(query, ct).ConfigureAwait(false), provider);
            if (record is not null && FromRecord(record) is { Kind: not LyricsKind.NotFound } local)
                return local;
        }

        var key = LyricsCache.KeyFor(query);
        if (_memory.TryGetValue(key, out var cached))
            return cached with { FromCache = true };

        if (_cache is not null && await _cache.TryGetAsync(query, ct).ConfigureAwait(false) is { } fromDisk)
        {
            fromDisk = fromDisk with { FromCache = true };
            Remember(key, fromDisk);
            return fromDisk;
        }

        var (best, error) = await FindRemoteAsync(query, ct).ConfigureAwait(false);
        if (best is null && error is not null)
        {
            // Offline, say: a stand-in the cache let expire still beats no lyrics. Not remembered,
            // so the next lookup asks the sources again.
            if (_cache is not null && await _cache.TryGetExpiredAsync(query, ct).ConfigureAwait(false) is { } expired)
                return expired with { FromCache = true };
            return LyricsResult.Error(error);
        }

        var result = FromRecord(best);
        await KeepAsync(query, result, best?.SyncedLyrics, ct).ConfigureAwait(false);
        return result;
    }

    public async Task<LyricsResult> GetLyricsFromAsync(TrackQuery query, string source, CancellationToken ct)
    {
        var provider = _remote.SelectMany(p => p.Sources).FirstOrDefault(p => p.Name == source);
        if (provider is null || string.IsNullOrWhiteSpace(query.Title) || string.IsNullOrWhiteSpace(query.Artist))
            return LyricsResult.NotFound;
        var (record, error) = await FindProviderAsync(provider, query, ct).ConfigureAwait(false);
        var result = FromRecord(record);
        if (result.Kind == LyricsKind.NotFound)
            return error is null ? result : LyricsResult.Error(error);
        await KeepAsync(query, result, record?.SyncedLyrics, ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>The result for the song from now on, in memory and on disk.</summary>
    private async Task KeepAsync(TrackQuery query, LyricsResult result, string? rawSynced, CancellationToken ct)
    {
        Remember(LyricsCache.KeyFor(query), result);
        if (_cache is null)
            return;
        try
        {
            await _cache.SaveAsync(query, result, rawSynced, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cache write failures must not hide lyrics we already have.
        }
    }

    public Task ForgetAsync(TrackQuery query)
    {
        _memory.TryRemove(LyricsCache.KeyFor(query), out _);
        _cache?.Remove(query);
        foreach (var provider in _local.Concat(_remote))
            provider.Forget(query);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The best record from the remote providers, and the first failure seen. A failure only
    /// matters when nothing was found: it turns "not found" (cached) into an error (retried).
    /// </summary>
    private async Task<(LrclibRecord? Best, string? Error)> FindRemoteAsync(TrackQuery query, CancellationToken ct)
    {
        using var race = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pending = _remote.Select(provider => FindProviderAsync(provider, query, race.Token)).ToList();
        LrclibRecord? fallback = null;
        string? error = null;
        try
        {
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending).WaitAsync(ct).ConfigureAwait(false);
                pending.Remove(completed);
                var (record, failure) = await completed.ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                var kind = FromRecord(record).Kind;
                if (kind is LyricsKind.Synced or LyricsKind.Instrumental)
                    return (record, null);
                if (kind == LyricsKind.PlainOnly)
                    fallback ??= record;
                error ??= failure;
            }
            return (fallback, error);
        }
        finally
        {
            race.Cancel();
        }
    }

    private async Task<(LrclibRecord? Record, string? Error)> FindProviderAsync(
        ILyricsProvider provider, TrackQuery query, CancellationToken ct)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
        attempt.CancelAfter(ProviderTimeout);
        try
        {
            var lookup = provider.FindAsync(query, attempt.Token);
            // Observe late failures even if a provider ignores cancellation.
            _ = lookup.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return (WithSource(await lookup.WaitAsync(attempt.Token).ConfigureAwait(false), provider), null);
        }
        catch (OperationCanceledException)
        {
            return (null, ct.IsCancellationRequested ? null : $"{provider.Name} timed out");
        }
        catch (Exception ex)
        {
            // A broken source must not prevent another source from supplying lyrics.
            return (null, $"{provider.Name}: {ex.Message}");
        }
    }

    internal static LyricsResult FromRecord(LrclibRecord? r)
    {
        if (r is null)
            return LyricsResult.NotFound;
        if (r.Instrumental)
            return new LyricsResult(LyricsKind.Instrumental, [], null, r.TrackName, r.ArtistName) { Source = r.Source, Provisional = r.Provisional };
        var lines = LrcParser.Parse(r.SyncedLyrics);
        if (lines.Count > 0)
            return new LyricsResult(LyricsKind.Synced, lines, r.PlainLyrics, r.TrackName, r.ArtistName) { Source = r.Source, Provisional = r.Provisional };
        if (!string.IsNullOrWhiteSpace(r.PlainLyrics))
            return new LyricsResult(LyricsKind.PlainOnly, [], r.PlainLyrics, r.TrackName, r.ArtistName) { Source = r.Source, Provisional = r.Provisional };
        return LyricsResult.NotFound;
    }

    internal static bool HasSynced(LrclibRecord r) => !string.IsNullOrWhiteSpace(r.SyncedLyrics);

    /// <summary>Tag a record with the provider that found it, unless an inner provider already did.</summary>
    internal static LrclibRecord? WithSource(LrclibRecord? record, ILyricsProvider provider) =>
        record is { Source: null } ? record with { Source = provider.Name } : record;

    private void Remember(string key, LyricsResult result)
    {
        if (_memory.Count >= MemoryCacheLimit)
            _memory.Clear();
        _memory[key] = result;
    }
}
