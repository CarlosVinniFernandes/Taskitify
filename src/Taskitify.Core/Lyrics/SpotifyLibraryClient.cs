using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Taskitify.Core.Lyrics;

/// <summary>
/// Liked Songs through the web player's own GraphQL queries ("areEntitiesInLibrary",
/// "addToLibrary", "removeFromLibrary"). The public Web API answers web-player tokens with 429
/// nearly every time, so it is not used. The queries are persisted: Spotify knows them by hash.
/// The hashes the web player used when this was written are the defaults; when Spotify retires
/// one, the current ones are read from the web player's script and the request is sent again.
/// </summary>
public sealed partial class SpotifyLibraryClient(HttpClient http, Action<string>? log = null)
{
    private const string Endpoint = "https://api-partner.spotify.com/pathfinder/v2/query";
    private const string PlayerPage = "https://open.spotify.com/";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    internal const string ContainsOperation = "areEntitiesInLibrary";
    internal const string AddOperation = "addToLibrary";
    internal const string RemoveOperation = "removeFromLibrary";

    private readonly Dictionary<string, string> _hashes = new()
    {
        [ContainsOperation] = "134337999233cc6fdd6b1e6dbf94841409f04a946c5c7b744b09ba0dfe5a85ed",
        [AddOperation] = "896ebcb47815681340860d121cb5d494e157e2a78d3950385cd54e0393c67148",
        [RemoveOperation] = "896ebcb47815681340860d121cb5d494e157e2a78d3950385cd54e0393c67148",
    };
    private readonly SemaphoreSlim _refreshLock = new(1);
    private DateTimeOffset _refreshedAt = DateTimeOffset.MinValue;

    /// <summary>A hash the web player was seen using (its own traffic), newer than any default.</summary>
    public void Learn(string operation, string hash)
    {
        if (operation is ContainsOperation or AddOperation or RemoveOperation && IsHash(hash))
            lock (_hashes) _hashes[operation] = hash;
    }

    /// <summary>Whether the track is in Liked Songs.</summary>
    public async Task<bool> IsSavedAsync(string trackId, SpotifyCredentials credentials, CancellationToken ct) =>
        (await SavedAsync([trackId], credentials, ct).ConfigureAwait(false))[0];

    /// <summary>Which of the tracks are in Liked Songs, in their order (one request).</summary>
    public async Task<IReadOnlyList<bool>> SavedAsync(IReadOnlyList<string> trackIds, SpotifyCredentials credentials, CancellationToken ct)
    {
        using var json = await SendAsync(ContainsOperation, new JsonObject { ["uris"] = Uris(trackIds) }, credentials, ct).ConfigureAwait(false);
        var saved = ReadAllSaved(json.RootElement);
        return saved.Count == trackIds.Count ? saved : throw new HttpRequestException("Spotify gave no library state");
    }

    /// <summary>Add the track to Liked Songs, or remove it.</summary>
    public Task SetSavedAsync(string trackId, bool saved, SpotifyCredentials credentials, CancellationToken ct) =>
        SetSavedAsync([trackId], saved, credentials, ct);

    /// <summary>Add the tracks to Liked Songs, or remove them (one request).</summary>
    public async Task SetSavedAsync(IReadOnlyList<string> trackIds, bool saved, SpotifyCredentials credentials, CancellationToken ct)
    {
        var variables = new JsonObject { ["libraryItemUris"] = Uris(trackIds) };
        if (saved) variables["interactionId"] = null;
        using var _ = await SendAsync(saved ? AddOperation : RemoveOperation, variables, credentials, ct).ConfigureAwait(false);
    }

    private static JsonArray Uris(IReadOnlyList<string> trackIds)
    {
        if (trackIds.Count == 0 || !trackIds.All(IsTrackId))
            throw new ArgumentException("Not a Spotify track id", nameof(trackIds));
        return new JsonArray(trackIds.Select(id => (JsonNode?)JsonValue.Create($"spotify:track:{id}")).ToArray());
    }

    private async Task<JsonDocument> SendAsync(string operation, JsonObject variables, SpotifyCredentials credentials, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            string hash;
            lock (_hashes) hash = _hashes[operation];
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RequestTimeout);
            using var request = Request(operation, hash, variables, credentials);
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            log?.Invoke($"Spotify {operation}: {(int)response.StatusCode}");
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new HttpRequestException($"Spotify {operation} denied", null, response.StatusCode);
            // A retired query: Spotify answers with an error naming it (200 or 400, depending on the server).
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            if (IsUnknownQuery(body) && attempt == 0 && await RefreshHashesAsync(ct).ConfigureAwait(false))
                continue;
            response.EnsureSuccessStatusCode();
            var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                json.Dispose();
                throw new HttpRequestException($"Spotify {operation} failed");
            }
            return json;
        }
    }

    private static HttpRequestMessage Request(string operation, string hash, JsonObject variables, SpotifyCredentials credentials)
    {
        var body = new JsonObject
        {
            ["variables"] = variables.DeepClone(),
            ["operationName"] = operation,
            ["extensions"] = new JsonObject { ["persistedQuery"] = new JsonObject { ["version"] = 1, ["sha256Hash"] = hash } },
        };
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.TryAddWithoutValidation("App-Platform", "WebPlayer");
        request.Headers.TryAddWithoutValidation("Origin", "https://open.spotify.com");
        if (!string.IsNullOrEmpty(credentials.ClientToken))
            request.Headers.TryAddWithoutValidation("Client-Token", credentials.ClientToken);
        return request;
    }

    internal static bool IsUnknownQuery(string body) =>
        body.Contains("PersistedQueryNotFound", StringComparison.OrdinalIgnoreCase)
        || body.Contains("PERSISTED_QUERY_NOT_FOUND", StringComparison.OrdinalIgnoreCase);

    /// <summary>The library state of the first entity in an "areEntitiesInLibrary" answer.</summary>
    internal static bool? ReadSaved(JsonElement root) => ReadAllSaved(root) is { Count: > 0 } all ? all[0] : null;

    /// <summary>The library state of every entity in an "areEntitiesInLibrary" answer, in order; empty when any is missing.</summary>
    internal static IReadOnlyList<bool> ReadAllSaved(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("lookup", out var lookup)
            || lookup.ValueKind != JsonValueKind.Array)
            return [];
        var result = new List<bool>();
        foreach (var item in lookup.EnumerateArray())
        {
            if (!item.TryGetProperty("data", out var entity) || entity.ValueKind != JsonValueKind.Object
                || !entity.TryGetProperty("saved", out var saved) || saved.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return [];
            result.Add(saved.GetBoolean());
        }
        return result;
    }

    /// <summary>
    /// The current hashes, from the web player's script (no credentials needed). At most once every
    /// few minutes, so a Spotify change does not turn every click into two downloads.
    /// </summary>
    private async Task<bool> RefreshHashesAsync(CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (DateTimeOffset.UtcNow - _refreshedAt < TimeSpan.FromMinutes(5)) return false;
            _refreshedAt = DateTimeOffset.UtcNow;
            var page = await http.GetStringAsync(PlayerPage, ct).ConfigureAwait(false);
            var found = 0;
            foreach (var script in PlayerScripts(page))
            {
                var js = await http.GetStringAsync(script, ct).ConfigureAwait(false);
                foreach (var (operation, hash) in ExtractHashes(js))
                {
                    Learn(operation, hash);
                    found++;
                }
                if (found > 0) break;
            }
            log?.Invoke($"Spotify library queries refreshed: {found} found");
            return found > 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log?.Invoke($"Spotify library queries refresh failed: {ex.GetType().Name}");
            return false;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>The web player's main script(s) referenced by its page; only Spotify's CDN.</summary>
    internal static IEnumerable<string> PlayerScripts(string html) => ScriptPattern().Matches(html)
        .Select(m => m.Groups[1].Value)
        .Where(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && (uri.Host.EndsWith(".spotifycdn.com", StringComparison.OrdinalIgnoreCase) || uri.Host == "open.spotify.com"))
        .OrderBy(url => url.Contains("/web-player.", StringComparison.Ordinal) ? 0 : 1)
        .Distinct()
        .Take(4);

    /// <summary>The library operations' hashes declared in a web player script.</summary>
    internal static IEnumerable<(string Operation, string Hash)> ExtractHashes(string js) => OperationPattern().Matches(js)
        .Select(m => (m.Groups[1].Value, m.Groups[2].Value));

    private static bool IsHash(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigitLower);

    public static bool IsTrackId(string? id) => id is { Length: 22 } && id.All(char.IsAsciiLetterOrDigit);

    [GeneratedRegex("<script[^>]+src=\"([^\"]+\\.js)\"")]
    private static partial Regex ScriptPattern();

    [GeneratedRegex("\"(areEntitiesInLibrary|addToLibrary|removeFromLibrary)\",\"(?:query|mutation)\",\"([0-9a-f]{64})\"")]
    private static partial Regex OperationPattern();
}
