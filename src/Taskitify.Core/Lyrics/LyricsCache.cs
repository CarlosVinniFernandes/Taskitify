using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Taskitify.Core.Lyrics;

/// <summary>
/// One JSON file per track in a local directory. Positive results never expire; "not found"
/// expires after <see cref="NegativeTtl"/> so lyrics added to LRCLIB later are picked up.
/// So do stand-ins: lyrics found while the primary source was failing, and entries saved before
/// results recorded their source; the primary is then asked again. Errors are never cached.
/// </summary>
public sealed class LyricsCache
{
    // 2: Spotify became the primary source; version 1 entries hold LRCLIB's lyrics.
    private const int FormatVersion = 2;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _directory;
    private readonly Func<DateTimeOffset> _now;

    public LyricsCache(string directory, Func<DateTimeOffset>? now = null)
    {
        _directory = directory;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public TimeSpan NegativeTtl { get; init; } = TimeSpan.FromHours(6);

    public string Directory => _directory;

    public static string KeyFor(TrackQuery q)
    {
        var duration = q.Duration is { } d ? ((int)Math.Round(d.TotalSeconds)).ToString() : "?";
        var raw = $"{TrackMatcher.NormalizeTitle(q.Title)}|{TrackMatcher.NormalizeText(q.Artist)}|{TrackMatcher.NormalizeText(q.Album)}|{duration}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..32].ToLowerInvariant();
    }

    public async Task<LyricsResult?> TryGetAsync(TrackQuery query, CancellationToken ct) =>
        await ReadAsync(query, ct).ConfigureAwait(false) is { Expired: false } entry ? entry.Result : null;

    /// <summary>An expired stand-in: when looking the song up again fails too (offline), better than nothing.</summary>
    public async Task<LyricsResult?> TryGetExpiredAsync(TrackQuery query, CancellationToken ct) =>
        await ReadAsync(query, ct).ConfigureAwait(false) is { Expired: true, Result.Kind: not LyricsKind.NotFound } entry ? entry.Result : null;

    private async Task<(LyricsResult Result, bool Expired)?> ReadAsync(TrackQuery query, CancellationToken ct)
    {
        var path = PathFor(query);
        if (!File.Exists(path))
            return null;
        try
        {
            await using var stream = File.OpenRead(path);
            var entry = await JsonSerializer.DeserializeAsync<Entry>(stream, Json, ct).ConfigureAwait(false);
            if (entry is null || entry.Version != FormatVersion)
                return null;
            LyricsResult? result = entry.Kind switch
            {
                LyricsKind.Synced => new LyricsResult(LyricsKind.Synced, LrcParser.Parse(entry.Synced), entry.Plain, entry.MatchedTitle, entry.MatchedArtist),
                LyricsKind.PlainOnly => new LyricsResult(LyricsKind.PlainOnly, [], entry.Plain, entry.MatchedTitle, entry.MatchedArtist),
                LyricsKind.Instrumental => new LyricsResult(LyricsKind.Instrumental, [], null, entry.MatchedTitle, entry.MatchedArtist),
                LyricsKind.NotFound => LyricsResult.NotFound,
                _ => null,
            };
            if (result is null)
                return null;
            // Lyrics with no source on record predate this field: like a stand-in, they may be the
            // fallback's version from a time the primary was failing.
            var temporary = entry.Kind == LyricsKind.NotFound || entry.Provisional || entry.Source is null;
            return (result with { Source = entry.Source, Provisional = entry.Provisional }, temporary && _now() - entry.SavedAt > NegativeTtl);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null; // a corrupt entry is just a cache miss
        }
    }

    public async Task SaveAsync(TrackQuery query, LyricsResult result, string? rawSynced, CancellationToken ct)
    {
        if (result.Kind == LyricsKind.Error)
            return;
        System.IO.Directory.CreateDirectory(_directory);
        var entry = new Entry(FormatVersion, result.Kind, rawSynced, result.PlainText, result.MatchedTitle, result.MatchedArtist, _now(), result.Source, result.Provisional);
        var path = PathFor(query);
        var tmp = path + ".tmp";
        await using (var stream = File.Create(tmp))
            await JsonSerializer.SerializeAsync(stream, entry, Json, ct).ConfigureAwait(false);
        File.Move(tmp, path, overwrite: true);
    }

    public void Remove(TrackQuery query)
    {
        try
        {
            File.Delete(PathFor(query));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Clear()
    {
        if (System.IO.Directory.Exists(_directory))
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "*.json"))
                File.Delete(file);
        }
    }

    private string PathFor(TrackQuery q) => Path.Combine(_directory, KeyFor(q) + ".json");

    private sealed record Entry(
        int Version,
        LyricsKind Kind,
        string? Synced,
        string? Plain,
        string? MatchedTitle,
        string? MatchedArtist,
        DateTimeOffset SavedAt,
        // Added after version 2 shipped; older entries read them as null ("unknown") and false.
        string? Source = null,
        bool Provisional = false);
}
