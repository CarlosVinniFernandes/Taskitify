using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Taskitify.Core.Lyrics;

/// <summary>What a karaoke source is asked: the song as the player reports it, plus its ISRC when known.</summary>
public sealed record KaraokeQuery(TrackQuery Track, string? Isrc = null);

/// <summary>A source of word- or syllable-timed lyrics, asked after the usual lyrics are shown.</summary>
public interface IKaraokeProvider
{
    /// <summary>Stable id used in settings ("lrclib", "lrcred").</summary>
    string Id { get; }

    /// <summary>The song's karaoke lyrics, or null when this source has none. Network failures throw.</summary>
    Task<KaraokeLyrics?> FindAsync(KaraokeQuery query, CancellationToken ct);
}

/// <summary>A source asked us to slow down (HTTP 429/503): skip it until then.</summary>
public sealed class ProviderCooldownException(TimeSpan retryAfter) : Exception($"Retry after {retryAfter.TotalSeconds:0} s")
{
    public TimeSpan RetryAfter { get; } = retryAfter;

    /// <summary>The delay a 429/503 response asks for (5 s when it says nothing usable).</summary>
    public static ProviderCooldownException From(HttpResponseMessage response)
    {
        var after = response.Headers.RetryAfter;
        var delay = after?.Delta ?? (after?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);
        return new ProviderCooldownException(delay is { } d && d > TimeSpan.Zero ? (d < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : d) : TimeSpan.FromSeconds(5));
    }

    public static bool IsThrottle(HttpStatusCode code) => code is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;
}

/// <summary>
/// Finds karaoke lyrics for a song: the disk cache first, then the enabled sources at once (they
/// answer in a few hundred milliseconds, and asking them in parallel costs less time than in
/// turn), keeping the best result: finer timing, then backing vocals and duets, then the
/// user's order. One lookup per song at a time (callers asking again share it), a deadline per
/// source, a pause for a source that asked for one, and a remembered "none" for a while.
/// </summary>
public sealed class KaraokeService
{
    private readonly IReadOnlyList<IKaraokeProvider> _providers;
    private readonly KaraokeCache? _cache;
    private readonly Func<DateTimeOffset> _now;
    private readonly ConcurrentDictionary<string, Task<KaraokeLyrics?>> _inFlight = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _cooldowns = new();

    public KaraokeService(IEnumerable<IKaraokeProvider> providers, KaraokeCache? cache, Func<DateTimeOffset>? now = null)
    {
        _providers = providers.ToList();
        _cache = cache;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The sources to ask, in the user's order (ids of <see cref="IKaraokeProvider.Id"/>).</summary>
    public Func<IReadOnlyList<string>> Enabled { get; init; } = () => [];

    /// <summary>How long one source may take.</summary>
    public TimeSpan ProviderTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public IReadOnlyList<string> ProviderIds => [.. _providers.Select(p => p.Id)];

    /// <summary>Provider failures, for diagnostics (id, message).</summary>
    public event Action<string, string>? ProviderFailed;

    public async Task<KaraokeLyrics?> FindAsync(KaraokeQuery query, CancellationToken ct)
    {
        var key = LyricsCache.KeyFor(query.Track);
        if (_cache is not null && await _cache.TryGetAsync(key, ct).ConfigureAwait(false) is { } cached)
            return cached.Lyrics;
        // A lookup already running for the song (a reload, a settings change): share it.
        var task = _inFlight.GetOrAdd(key, _ => LookUpAsync(query, key));
        try
        {
            return await task.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            if (task.IsCompleted)
                _inFlight.TryRemove(new KeyValuePair<string, Task<KaraokeLyrics?>>(key, task));
        }
    }

    /// <summary>Forget the song (a reload asks the sources again).</summary>
    public void Forget(TrackQuery track) => _cache?.Remove(LyricsCache.KeyFor(track));

    private async Task<KaraokeLyrics?> LookUpAsync(KaraokeQuery query, string key)
    {
        // Not the caller's token: a caller that gives up (the song changed) must not cancel the
        // lookup others share; each source still has its own deadline.
        var order = Enabled();
        var asked = order.Select(id => _providers.FirstOrDefault(p => p.Id == id)).OfType<IKaraokeProvider>()
            .Where(p => !_cooldowns.TryGetValue(p.Id, out var until) || _now() >= until).ToList();
        if (asked.Count == 0)
            return null;
        var answers = await Task.WhenAll(asked.Select(p => AskAsync(p, query))).ConfigureAwait(false);
        var best = answers.Select((a, i) => (a.Lyrics, Rank: i, a.Failed))
            .Where(a => a.Lyrics is not null && IsSameSong(query.Track, a.Lyrics))
            .OrderByDescending(a => a.Lyrics!.Quality).ThenBy(a => a.Rank)
            .Select(a => a.Lyrics).FirstOrDefault();
        var failed = answers.Any(a => a.Failed);
        if (_cache is not null && (best is not null || !failed))
            await _cache.SaveAsync(key, best, CancellationToken.None).ConfigureAwait(false);
        return best;
    }

    private async Task<(KaraokeLyrics? Lyrics, bool Failed)> AskAsync(IKaraokeProvider provider, KaraokeQuery query)
    {
        using var deadline = new CancellationTokenSource(ProviderTimeout);
        try
        {
            var lyrics = await provider.FindAsync(query, deadline.Token).ConfigureAwait(false);
            return (lyrics is { Lines.Count: > 0 } && lyrics.Sync >= LyricsSyncLevel.Line ? lyrics : null, false);
        }
        catch (ProviderCooldownException ex)
        {
            var until = _now() + ex.RetryAfter;
            _cooldowns.AddOrUpdate(provider.Id, until, (_, current) => current > until ? current : until);
            ProviderFailed?.Invoke(provider.Id, ex.Message);
            return (null, true);
        }
        catch (Exception ex)
        {
            ProviderFailed?.Invoke(provider.Id, ex is OperationCanceledException ? "timed out" : ex.Message);
            return (null, true);
        }
    }

    /// <summary>The source's song is the one asked for (sources searching by name can return a namesake).</summary>
    internal static bool IsSameSong(TrackQuery query, KaraokeLyrics lyrics) =>
        lyrics.MatchedTitle is null || TrackMatcher.IsMatch(query, new LrclibRecord(0, lyrics.MatchedTitle, lyrics.MatchedArtist ?? query.Artist, null, null, false, null, null));
}

/// <summary>
/// Karaoke lyrics on disk, one small JSON file per song next to the lyrics cache ("none" too,
/// for <see cref="NegativeTtl"/>), so a song is looked up once. Corrupt or older files are misses.
/// </summary>
public sealed class KaraokeCache
{
    private const int FormatVersion = 1;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _directory;
    private readonly Func<DateTimeOffset> _now;

    public KaraokeCache(string directory, Func<DateTimeOffset>? now = null)
    {
        _directory = directory;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public TimeSpan NegativeTtl { get; init; } = TimeSpan.FromHours(12);

    public string Directory => _directory;

    private sealed record Entry(int Version, DateTimeOffset SavedAt, KaraokeLyrics? Lyrics);

    /// <summary>The cached answer (Lyrics null: remembered as none), or null when not cached.</summary>
    public async Task<(KaraokeLyrics? Lyrics, bool Found)?> TryGetAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
            return null;
        try
        {
            await using var stream = File.OpenRead(path);
            var entry = await JsonSerializer.DeserializeAsync<Entry>(stream, Json, ct).ConfigureAwait(false);
            if (entry is null || entry.Version != FormatVersion)
                return null;
            if (entry.Lyrics is null)
                return _now() - entry.SavedAt < NegativeTtl ? (null, false) : null;
            return entry.Lyrics.Lines.Count > 0 ? (entry.Lyrics, true) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    public async Task SaveAsync(string key, KaraokeLyrics? lyrics, CancellationToken ct)
    {
        try
        {
            System.IO.Directory.CreateDirectory(_directory);
            var path = PathFor(key);
            var tmp = path + ".tmp";
            await using (var stream = File.Create(tmp))
                await JsonSerializer.SerializeAsync(stream, new Entry(FormatVersion, _now(), lyrics), Json, ct).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The cache is an optimization: a failed write just means a lookup next time.
        }
    }

    public void Remove(string key)
    {
        try
        {
            File.Delete(PathFor(key));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string PathFor(string key) => Path.Combine(_directory, key + ".json");
}
