using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Taskitify.Core.Lyrics;
using Taskitify.Models;

namespace Taskitify.Tests;

public sealed class SpotifyLibraryTests
{
    private const string Id = "1234567890123456789012";
    private static readonly SpotifyCredentials Credentials = new("test-token", "test-client");
    private const string NewHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private sealed class Handler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Url, JsonObject? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.RequestUri!.ToString(), body is null ? null : JsonNode.Parse(body) as JsonObject));
            return respond(request, body);
        }
    }

    private static HttpResponseMessage Text(string text, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(text) };

    private const string Saved = """{"data":{"lookup":[{"__typename":"TrackResponseWrapper","data":{"__typename":"Track","saved":true}}]}}""";

    [Fact]
    public async Task Checks_the_track_with_the_web_players_query_and_session_headers()
    {
        using var handler = new Handler((request, _) =>
        {
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            Assert.Equal("test-client", Assert.Single(request.Headers.GetValues("Client-Token")));
            Assert.Equal("WebPlayer", Assert.Single(request.Headers.GetValues("App-Platform")));
            return Text(Saved);
        });
        using var http = new HttpClient(handler);
        Assert.True(await new SpotifyLibraryClient(http).IsSavedAsync(Id, Credentials, default));

        var (url, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://api-partner.spotify.com/pathfinder/v2/query", url);
        Assert.Equal("areEntitiesInLibrary", body!["operationName"]!.GetValue<string>());
        Assert.Equal($"spotify:track:{Id}", body["variables"]!["uris"]![0]!.GetValue<string>());
        Assert.Equal(64, body["extensions"]!["persistedQuery"]!["sha256Hash"]!.GetValue<string>().Length);
    }

    [Theory]
    [InlineData(true, "addToLibrary")]
    [InlineData(false, "removeFromLibrary")]
    public async Task Likes_and_unlikes_by_track_uri(bool saved, string operation)
    {
        using var handler = new Handler((_, _) => Text("""{"data":{"addLibraryItems":{}}}"""));
        using var http = new HttpClient(handler);
        await new SpotifyLibraryClient(http).SetSavedAsync(Id, saved, Credentials, default);

        var body = Assert.Single(handler.Requests).Body!;
        Assert.Equal(operation, body["operationName"]!.GetValue<string>());
        Assert.Equal($"spotify:track:{Id}", body["variables"]!["libraryItemUris"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task A_retired_query_is_looked_up_in_the_web_player_script_and_sent_again()
    {
        const string page = """<html><script src="https://open.spotifycdn.com/cdn/build/web-player/web-player.abc.js"></script></html>""";
        var script = $$"""let a=new X.l("areEntitiesInLibrary","query","{{NewHash}}",null),b=new X.l("addToLibrary","mutation","{{NewHash}}",null);""";
        using var handler = new Handler((request, body) =>
        {
            if (request.RequestUri!.Host == "open.spotify.com") return Text(page);
            if (request.RequestUri.Host == "open.spotifycdn.com") return Text(script);
            return body!.Contains(NewHash)
                ? Text(Saved)
                : Text("""{"errors":[{"message":"PersistedQueryNotFound"}]}""", HttpStatusCode.BadRequest);
        });
        using var http = new HttpClient(handler);
        Assert.True(await new SpotifyLibraryClient(http).IsSavedAsync(Id, Credentials, default));
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal(NewHash, handler.Requests[^1].Body!["extensions"]!["persistedQuery"]!["sha256Hash"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_rejected_token_is_reported_as_unauthorized()
    {
        using var http = new HttpClient(new Handler((_, _) => Text("", HttpStatusCode.Unauthorized)));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new SpotifyLibraryClient(http).IsSavedAsync(Id, Credentials, default));
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task A_learned_hash_replaces_the_default()
    {
        using var handler = new Handler((_, _) => Text(Saved));
        using var http = new HttpClient(handler);
        var client = new SpotifyLibraryClient(http);
        client.Learn("areEntitiesInLibrary", NewHash);
        client.Learn("areEntitiesInLibrary", "not-a-hash");
        client.Learn("somethingElse", NewHash);
        await client.IsSavedAsync(Id, Credentials, default);
        Assert.Equal(NewHash, Assert.Single(handler.Requests).Body!["extensions"]!["persistedQuery"]!["sha256Hash"]!.GetValue<string>());
    }

    [Fact]
    public void Reads_the_saved_state_and_nothing_else()
    {
        Assert.True(SpotifyLibraryClient.ReadSaved(JsonDocument.Parse(Saved).RootElement));
        Assert.False(SpotifyLibraryClient.ReadSaved(JsonDocument.Parse(Saved.Replace("true", "false")).RootElement));
        Assert.Null(SpotifyLibraryClient.ReadSaved(JsonDocument.Parse("""{"data":{"lookup":[]}}""").RootElement));
        Assert.Null(SpotifyLibraryClient.ReadSaved(JsonDocument.Parse("""{"errors":[]}""").RootElement));
    }

    [Fact]
    public void Only_spotify_scripts_are_read_web_player_first()
    {
        const string html = """
            <script src="https://evil.example/web-player.js"></script>
            <script src="https://open.spotifycdn.com/cdn/build/web-player/vendor~web-player.1.js"></script>
            <script src="https://open.spotifycdn.com/cdn/build/web-player/web-player.2.js"></script>
            """;
        Assert.Equal(
            ["https://open.spotifycdn.com/cdn/build/web-player/web-player.2.js", "https://open.spotifycdn.com/cdn/build/web-player/vendor~web-player.1.js"],
            SpotifyLibraryClient.PlayerScripts(html));
    }

    [Fact]
    public async Task Invalid_track_ids_are_never_sent()
    {
        using var handler = new Handler((_, _) => Text(Saved));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => new SpotifyLibraryClient(http).IsSavedAsync("spotify:track:x\"", Credentials, default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Older_settings_get_the_like_button_once_and_keep_the_users_choice_after()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("""{"Bar":{"ShowCover":true},"MiniPlayer":{"ShowCover":true}}""")!;
        old.MigrateLegacy();
        Assert.True(old.Bar.ShowLike);
        Assert.True(old.MiniPlayer.ShowLike);
        Assert.False(old.MinimizedBar.ShowLike);

        old.Bar.ShowLike = false;
        var reloaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(old))!;
        reloaded.MigrateLegacy();
        Assert.False(reloaded.Bar.ShowLike);
    }
}
