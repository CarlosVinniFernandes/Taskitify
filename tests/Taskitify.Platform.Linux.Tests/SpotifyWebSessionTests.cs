using Taskitify.Core.Lyrics;
using Taskitify.Platform.Linux.Spotify;

namespace Taskitify.Platform.Linux.Tests;

public class SpotifyWebSessionTests
{
    private sealed class FakePlayer : IWebPlayer
    {
        public event Action<WebRequest>? Request;
        public event Action<bool>? Loaded;
        public List<string> Navigations { get; } = [];
        public bool SignedIn { get; set; } = true;
        public bool Closed { get; private set; }
        public bool Cleared { get; private set; }
        public bool CannotStart { get; set; }

        /// <summary>What the page does after a navigation (the real one answers from another thread).</summary>
        public Action<string>? OnNavigate { get; set; }

        public void Navigate(string url)
        {
            if (CannotStart)
                throw new InvalidOperationException("no python3");
            Navigations.Add(url);
            Task.Run(() => OnNavigate?.Invoke(url));
        }

        public void ShowLogin(string url) => Navigate(url);
        public Task<bool> HasSessionCookieAsync() => Task.FromResult(SignedIn);
        public Task ClearAsync() { Cleared = true; return Task.CompletedTask; }
        public void Close() => Closed = true;
        public void Dispose() { }
        public void Raise(WebRequest r) => Request?.Invoke(r);
        public void RaiseLoaded(bool ok) => Loaded?.Invoke(ok);
    }

    private static readonly TrackQuery Query = new("Song", "Artist", "Album", TimeSpan.FromSeconds(180));

    private const string Search = """
        {"data":{"searchV2":{"tracksV2":{"items":[{"item":{"data":{
        "uri":"spotify:track:1234567890123456789012","name":"Song",
        "artists":{"items":[{"profile":{"name":"Artist"}}]},
        "albumOfTrack":{"name":"Album"},"duration":{"totalMilliseconds":180000}}}}]}}}}
        """;

    private const string SearchBody =
        """{"variables":{"searchTerm":"older song","limit":20},"operationName":"searchTracks","extensions":{"persistedQuery":{"version":1,"sha256Hash":"abc"}}}""";

    private static WebRequest Token(string token, int minutes = 60) => new("GET", "https://open.spotify.com/api/token?reason=init",
        new Dictionary<string, string>(), null, 200,
        $$"""{"accessToken":"{{token}}","accessTokenExpirationTimestampMs":{{DateTimeOffset.UtcNow.AddMinutes(minutes).ToUnixTimeMilliseconds()}}}""");

    private static WebRequest Api(string token, string? response = null, string method = "POST", string? body = SearchBody) => new(method,
        "https://api-partner.spotify.com/pathfinder/v2/query",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["authorization"] = "Bearer " + token, ["client-token"] = "ct-" + token, ["app-platform"] = "WebPlayer" },
        body, 200, response);

    private static SpotifyWebSession NewSession(FakePlayer player) => new(player)
    {
        Enabled = true,
        TokenWait = TimeSpan.FromSeconds(2),
        SearchCaptureWait = TimeSpan.FromMilliseconds(300),
        SearchWait = TimeSpan.FromSeconds(2),
        CloseAfter = TimeSpan.FromMilliseconds(100),
    };

    [Fact]
    public async Task Takes_the_token_and_client_token_from_the_web_players_own_traffic()
    {
        var player = new FakePlayer();
        player.OnNavigate = _ => { player.Raise(Token("t1")); player.Raise(Api("t1")); };
        using var session = NewSession(player);

        var credentials = await session.GetCredentialsAsync(null, default);

        Assert.Equal(new SpotifyCredentials("t1", "ct-t1"), credentials);
        Assert.Equal("SpotifyLyricsReady", session.StatusKey);
        Assert.Equal(credentials, session.ValidCredentials);
        Assert.Equal(["https://open.spotify.com/"], player.Navigations);
    }

    [Fact]
    public async Task A_valid_token_is_reused_without_loading_the_page_again()
    {
        var player = new FakePlayer();
        player.OnNavigate = _ => { player.Raise(Token("t1")); player.Raise(Api("t1")); };
        using var session = NewSession(player);
        await session.GetCredentialsAsync(null, default);

        await session.GetCredentialsAsync(null, default);

        Assert.Single(player.Navigations);
    }

    [Fact]
    public async Task The_search_the_page_made_is_captured_so_it_can_be_replayed()
    {
        var player = new FakePlayer();
        player.OnNavigate = _ => { player.Raise(Token("t1")); player.Raise(Api("t1")); };
        using var session = NewSession(player);

        await session.GetCredentialsAsync(Query, default);

        Assert.Contains("/search/Song%20Artist/tracks", player.Navigations[0]);
        Assert.NotNull(session.SearchTemplate);
        Assert.Equal("searchTracks", session.SearchTemplate!.OperationName);
    }

    [Fact]
    public async Task Without_a_token_and_without_the_login_cookie_it_asks_for_sign_in_and_then_stops_asking()
    {
        var player = new FakePlayer { SignedIn = false };
        using var session = NewSession(player);

        var first = await session.GetCredentialsAsync(null, default);
        var second = await session.GetCredentialsAsync(null, default);

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal("SpotifyLyricsLogin", session.StatusKey);
        Assert.Single(player.Navigations);
    }

    [Fact]
    public async Task A_slow_page_with_the_login_cookie_is_a_failure_to_retry_not_a_sign_out()
    {
        var player = new FakePlayer { SignedIn = true };
        using var session = new SpotifyWebSession(player) { Enabled = true, TokenWait = TimeSpan.FromMilliseconds(200) };

        await Assert.ThrowsAsync<HttpRequestException>(() => session.GetCredentialsAsync(null, default));

        Assert.NotEqual("SpotifyLyricsLogin", session.StatusKey);
    }

    [Fact]
    public async Task A_page_that_does_not_load_is_a_failure_and_pauses_new_attempts()
    {
        var player = new FakePlayer();
        player.OnNavigate = _ => player.RaiseLoaded(false);
        using var session = NewSession(player);

        await Assert.ThrowsAsync<HttpRequestException>(() => session.GetCredentialsAsync(null, default));
        await Assert.ThrowsAsync<HttpRequestException>(() => session.GetCredentialsAsync(null, default));

        Assert.Single(player.Navigations);
    }

    [Fact]
    public async Task A_rejected_token_is_replaced_by_a_new_page_load()
    {
        var player = new FakePlayer();
        var n = 0;
        player.OnNavigate = _ => { var t = "t" + Interlocked.Increment(ref n); player.Raise(Token(t)); player.Raise(Api(t)); };
        using var session = NewSession(player);
        var old = (await session.GetCredentialsAsync(null, default))!;

        var renewed = await session.RenewCredentialsAsync(old, default);

        Assert.Equal("t2", renewed!.AccessToken);
        Assert.Equal(2, player.Navigations.Count);
    }

    [Fact]
    public async Task Search_returns_the_matching_tracks_from_the_page_and_ignores_others()
    {
        var player = new FakePlayer();
        player.OnNavigate = _ => player.Raise(Api("t1", Search, body: null));
        using var session = NewSession(player);

        var tracks = await session.SearchAsync(Query, default);

        Assert.Equal("1234567890123456789012", Assert.Single(tracks).Id);

        player.OnNavigate = _ => player.Raise(Api("t1", Search.Replace("\"Song\"", "\"Other\""), body: null));
        Assert.Empty(await session.SearchAsync(Query, default));
    }

    [Fact]
    public async Task Search_that_the_page_answers_with_its_own_query_is_done_and_returns_nothing_to_match()
    {
        var player = new FakePlayer();
        player.OnNavigate = _ => player.Raise(Api("t1"));
        using var session = NewSession(player);

        Assert.Empty(await session.SearchAsync(Query, default));
        Assert.NotNull(session.SearchTemplate);
    }

    [Fact]
    public async Task The_browser_is_closed_after_use_and_when_the_source_is_turned_off()
    {
        var player = new FakePlayer();
        player.OnNavigate = _ => { player.Raise(Token("t1")); player.Raise(Api("t1")); };
        using var session = NewSession(player);
        await session.GetCredentialsAsync(null, default);
        await Task.Delay(400);
        Assert.True(player.Closed);

        await session.DisconnectAsync();

        Assert.True(player.Cleared);
        Assert.Null(session.ValidCredentials);
        Assert.Equal("SpotifyLyricsDisconnected", session.StatusKey);
        Assert.Null(await session.GetCredentialsAsync(null, default));
    }

    [Fact]
    public async Task A_browser_that_cannot_start_is_reported_and_the_lookup_falls_back()
    {
        var player = new FakePlayer { CannotStart = true };
        using var session = NewSession(player);

        Assert.Null(await session.GetCredentialsAsync(null, default));

        Assert.Equal("SpotifyLyricsBrowserError", session.StatusKey);
    }

    [Fact]
    public async Task Denied_by_spotify_drops_the_token()
    {
        var player = new FakePlayer();
        player.OnNavigate = _ => { player.Raise(Token("t1")); player.Raise(Api("t1")); };
        using var session = NewSession(player);
        Assert.NotNull(await session.GetCredentialsAsync(null, default));
        Assert.NotNull(session.ValidCredentials);

        session.ReportStatus("SpotifyLyricsDenied");

        Assert.Null(session.ValidCredentials);
        Assert.Equal("SpotifyLyricsDenied", session.StatusKey);
    }
}
