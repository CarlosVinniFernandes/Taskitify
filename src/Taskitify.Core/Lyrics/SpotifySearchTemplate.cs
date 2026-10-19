using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Taskitify.Core.Lyrics;

/// <summary>
/// The web player's own track search (a persisted GraphQL query), captured from its traffic so it
/// can be sent again for other songs without loading the web player. Nothing about the query is
/// hardcoded: when Spotify ships a new one the replay fails, the web player searches once more
/// and its request becomes the new template. Holds no credentials; they are added per request.
/// </summary>
public sealed class SpotifySearchTemplate
{
    // Credentials, and headers HttpClient sets itself. Accept-Encoding too: the browser accepts
    // br/zstd, which this HttpClient does not decompress.
    private static readonly HashSet<string> NotCopied = new(StringComparer.OrdinalIgnoreCase)
        { "Authorization", "Client-Token", "Cookie", "Content-Length", "Content-Type", "Host", "Accept-Encoding" };

    private readonly Uri _endpoint;
    private readonly JsonObject _body;
    private readonly KeyValuePair<string, string>[] _headers;

    private SpotifySearchTemplate(Uri endpoint, JsonObject body, KeyValuePair<string, string>[] headers)
    {
        _endpoint = endpoint;
        _body = body;
        _headers = headers;
    }

    public string OperationName => _body["operationName"]?.GetValue<string>() ?? "";

    /// <summary>Identifies the query version; changes when Spotify ships a new web player.</summary>
    public string? QueryHash => _body["extensions"]?["persistedQuery"]?["sha256Hash"] is JsonValue hash
        && hash.GetValueKind() == JsonValueKind.String ? hash.GetValue<string>() : null;

    /// <summary>A template from one of the web player's requests, or null when it is not a track search.</summary>
    public static SpotifySearchTemplate? TryCapture(string method, string uri, string? body, IEnumerable<KeyValuePair<string, string>> headers)
    {
        if (method != "POST" || body is null || !Uri.TryCreate(uri, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != "https" || endpoint.Host != "api-partner.spotify.com")
            return null;
        try
        {
            if (JsonNode.Parse(body) is not JsonObject json
                || json["operationName"] is not JsonValue name || name.GetValueKind() != JsonValueKind.String
                || !name.GetValue<string>().StartsWith("search", StringComparison.Ordinal)
                || json["variables"] is not JsonObject variables || variables["searchTerm"] is not JsonValue)
                return null;
            return new SpotifySearchTemplate(endpoint, json,
                headers.Where(h => !NotCopied.Contains(h.Key)).ToArray());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The captured search with another search term and the current credentials.</summary>
    public HttpRequestMessage CreateRequest(string searchTerm, SpotifyCredentials credentials)
    {
        var body = (JsonObject)_body.DeepClone();
        body["variables"]!["searchTerm"] = searchTerm;
        var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        foreach (var (name, value) in _headers)
            request.Headers.TryAddWithoutValidation(name, value);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        if (!string.IsNullOrEmpty(credentials.ClientToken))
            request.Headers.TryAddWithoutValidation("Client-Token", credentials.ClientToken);
        return request;
    }
}
