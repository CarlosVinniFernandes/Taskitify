using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Taskitify.Core.Lyrics;

public sealed record SpotifyCredentials(string AccessToken, string? ClientToken);
public sealed record SpotifyTrack(string Id, LrclibRecord Metadata);

public interface ISpotifyLyricsSession
{
    Task<SpotifyCredentials?> GetCredentialsAsync(CancellationToken ct);
    Task<IReadOnlyList<SpotifyTrack>> SearchAsync(TrackQuery query, CancellationToken ct);
    void ReportStatus(string key);
}

/// <summary>Optional, unofficial Spotify web-player source. No cookies or passwords leave the browser profile.</summary>
public sealed class SpotifyLyricsProvider(HttpClient http, ISpotifyLyricsSession session) : ILyricsProvider
{
    public string Name => "Spotify";
    public bool IsLocal => false;

    public async Task<LrclibRecord?> FindAsync(TrackQuery query, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var token = timeout.Token;
        var credentials = await session.GetCredentialsAsync(token).ConfigureAwait(false);
        if (credentials is null) return null;

        // The public search endpoint can reject web-player credentials. In that case use
        // the web player's own search responses, without hardcoding its changing GraphQL hashes.
        IReadOnlyList<SpotifyTrack> tracks = [];
        using var searchTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        searchTimeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var request = Request("https://api.spotify.com/v1/search?type=track&limit=10&q=" +
                Uri.EscapeDataString(query.Title + " " + query.Artist), credentials);
            using var response = await http.SendAsync(request, searchTimeout.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(searchTimeout.Token).ConfigureAwait(false));
                tracks = ReadSearch(json.RootElement);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        catch (HttpRequestException) { }
        catch (JsonException) { }
        if (!tracks.Any(t => TrackMatcher.IsMatch(query, t.Metadata)))
        {
            tracks = await session.SearchAsync(query, token).ConfigureAwait(false);
            credentials = await session.GetCredentialsAsync(token).ConfigureAwait(false);
            if (credentials is null) return null;
        }

        LrclibRecord? plain = null;
        foreach (var track in tracks.Where(t => TrackMatcher.IsMatch(query, t.Metadata))
                     .OrderBy(t => TrackMatcher.DurationDistance(query, t.Metadata)).Take(3))
        {
            using var request = Request($"https://spclient.wg.spotify.com/color-lyrics/v2/track/{track.Id}?format=json&market=from_token", credentials);
            using var response = await http.SendAsync(request, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) continue;
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                session.ReportStatus("SpotifyLyricsDenied");
                throw new HttpRequestException("Spotify lyrics access denied", null, response.StatusCode);
            }
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
            var record = ReadLyrics(json.RootElement, track.Metadata);
            if (record is null) continue;
            if (LyricsService.FromRecord(record).Kind == LyricsKind.Synced)
            {
                session.ReportStatus("SpotifyLyricsWorking");
                return record;
            }
            plain ??= record;
        }
        session.ReportStatus("SpotifyLyricsUnavailable");
        return plain;
    }

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
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
        var buffer = new char[4096];
        var result = new StringBuilder();
        int count;
        while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (result.Length + count > 4_000_000) throw new InvalidDataException("Search response too large");
            result.Append(buffer, 0, count);
        }
        using var json = JsonDocument.Parse(result.ToString());
        return ReadSearch(json.RootElement);
    });

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
                var album = node.TryGetProperty("album", out var a) ? String(a, "name") :
                    node.TryGetProperty("albumOfTrack", out a) ? String(a, "name") : null;
                if (names.Length > 0 && seconds is > 0)
                    found[id] = new SpotifyTrack(id, new LrclibRecord(0, title, string.Join(", ", names), album, seconds, false, null, null));
            }
            foreach (var property in node.EnumerateObject()) Visit(property.Value);
        }
    }

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
