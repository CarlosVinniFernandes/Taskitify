using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Taskitify.Core.Lyrics;

/// <summary>
/// One JSON file per track in a local directory. Positive results never expire; "not found"
/// expires after <see cref="NegativeTtl"/> so lyrics added to LRCLIB later are picked up.
/// Errors are never cached.
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

    public async Task<LyricsResult?> TryGetAsync(TrackQuery query, CancellationToken ct)
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
            if (entry.Kind == LyricsKind.NotFound && _now() - entry.SavedAt > NegativeTtl)
                return null;
            return entry.Kind switch
            {
                LyricsKind.Synced => new LyricsResult(LyricsKind.Synced, LrcParser.Parse(entry.Synced), entry.Plain, entry.MatchedTitle, entry.MatchedArtist),
                LyricsKind.PlainOnly => new LyricsResult(LyricsKind.PlainOnly, [], entry.Plain, entry.MatchedTitle, entry.MatchedArtist),
                LyricsKind.Instrumental => new LyricsResult(LyricsKind.Instrumental, [], null, entry.MatchedTitle, entry.MatchedArtist),
                LyricsKind.NotFound => LyricsResult.NotFound,
                _ => null,
            };
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
        var entry = new Entry(FormatVersion, result.Kind, rawSynced, result.PlainText, result.MatchedTitle, result.MatchedArtist, _now());
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
        DateTimeOffset SavedAt);
}
