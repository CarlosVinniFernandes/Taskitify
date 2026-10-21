using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Taskitify.Core.Lyrics;

public sealed record SpotifyCredentials(string AccessToken, string? ClientToken);
/// <param name="CoverUrl">The album cover on Spotify's image CDN, about the size the overlay draws.</param>
public sealed record SpotifyTrack(string Id, LrclibRecord Metadata, string? CoverUrl = null);

public interface ISpotifyLyricsSession
{
    /// <summary>The web player's own track search, once it has run in the session; null before.</summary>
    SpotifySearchTemplate? SearchTemplate { get; }

    /// <summary>The token if it is still valid; never starts or loads the web player.</summary>
    SpotifyCredentials? ValidCredentials { get; }

    /// <summary>Null when signed out; throws when Spotify could not be reached (a failure to retry).</summary>
    /// <param name="searchFor">The lookup that needs the token, if any: a web player that has to
    /// load anyway can open on this song's search and capture <see cref="SearchTemplate"/> too.</param>
    Task<SpotifyCredentials?> GetCredentialsAsync(TrackQuery? searchFor, CancellationToken ct);

    /// <summary>Spotify rejected these credentials (401): drop them and get new ones.</summary>
    Task<SpotifyCredentials?> RenewCredentialsAsync(SpotifyCredentials rejected, CancellationToken ct);
    Task<IReadOnlyList<SpotifyTrack>> SearchAsync(TrackQuery query, CancellationToken ct);
    void ReportStatus(string key);
}

/// <summary>Optional, unofficial Spotify web-player source. No cookies or passwords leave the browser profile.</summary>
/// <param name="log">Receives one diagnostic line per lookup: status codes and timings, never tokens or URLs.</param>
public sealed class SpotifyLyricsProvider(HttpClient http, ISpotifyLyricsSession session, Action<string>? log = null) : ILyricsProvider
{
    private static readonly TimeSpan ReplayTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PublicSearchTimeout = TimeSpan.FromSeconds(3);
    private const int MaxCoverBytes = 2 * 1024 * 1024;
    private const int MaxCovers = 64;

    // Cover of the song each recent search matched, by query: Spotify's desktop app sometimes
    // publishes a song to Windows without one, usually right when its lyrics are looked up.
    private readonly ConcurrentDictionary<string, string> _covers = new();

    // A captured search Spotify rejected; skipped until the web player captures another one.
    private SpotifySearchTemplate? _staleTemplate;

    // The public endpoint answers web-player credentials with 429 and a Retry-After.
    private DateTimeOffset _publicSearchAfter = DateTimeOffset.MinValue;
    private readonly Func<DateTimeOffset> _now = () => DateTimeOffset.UtcNow;

    internal SpotifyLyricsProvider(HttpClient http, ISpotifyLyricsSession session, Func<DateTimeOffset> now, Action<string>? log = null)
        : this(http, session, log) => _now = now;

    public string Name => "Spotify";
    public bool IsLocal => false;

    public async Task<LrclibRecord?> FindAsync(TrackQuery query, CancellationToken ct)
    {
        var trace = log is null ? null : new List<string>();
        var clock = Stopwatch.StartNew();
        try
        {
            return await FindAsync(query, trace, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (trace is not null)
        {
            trace.Add(ex is OperationCanceledException ? "cancelled" : $"failed: {ex.GetType().Name}");
            throw;
        }
        finally
        {
            if (trace is not null)
                log!($"Spotify lookup: {string.Join(", ", trace)} ({clock.ElapsedMilliseconds} ms)");
        }
    }

    private async Task<LrclibRecord?> FindAsync(TrackQuery query, List<string>? trace, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var token = timeout.Token;
        var step = Stopwatch.StartNew();
        var credentials = await session.GetCredentialsAsync(query, token).ConfigureAwait(false);
        trace?.Add(credentials is null ? "no session" : $"token {step.ElapsedMilliseconds} ms");
        if (credentials is null) return null;

        // Cheapest first: the web player's own search sent again without the browser (once the
        // web player has run one), the public endpoint (which answers web-player credentials with
        // 429 nearly every time), and only then the web player itself, which loads a whole page.
        var term = query.Title + " " + query.Artist;
        var (replayed, failure) = await ReplaySearchAsync(term, credentials, trace, token).ConfigureAwait(false);
        // Spotify unreachable or busy: the web player would search the same servers, and waking
        // it on every retry costs a page load. A failure is retried; "no lyrics" is cached for hours.
        if (failure is { StatusCode: not HttpStatusCode.Unauthorized })
            throw failure;
        var tracks = replayed ?? [];
        if (!tracks.Any(t => TrackMatcher.IsMatch(query, t.Metadata)))
            tracks = await PublicSearchAsync(term, credentials, trace, token).ConfigureAwait(false);
        if (!tracks.Any(t => TrackMatcher.IsMatch(query, t.Metadata)))
        {
            step.Restart();
            tracks = await session.SearchAsync(query, token).ConfigureAwait(false);
            trace?.Add($"web player search {tracks.Count} found {step.ElapsedMilliseconds} ms");
            credentials = await session.GetCredentialsAsync(null, token).ConfigureAwait(false);
            if (credentials is null) return null;
            // The web player's search returns once its query is captured: send that query here.
            if (!tracks.Any(t => TrackMatcher.IsMatch(query, t.Metadata)))
            {
                var answered = replayed is not null || tracks.Count > 0;
                (replayed, failure) = await ReplaySearchAsync(term, credentials, trace, token).ConfigureAwait(false);
                tracks = replayed ?? [];
                // No search was answered, so nothing is known about this song's lyrics. A token
                // still rejected after the web player loaded again is reported like the lyrics' 401.
                if (failure is not null && !answered)
                {
                    if (failure.StatusCode == HttpStatusCode.Unauthorized)
                        session.ReportStatus("SpotifyLyricsDenied");
                    throw failure;
                }
                // Neither did a web player that has not captured any search yet (the first lookup
                // after starting, with Spotify failing). One that only had a retired query does not
                // fail: a page that never captures one would be reloaded on every retry.
                if (!answered && replayed is null && session.SearchTemplate is null)
                    throw new HttpRequestException("Spotify web player captured no search");
            }
        }

        var matching = tracks.Where(t => TrackMatcher.IsMatch(query, t.Metadata))
            .OrderBy(t => TrackMatcher.DurationDistance(query, t.Metadata)).ToList();
        if (matching.Select(t => t.CoverUrl).FirstOrDefault(url => url is not null) is { } cover)
            RememberCover(LyricsCache.KeyFor(query), cover);

        LrclibRecord? plain = null;
        var renewed = false;
        foreach (var track in matching.Take(3))
        {
            var (status, record) = await FetchLyricsAsync(track, credentials, trace, token).ConfigureAwait(false);
            if (status == HttpStatusCode.Unauthorized && !renewed)
            {
                // A token can expire or be revoked early: renew it once. Giving up here would
                // make LRCLIB's lyrics win, and be cached, for a song Spotify has lyrics for.
                renewed = true;
                credentials = await session.RenewCredentialsAsync(credentials, token).ConfigureAwait(false);
                trace?.Add(credentials is null ? "token renewal failed" : "token renewed");
                if (credentials is null) return null;
                (status, record) = await FetchLyricsAsync(track, credentials, trace, token).ConfigureAwait(false);
            }
            if (status == HttpStatusCode.NotFound) continue;
            if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                session.ReportStatus("SpotifyLyricsDenied");
                throw new HttpRequestException("Spotify lyrics access denied", null, status);
            }
            if (record is null) continue;
            if (LyricsService.FromRecord(record).Kind == LyricsKind.Synced)
            {
                trace?.Add("synced");
                session.ReportStatus("SpotifyLyricsWorking");
                return record;
            }
            plain ??= record;
        }
        trace?.Add(plain is null ? "no lyrics" : "plain only");
        session.ReportStatus("SpotifyLyricsUnavailable");
        return plain;
    }

    /// <summary>
    /// The album cover Spotify's search shows for the song, for when Windows got none from the
    /// desktop app: the one a lookup of the song already saw, else the captured search sent again
    /// with a still valid token. Never opens the web player for a cover. Null when there is none.
    /// </summary>
    public async Task<byte[]?> FindCoverAsync(TrackQuery query, CancellationToken ct)
    {
        var key = LyricsCache.KeyFor(query);
        if (!_covers.TryGetValue(key, out var url))
        {
            if (session.ValidCredentials is not { } credentials)
                return null;
            var (tracks, _) = await ReplaySearchAsync(query.Title + " " + query.Artist, credentials, null, ct).ConfigureAwait(false);
            url = (tracks ?? []).Where(t => TrackMatcher.IsMatch(query, t.Metadata))
                .OrderBy(t => TrackMatcher.DurationDistance(query, t.Metadata))
                .Select(t => t.CoverUrl).FirstOrDefault(u => u is not null);
            if (url is null)
                return null;
            RememberCover(key, url);
        }
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxCoverBytes)
                return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            return bytes.Length is > 0 and <= MaxCoverBytes ? bytes : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private void RememberCover(string key, string url)
    {
        if (_covers.Count >= MaxCovers)
            _covers.Clear();
        _covers[key] = url;
    }

    /// <summary>The lyrics for one track: the status, and the record when the request succeeded.</summary>
    private async Task<(HttpStatusCode Status, LrclibRecord? Record)> FetchLyricsAsync(SpotifyTrack track,
        SpotifyCredentials credentials, List<string>? trace, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        using var request = Request($"https://spclient.wg.spotify.com/color-lyrics/v2/track/{track.Id}?format=json&market=from_token", credentials);
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        trace?.Add($"lyrics {(int)response.StatusCode} {clock.ElapsedMilliseconds} ms");
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return (response.StatusCode, null);
        response.EnsureSuccessStatusCode();
        using var json = await ParseAsync(response, ct).ConfigureAwait(false);
        return (response.StatusCode, ReadLyrics(json.RootElement, track.Metadata));
    }

    /// <summary>
    /// The web player's last captured track search, with this search term: the tracks Spotify
    /// answered with, or why it did not answer. Both are null when there was nothing to send.
    /// </summary>
    private async Task<(IReadOnlyList<SpotifyTrack>? Tracks, HttpRequestException? Failure)> ReplaySearchAsync(
        string term, SpotifyCredentials credentials, List<string>? trace, CancellationToken ct)
    {
        if (session.SearchTemplate is not { } template || template == _staleTemplate)
            return (null, null);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ReplayTimeout);
        var clock = Stopwatch.StartNew();
        try
        {
            using var request = template.CreateRequest(term, credentials);
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                trace?.Add($"replay {(int)response.StatusCode} {clock.ElapsedMilliseconds} ms");
                // A rejected token (401) or a busy server (429, 5xx) says nothing about the query
                // itself; anything else means Spotify no longer takes this query.
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                    return (null, new HttpRequestException($"Spotify search answered {(int)response.StatusCode}", null, response.StatusCode));
                _staleTemplate = template;
                return (null, null);
            }
            using var json = await ParseAsync(response, timeout.Token).ConfigureAwait(false);
            var tracks = ReadSearch(json.RootElement);
            trace?.Add($"replay {tracks.Count} found {clock.ElapsedMilliseconds} ms");
            return (tracks, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            trace?.Add("replay timeout");
            return (null, new HttpRequestException("Spotify search timed out"));
        }
        catch (HttpRequestException ex)
        {
            trace?.Add($"replay {ex.GetType().Name}");
            return (null, ex);
        }
        catch (JsonException)
        {
            _staleTemplate = template;
            trace?.Add("replay bad json");
            return (null, null);
        }
    }

    private async Task<IReadOnlyList<SpotifyTrack>> PublicSearchAsync(string term, SpotifyCredentials credentials,
        List<string>? trace, CancellationToken ct)
    {
        if (_now() < _publicSearchAfter)
        {
            trace?.Add("api search skipped (429)");
            return [];
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(PublicSearchTimeout);
        var clock = Stopwatch.StartNew();
        try
        {
            using var request = Request("https://api.spotify.com/v1/search?type=track&limit=10&q=" + Uri.EscapeDataString(term), credentials);
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                _publicSearchAfter = _now() + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1));
            IReadOnlyList<SpotifyTrack> tracks = [];
            if (response.IsSuccessStatusCode)
            {
                using var json = await ParseAsync(response, timeout.Token).ConfigureAwait(false);
                tracks = ReadSearch(json.RootElement);
            }
            trace?.Add($"api search {(int)response.StatusCode}{RetryAfter(response)} {clock.ElapsedMilliseconds} ms");
            return tracks;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { trace?.Add("api search timeout"); }
        catch (HttpRequestException ex) { trace?.Add($"api search {ex.GetType().Name}"); }
        catch (JsonException) { trace?.Add("api search bad json"); }
        return [];
    }

    // Parsed from the stream with pooled buffers: a search response can be large enough for its
    // text to land on the large object heap, which only full collections reclaim.
    private static async Task<JsonDocument> ParseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(body, cancellationToken: ct).ConfigureAwait(false);
    }

    private static string RetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta is { } delay ? $" (retry after {delay.TotalSeconds:0} s)" : "";

    private static HttpRequestMessage Request(string url, SpotifyCredentials credentials)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.TryAddWithoutValidation("App-Platform", "WebPlayer");
        request.Headers.TryAddWithoutValidation("Origin", "https://open.spotify.com");
        if (!string.IsNullOrEmpty(credentials.ClientToken))
            request.Headers.TryAddWithoutValidation("Client-Token", credentials.ClientToken);
        return request;
    }

    /// <summary>Read WebView2 COM streams sequentially: some do not implement Length or Seek.</summary>
    public static Task<IReadOnlyList<SpotifyTrack>> ReadSearchAsync(Stream stream) => Task.Run(() =>
    {
        using var json = JsonDocument.Parse(ReadText(stream));
        return ReadSearch(json.RootElement);
    });

    /// <summary>
    /// The access token and its expiry from the web player's own token response
    /// (open.spotify.com/api/token), read like <see cref="ReadSearchAsync"/>; null when absent.
    /// </summary>
    public static Task<(string AccessToken, DateTimeOffset ExpiresAt)?> ReadTokenAsync(Stream stream) => Task.Run(() =>
    {
        try
        {
            using var json = JsonDocument.Parse(ReadText(stream));
            return ReadToken(json.RootElement);
        }
        catch (JsonException)
        {
            return ((string, DateTimeOffset)?)null;
        }
    });

    internal static (string AccessToken, DateTimeOffset ExpiresAt)? ReadToken(JsonElement root)
    {
        if (String(root, "accessToken") is not { Length: > 0 } token
            || !root.TryGetProperty("accessTokenExpirationTimestampMs", out var ms) || !ms.TryGetInt64(out var expires)
            || expires <= 0)
            return null;
        return (token, DateTimeOffset.FromUnixTimeMilliseconds(expires));
    }

    private static string ReadText(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
        var buffer = new char[4096];
        var result = new StringBuilder();
        int count;
        while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (result.Length + count > 4_000_000) throw new InvalidDataException("Response too large");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }

    public static IReadOnlyList<SpotifyTrack> ReadSearch(JsonElement root)
    {
        var found = new Dictionary<string, SpotifyTrack>();
        Visit(root);
        return found.Values.ToList();

        void Visit(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in node.EnumerateArray()) Visit(child);
                return;
            }
            if (node.ValueKind != JsonValueKind.Object) return;
            var uri = String(node, "uri");
            var id = uri?.StartsWith("spotify:track:", StringComparison.Ordinal) == true ? uri[14..] : null;
            var title = String(node, "name");
            if (id is { Length: 22 } && id.All(char.IsAsciiLetterOrDigit) && title is not null
                && node.TryGetProperty("artists", out var artists))
            {
                if (artists.ValueKind == JsonValueKind.Object && artists.TryGetProperty("items", out var items)) artists = items;
                var names = artists.ValueKind == JsonValueKind.Array ? artists.EnumerateArray()
                    .Select(a => String(a, "name") ?? (a.TryGetProperty("profile", out var p) ? String(p, "name") : null))
                    .Where(n => n is not null).ToArray() : [];
                double? seconds = null;
                if (node.TryGetProperty("duration_ms", out var ms) && ms.TryGetDouble(out var duration)) seconds = duration / 1000;
                else if (node.TryGetProperty("duration", out var d) && d.TryGetProperty("totalMilliseconds", out ms)
                         && ms.TryGetDouble(out duration)) seconds = duration / 1000;
                var hasAlbum = node.TryGetProperty("album", out var a) || node.TryGetProperty("albumOfTrack", out a);
                var album = hasAlbum ? String(a, "name") : null;
                if (names.Length > 0 && seconds is > 0)
                    found[id] = new SpotifyTrack(id, new LrclibRecord(0, title, string.Join(", ", names), album, seconds, false, null, null),
                        hasAlbum ? CoverUrl(a) : null);
            }
            foreach (var property in node.EnumerateObject()) Visit(property.Value);
        }
    }

    /// <summary>
    /// The cover nearest the 256 px the overlay decodes (the smallest at least that wide, else the
    /// largest): the web player's "coverArt.sources" or the public API's "images". Only Spotify's
    /// image CDN over HTTPS is accepted, as the address comes from a response.
    /// </summary>
    private static string? CoverUrl(JsonElement album)
    {
        if (album.ValueKind != JsonValueKind.Object)
            return null;
        var images = album.TryGetProperty("coverArt", out var art) && art.ValueKind == JsonValueKind.Object
            && art.TryGetProperty("sources", out var sources) ? sources
            : album.TryGetProperty("images", out var list) ? list : default;
        if (images.ValueKind != JsonValueKind.Array)
            return null;
        var covers = images.EnumerateArray()
            .Select(i => (Url: String(i, "url"), Width: i.ValueKind == JsonValueKind.Object && i.TryGetProperty("width", out var w)
                && w.ValueKind == JsonValueKind.Number && w.TryGetInt32(out var px) ? px : 0))
            .Where(c => IsCoverUrl(c.Url))
            .ToList();
        if (covers.Count == 0)
            return null;
        var fits = covers.Where(c => c.Width >= 256).OrderBy(c => c.Width).ToList();
        return (fits.Count > 0 ? fits[0] : covers.MaxBy(c => c.Width)).Url;
    }

    internal static bool IsCoverUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.EndsWith(".scdn.co", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".spotifycdn.com", StringComparison.OrdinalIgnoreCase));

    internal static LrclibRecord? ReadLyrics(JsonElement root, LrclibRecord metadata)
    {
        if (!root.TryGetProperty("lyrics", out var lyrics) || !lyrics.TryGetProperty("lines", out var lines)
            || lines.ValueKind != JsonValueKind.Array) return null;
        var synced = String(lyrics, "syncType") == "LINE_SYNCED";
        var lrc = new StringBuilder();
        var plain = new List<string>();
        foreach (var line in lines.EnumerateArray())
        {
            var words = String(line, "words");
            if (words is null) continue;
            words = words.Replace('\r', ' ').Replace('\n', ' ');
            plain.Add(words);
            if (!synced || !line.TryGetProperty("startTimeMs", out var time)
                || !long.TryParse(time.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var ms)
                || ms < 0 || ms >= 60_000_000) continue;
            lrc.Append(CultureInfo.InvariantCulture, $"[{ms / 60000:00}:{ms / 1000 % 60:00}.{ms % 1000:000}]{words}\n");
        }
        if (plain.All(string.IsNullOrWhiteSpace)) return null;
        return metadata with { PlainLyrics = string.Join('\n', plain), SyncedLyrics = lrc.Length > 0 ? lrc.ToString() : null };
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
