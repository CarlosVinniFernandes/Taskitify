using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

namespace Taskitify.Core.Lyrics;

/// <summary>
/// LRCLIB answers shared for a few minutes between the line-lyrics fallback and the karaoke
/// lookup: the same song is never asked twice in a row (LRCLIB's rate limit makes each request count).
/// </summary>
public sealed class MemoizingLrclibClient(ILrclibClient inner, Func<DateTimeOffset>? now = null) : ILrclibClient
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private const int MaxEntries = 64;
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);
    private readonly ConcurrentDictionary<string, (Task<object?> Task, DateTimeOffset At)> _memo = new();

    public Task<LrclibRecord?> GetAsync(TrackQuery query, CancellationToken ct) =>
        Remember<LrclibRecord?>("get|" + LyricsCache.KeyFor(query), () => inner.GetAsync(query, CancellationToken.None), ct);

    public Task<IReadOnlyList<LrclibRecord>> SearchAsync(string title, string artist, CancellationToken ct) =>
        Remember<IReadOnlyList<LrclibRecord>>($"search|{TrackMatcher.NormalizeTitle(title)}|{TrackMatcher.NormalizeText(artist)}",
            () => inner.SearchAsync(title, artist, CancellationToken.None), ct);

    private async Task<T> Remember<T>(string key, Func<Task<T>> fetch, CancellationToken ct)
    {
        if (_memo.TryGetValue(key, out var entry) && (_now() - entry.At > Ttl || entry.Task.IsFaulted || entry.Task.IsCanceled))
            _memo.TryRemove(key, out _);
        if (_memo.Count >= MaxEntries)
            _memo.Clear();
        var shared = _memo.GetOrAdd(key, _ => (Wrap(fetch), _now()));
        return (T)(await shared.Task.WaitAsync(ct).ConfigureAwait(false))!;

        static async Task<object?> Wrap(Func<Task<T>> fetch) => await fetch().ConfigureAwait(false);
    }
}

/// <summary>
/// LRCLIB's word-timed lyrics: its records carry a Lyricsfile whose lines have word timings when
/// <c>hasWordSync</c> is set. The exact match first, then the search's best word-synced version.
/// </summary>
public sealed class LrclibKaraokeProvider(ILrclibClient client) : IKaraokeProvider
{
    public string Id => "lrclib";

    public async Task<KaraokeLyrics?> FindAsync(KaraokeQuery query, CancellationToken ct)
    {
        var track = query.Track;
        var exact = await client.GetAsync(track, ct).ConfigureAwait(false);
        var record = exact is { HasWordSync: true } && TrackMatcher.IsMatch(track, exact) ? exact : null;
        if (record is null)
        {
            record = (await client.SearchAsync(track.Title, track.Artist, ct).ConfigureAwait(false))
                .Where(r => r.HasWordSync && TrackMatcher.IsMatch(track, r))
                .OrderBy(r => TrackMatcher.DurationDistance(track, r))
                .FirstOrDefault();
        }
        if (record?.Lyricsfile is not { } yaml)
            return null;
        return LyricsfileParser.Parse(yaml, "LRCLIB") is { Sync: >= LyricsSyncLevel.Word } lyrics
            ? lyrics with { MatchedTitle = record.TrackName, MatchedArtist = record.ArtistName, Credit = "LRCLIB", CreditUrl = "https://lrclib.net" }
            : null;
    }
}

/// <summary>
/// lrc.red (https://lrc.red/api): a public library of word- and syllable-timed TTML, with backing
/// vocals and duet sides, found by ISRC or by search. No key; it asks apps to identify themselves
/// (our User-Agent does) and to credit and link it where the lyrics are shown.
/// </summary>
public sealed class LrcRedProvider(HttpClient http) : IKaraokeProvider
{
    private static readonly Uri Base = new("https://lrc.red/");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Id => "lrcred";

    private sealed record Hit(string? Isrc, string? Title, string? Artist, string? Album, double? Duration);

    private sealed record SearchPage(List<Hit>? Hits);

    public async Task<KaraokeLyrics?> FindAsync(KaraokeQuery query, CancellationToken ct)
    {
        var track = query.Track;
        var isrcs = new List<string>();
        if (IsIsrc(query.Isrc))
            isrcs.Add(query.Isrc!.ToUpperInvariant());
        var page = await GetJsonAsync<SearchPage>("search.json?q=" + Uri.EscapeDataString($"{track.Title} {track.Artist}"), ct).ConfigureAwait(false);
        var album = TrackMatcher.NormalizeText(track.Album);
        isrcs.AddRange((page?.Hits ?? [])
            .Where(h => IsIsrc(h.Isrc) && TrackMatcher.IsMatch(track, AsRecord(h)))
            // The same album first (a compilation's copy may be timed differently), then the closest length.
            .OrderBy(h => album.Length > 0 && TrackMatcher.NormalizeText(h.Album) == album ? 0 : 1)
            .ThenBy(h => TrackMatcher.DurationDistance(track, AsRecord(h)))
            .Select(h => h.Isrc!.ToUpperInvariant())
            .Where(i => !isrcs.Contains(i))
            .Take(3));
        KaraokeLyrics? lineOnly = null;
        var hit = (page?.Hits ?? []).ToDictionary(h => h.Isrc?.ToUpperInvariant() ?? "", h => h, StringComparer.Ordinal);
        foreach (var isrc in isrcs.Take(3))
        {
            var ttml = await GetTextAsync($"s/{isrc}.ttml", ct).ConfigureAwait(false);
            if (TtmlParser.Parse(ttml, "lrc.red") is not { } lyrics)
                continue;
            hit.TryGetValue(isrc, out var h);
            lyrics = lyrics with { Credit = "lrc.red", CreditUrl = "https://lrc.red", MatchedTitle = h?.Title, MatchedArtist = h?.Artist };
            if (lyrics.Sync >= LyricsSyncLevel.Word)
                return lyrics;
            lineOnly ??= lyrics;
        }
        return lineOnly;
    }

    private static LrclibRecord AsRecord(Hit h) => new(0, h.Title, h.Artist, h.Album, h.Duration, false, null, null);

    private static bool IsIsrc(string? value) => value is { Length: 12 } && value.All(char.IsAsciiLetterOrDigit);

    private async Task<T?> GetJsonAsync<T>(string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(new Uri(Base, path), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (ProviderCooldownException.IsThrottle(response.StatusCode))
            throw ProviderCooldownException.From(response);
        if (!response.IsSuccessStatusCode)
            return default;
        return await response.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
    }

    private async Task<string?> GetTextAsync(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Base, path));
        request.Headers.Accept.ParseAdd("application/ttml+xml");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (ProviderCooldownException.IsThrottle(response.StatusCode))
            throw ProviderCooldownException.From(response);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 2_000_000)
            return null;
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}
