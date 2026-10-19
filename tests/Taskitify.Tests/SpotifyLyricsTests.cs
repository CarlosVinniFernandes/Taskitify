using System.Net;
using System.Text.Json;
using Taskitify.Core.Lyrics;

namespace Taskitify.Tests;

public sealed class SpotifyLyricsTests
{
    private const string Id = "1234567890123456789012";
    private static readonly TrackQuery Query = new("Song", "Artist", "Album", TimeSpan.FromSeconds(180));
    private static readonly LrclibRecord Metadata = new(0, "Song", "Artist", "Album", 180, false, null, null);
    private const string Search = """
        {"tracks":{"items":[{"uri":"spotify:track:1234567890123456789012","name":"Song",
        "artists":[{"name":"Artist"}],"album":{"name":"Album"},"duration_ms":180000}]}}
        """;
    private const string Lyrics = """
        {"lyrics":{"syncType":"LINE_SYNCED","lines":[
        {"startTimeMs":"1234","words":"First"},{"startTimeMs":"60345","words":"Second"}]}}
        """;

    private sealed class Session : ISpotifyLyricsSession
    {
        public SpotifyCredentials? Credentials = new("test-token", "test-client");
        public string? Status;
        public int Searches;
        public IReadOnlyList<SpotifyTrack> Tracks = [new(Id, Metadata)];
        public SpotifySearchTemplate? SearchTemplate { get; set; }
        public Task<SpotifyCredentials?> GetCredentialsAsync(TrackQuery? searchFor, CancellationToken ct) => Task.FromResult(Credentials);
        public int Renewals;
        public Task<SpotifyCredentials?> RenewCredentialsAsync(SpotifyCredentials rejected, CancellationToken ct)
        {
            Renewals++;
            Credentials = new("renewed-token", "renewed-client");
            return Task.FromResult<SpotifyCredentials?>(Credentials);
        }
        public SpotifySearchTemplate? CapturedBySearch;
        public Task<IReadOnlyList<SpotifyTrack>> SearchAsync(TrackQuery query, CancellationToken ct)
        {
            Searches++;
            SearchTemplate = CapturedBySearch ?? SearchTemplate;
            return Task.FromResult(Tracks);
        }
        public void ReportStatus(string key) => Status = key;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; return Task.FromResult(respond(request)); }
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private sealed class BrowserStream(byte[] data) : MemoryStream(data)
    {
        public override long Length => throw new NotImplementedException();
        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();
    }

    [Fact]
    public async Task Browser_response_does_not_require_length_or_seeking()
    {
        using var stream = new BrowserStream(System.Text.Encoding.UTF8.GetBytes(Search));
        var track = Assert.Single(await SpotifyLyricsProvider.ReadSearchAsync(stream));
        Assert.Equal(Id, track.Id);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task Searches_and_fetches_matching_lyrics_with_session_headers()
    {
        var session = new Session();
        using var handler = new Handler(request =>
        {
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            Assert.Equal("WebPlayer", Assert.Single(request.Headers.GetValues("App-Platform")));
            Assert.Equal("test-client", Assert.Single(request.Headers.GetValues("Client-Token")));
            Assert.False(request.Headers.Contains("Cookie"));
            if (request.RequestUri!.Host == "api.spotify.com") return Json(Search);
            Assert.Equal($"/color-lyrics/v2/track/{Id}", request.RequestUri.AbsolutePath);
            return Json(Lyrics);
        });
        using var http = new HttpClient(handler);
        var result = LyricsService.FromRecord(await new SpotifyLyricsProvider(http, session).FindAsync(Query, default));
        Assert.Equal(LyricsKind.Synced, result.Kind);
        Assert.Equal(TimeSpan.FromMilliseconds(1234), result.Lines[0].Time);
        Assert.Equal(TimeSpan.FromMilliseconds(60345), result.Lines[1].Time);
        Assert.Equal(0, session.Searches);
        Assert.Equal("SpotifyLyricsWorking", session.Status);
    }

    [Fact]
    public async Task Disconnected_session_makes_no_network_requests()
    {
        using var handler = new Handler(_ => throw new Exception("Must not request"));
        using var http = new HttpClient(handler);
        Assert.Null(await new SpotifyLyricsProvider(http, new Session { Credentials = null }).FindAsync(Query, default));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Rejected_api_search_falls_back_to_the_web_player()
    {
        var session = new Session();
        using var http = new HttpClient(new Handler(r => r.RequestUri!.Host == "api.spotify.com"
            ? new(HttpStatusCode.Forbidden) : Json(Lyrics)));
        Assert.NotNull(await new SpotifyLyricsProvider(http, session).FindAsync(Query, default));
        Assert.Equal(1, session.Searches);
    }

    private const string GraphqlSearch = """
        {"data":{"searchV2":{"tracksV2":{"items":[{"item":{"data":{
        "uri":"spotify:track:1234567890123456789012","name":"Song",
        "artists":{"items":[{"profile":{"name":"Artist"}}]},
        "albumOfTrack":{"name":"Album"},"duration":{"totalMilliseconds":180000}}}}]}}}}
        """;

    private static SpotifySearchTemplate Template() => SpotifySearchTemplate.TryCapture("POST",
        "https://api-partner.spotify.com/pathfinder/v2/query",
        """{"variables":{"searchTerm":"older song","limit":20},"operationName":"searchTracks","extensions":{"persistedQuery":{"version":1,"sha256Hash":"abc"}}}""",
        [new("app-platform", "WebPlayer")])!;

    [Fact]
    public async Task Replayed_web_player_search_needs_neither_the_browser_nor_the_public_endpoint()
    {
        var session = new Session { SearchTemplate = Template() };
        var hosts = new List<string>();
        string? searched = null;
        using var http = new HttpClient(new Handler(r =>
        {
            hosts.Add(r.RequestUri!.Host);
            if (r.RequestUri.Host != "api-partner.spotify.com") return Json(Lyrics);
            searched = r.Content!.ReadAsStringAsync().Result;
            return Json(GraphqlSearch);
        }));

        var result = LyricsService.FromRecord(await new SpotifyLyricsProvider(http, session).FindAsync(Query, default));

        Assert.Equal(LyricsKind.Synced, result.Kind);
        Assert.Equal(0, session.Searches);
        Assert.Equal(["api-partner.spotify.com", "spclient.wg.spotify.com"], hosts);
        Assert.Contains("\"searchTerm\":\"Song Artist\"", searched);
    }

    [Fact]
    public async Task Query_captured_by_the_web_player_search_is_sent_at_once()
    {
        var session = new Session { Tracks = [], CapturedBySearch = Template() };
        var replays = 0;
        using var http = new HttpClient(new Handler(r =>
        {
            if (r.RequestUri!.Host == "api.spotify.com") return new(HttpStatusCode.TooManyRequests);
            if (r.RequestUri.Host != "api-partner.spotify.com") return Json(Lyrics);
            replays++;
            return Json(GraphqlSearch);
        }));

        var result = LyricsService.FromRecord(await new SpotifyLyricsProvider(http, session).FindAsync(Query, default));

        Assert.Equal(LyricsKind.Synced, result.Kind);
        Assert.Equal(1, session.Searches);
        Assert.Equal(1, replays);
    }

    [Fact]
    public async Task Retired_replay_query_falls_back_to_the_web_player()
    {
        var session = new Session { SearchTemplate = Template() };
        var replays = 0;
        using var http = new HttpClient(new Handler(r =>
        {
            if (r.RequestUri!.Host == "api-partner.spotify.com") { replays++; return new(HttpStatusCode.BadRequest); }
            return r.RequestUri.Host == "api.spotify.com" ? new(HttpStatusCode.TooManyRequests) : Json(Lyrics);
        }));
        var provider = new SpotifyLyricsProvider(http, session);

        Assert.NotNull(await provider.FindAsync(Query, default));
        Assert.NotNull(await provider.FindAsync(Query, default));

        // Not sent again: the web player searches until it captures a query Spotify takes.
        Assert.Equal(2, session.Searches);
        Assert.Equal(1, replays);
    }

    [Theory]
    [InlineData("503")]
    [InlineData("429")]
    [InlineData("network")]
    [InlineData("timeout")]
    public async Task Unreachable_or_busy_search_is_a_failure_not_missing_lyrics(string problem)
    {
        var session = new Session { SearchTemplate = Template() };
        var replays = 0;
        var others = 0;
        using var http = new HttpClient(new Handler(r =>
        {
            if (r.RequestUri!.Host != "api-partner.spotify.com") { others++; return Json(Lyrics); }
            replays++;
            return problem switch
            {
                "503" => new(HttpStatusCode.ServiceUnavailable),
                "429" => new(HttpStatusCode.TooManyRequests),
                "network" => throw new HttpRequestException("Connection reset"),
                _ => throw new TaskCanceledException(),
            };
        }));
        var provider = new SpotifyLyricsProvider(http, session);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.FindAsync(Query, default));
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.FindAsync(Query, default));

        // The query is kept and sent again; the web player, which would search the same
        // servers, is not woken on every retry, and nothing reports the song as lyric-less.
        Assert.Equal(2, replays);
        Assert.Equal(0, others);
        Assert.Equal(0, session.Searches);
        Assert.Null(session.Status);
    }

    [Fact]
    public async Task Failed_replay_of_the_query_the_web_player_just_captured_is_a_failure()
    {
        // No query captured yet: the web player searches once, its query is sent again and that
        // fails. Reporting no lyrics here would get the song cached as "not found" for hours.
        var session = new Session { Tracks = [], CapturedBySearch = Template() };
        using var http = new HttpClient(new Handler(r => r.RequestUri!.Host switch
        {
            "api.spotify.com" => new(HttpStatusCode.TooManyRequests),
            "api-partner.spotify.com" => throw new HttpRequestException("Connection reset"),
            _ => Json(Lyrics),
        }));

        await Assert.ThrowsAsync<HttpRequestException>(() => new SpotifyLyricsProvider(http, session).FindAsync(Query, default));

        Assert.Equal(1, session.Searches);
        Assert.Null(session.Status);
    }

    [Fact]
    public async Task Search_token_rejected_again_after_the_web_player_loads_is_reported()
    {
        var session = new Session { Tracks = [], SearchTemplate = Template() };
        using var http = new HttpClient(new Handler(r => r.RequestUri!.Host == "api.spotify.com"
            ? new(HttpStatusCode.TooManyRequests) : new(HttpStatusCode.Unauthorized)));

        await Assert.ThrowsAsync<HttpRequestException>(() => new SpotifyLyricsProvider(http, session).FindAsync(Query, default));

        // One page load (which can bring a new token), then the same answer as rejected lyrics.
        Assert.Equal(1, session.Searches);
        Assert.Equal("SpotifyLyricsDenied", session.Status);
    }

    [Fact]
    public async Task Answered_search_without_the_song_still_means_no_lyrics()
    {
        var session = new Session { Tracks = [], SearchTemplate = Template() };
        var replays = 0;
        using var http = new HttpClient(new Handler(r =>
        {
            if (r.RequestUri!.Host != "api-partner.spotify.com") return new(HttpStatusCode.TooManyRequests);
            // Answered once without the song, then failing: the answer stands.
            return ++replays == 1 ? Json(GraphqlSearch.Replace("\"Song\"", "\"Other\"")) : new(HttpStatusCode.ServiceUnavailable);
        }));

        Assert.Null(await new SpotifyLyricsProvider(http, session).FindAsync(Query, default));
        Assert.Equal("SpotifyLyricsUnavailable", session.Status);
    }

    private sealed class NoLyrics : ILyricsProvider
    {
        public string Name => "LRCLIB";
        public bool IsLocal => false;
        public Task<LrclibRecord?> FindAsync(TrackQuery query, CancellationToken ct) => Task.FromResult<LrclibRecord?>(null);
    }

    [Fact]
    public async Task Web_player_that_captured_no_search_is_a_failure_not_missing_lyrics()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TaskitifyTests", Guid.NewGuid().ToString("N"));
        try
        {
            // First lookup after starting: no query captured yet, and the web player's own search
            // fails too. Nothing answered, so the song must not be cached as "not found".
            var session = new Session { Tracks = [] };
            using var http = new HttpClient(new Handler(r => r.RequestUri!.Host switch
            {
                "api.spotify.com" => new(HttpStatusCode.TooManyRequests),
                "api-partner.spotify.com" => Json(GraphqlSearch),
                _ => Json(Lyrics),
            }));
            var service = new LyricsService([new FallbackLyricsProvider(new SpotifyLyricsProvider(http, session), new NoLyrics())], new LyricsCache(dir));

            Assert.Equal(LyricsKind.Error, (await service.GetLyricsAsync(Query, default)).Kind);
            Assert.Null(session.Status);

            // The retry: the web player captures its query this time.
            session.CapturedBySearch = Template();
            var retried = await service.GetLyricsAsync(Query, default);
            Assert.Equal((LyricsKind.Synced, "Spotify"), (retried.Kind, retried.Source));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Search_failure_is_retried_instead_of_cached_as_not_found()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TaskitifyTests", Guid.NewGuid().ToString("N"));
        try
        {
            var down = true;
            using var http = new HttpClient(new Handler(r => r.RequestUri!.Host switch
            {
                "api-partner.spotify.com" when down => new(HttpStatusCode.ServiceUnavailable),
                "api-partner.spotify.com" => Json(GraphqlSearch),
                "api.spotify.com" => new(HttpStatusCode.TooManyRequests),
                _ => Json(Lyrics),
            }));
            var spotify = new SpotifyLyricsProvider(http, new Session { SearchTemplate = Template(), Tracks = [] });
            var service = new LyricsService([new FallbackLyricsProvider(spotify, new NoLyrics())], new LyricsCache(dir));

            Assert.Equal(LyricsKind.Error, (await service.GetLyricsAsync(Query, default)).Kind);
            down = false;
            var retried = await service.GetLyricsAsync(Query, default);

            // Neither the caches nor the fallback chain kept the failed attempt as Spotify's answer.
            Assert.Equal((LyricsKind.Synced, "Spotify", false), (retried.Kind, retried.Source, retried.FromCache));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Rate_limited_public_search_is_skipped_until_its_retry_after()
    {
        var now = DateTimeOffset.UtcNow;
        var publicSearches = 0;
        using var http = new HttpClient(new Handler(r =>
        {
            if (r.RequestUri!.Host != "api.spotify.com") return Json(Lyrics);
            publicSearches++;
            var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return limited;
        }));
        var provider = new SpotifyLyricsProvider(http, new Session(), () => now);

        await provider.FindAsync(Query, default);
        now += TimeSpan.FromSeconds(20);
        await provider.FindAsync(Query, default);
        Assert.Equal(1, publicSearches);

        now += TimeSpan.FromSeconds(11);
        await provider.FindAsync(Query, default);
        Assert.Equal(2, publicSearches);
    }

    [Fact]
    public async Task Diagnostics_report_status_codes_and_never_credentials()
    {
        var lines = new List<string>();
        using var http = new HttpClient(new Handler(r => r.RequestUri!.Host == "api.spotify.com"
            ? new(HttpStatusCode.TooManyRequests) : Json(Lyrics)));

        await new SpotifyLyricsProvider(http, new Session(), lines.Add).FindAsync(Query, default);

        var line = Assert.Single(lines);
        Assert.Contains("api search 429", line);
        Assert.Contains("web player search 1 found", line);
        Assert.Contains("lyrics 200", line);
        Assert.DoesNotContain("test-token", line);
        Assert.DoesNotContain("test-client", line);
        Assert.DoesNotContain("http", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diagnostics_are_written_when_the_lookup_fails()
    {
        var lines = new List<string>();
        using var http = new HttpClient(new Handler(r => r.RequestUri!.Host == "api.spotify.com" ? Json(Search) : new(HttpStatusCode.Unauthorized)));

        await Assert.ThrowsAsync<HttpRequestException>(() => new SpotifyLyricsProvider(http, new Session(), lines.Add).FindAsync(Query, default));

        var line = Assert.Single(lines);
        Assert.Contains("api search 200", line);
        Assert.Contains("lyrics 401", line);
        Assert.Contains("failed: HttpRequestException", line);
    }

    [Fact]
    public async Task Search_transport_failure_still_uses_the_web_player()
    {
        var session = new Session();
        using var http = new HttpClient(new Handler(r => r.RequestUri!.Host == "api.spotify.com"
            ? throw new HttpRequestException("Unavailable search") : Json(Lyrics)));
        Assert.NotNull(await new SpotifyLyricsProvider(http, session).FindAsync(Query, default));
        Assert.Equal(1, session.Searches);
    }

    [Fact]
    public async Task Wrong_track_is_never_used()
    {
        using var handler = new Handler(_ => Json(Search.Replace("Song", "Other")));
        using var http = new HttpClient(handler);
        var session = new Session { Tracks = [new(Id, Metadata with { Duration = 300 })] };
        Assert.Null(await new SpotifyLyricsProvider(http, session).FindAsync(Query, default));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Rejected_lyrics_are_reported_as_transient_failure(HttpStatusCode code)
    {
        var session = new Session();
        using var http = new HttpClient(new Handler(r => r.RequestUri!.Host == "api.spotify.com" ? Json(Search) : new(code)));
        await Assert.ThrowsAsync<HttpRequestException>(() => new SpotifyLyricsProvider(http, session).FindAsync(Query, default));
        Assert.Equal("SpotifyLyricsDenied", session.Status);
    }

    [Fact]
    public async Task Expired_token_is_renewed_once_instead_of_falling_back_to_LRCLIB()
    {
        var session = new Session();
        using var http = new HttpClient(new Handler(r => r.RequestUri!.Host == "api.spotify.com" ? Json(Search)
            : r.Headers.Authorization?.Parameter == "renewed-token" ? Json(Lyrics) : new(HttpStatusCode.Unauthorized)));

        var result = LyricsService.FromRecord(await new SpotifyLyricsProvider(http, session).FindAsync(Query, default));

        Assert.Equal(LyricsKind.Synced, result.Kind);
        Assert.Equal(1, session.Renewals);
        Assert.Equal("SpotifyLyricsWorking", session.Status);
    }

    [Fact]
    public void Reads_the_web_player_token_expiry()
    {
        using var json = JsonDocument.Parse("""{"clientId":"x","accessToken":"abc","accessTokenExpirationTimestampMs":1760750000000,"isAnonymous":false}""");
        var token = SpotifyLyricsProvider.ReadToken(json.RootElement);
        Assert.Equal("abc", token?.AccessToken);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1760750000000), token?.ExpiresAt);

        using var anonymous = JsonDocument.Parse("""{"isAnonymous":true}""");
        Assert.Null(SpotifyLyricsProvider.ReadToken(anonymous.RootElement));
    }

    [Fact]
    public async Task Missing_lyrics_are_not_an_authentication_failure()
    {
        var session = new Session();
        using var http = new HttpClient(new Handler(r => r.RequestUri!.Host == "api.spotify.com" ? Json(Search) : new(HttpStatusCode.NotFound)));
        Assert.Null(await new SpotifyLyricsProvider(http, session).FindAsync(Query, default));
        Assert.Equal("SpotifyLyricsUnavailable", session.Status);
    }

    [Fact]
    public void Reads_web_player_graphql_tracks_and_ignores_albums()
    {
        using var json = JsonDocument.Parse("""
            {"data":{"searchV2":{"tracksV2":{"items":[{"item":{"data":{
            "uri":"spotify:track:1234567890123456789012","name":"Song",
            "artists":{"items":[{"profile":{"name":"Artist"}}]},
            "albumOfTrack":{"name":"Album"},"duration":{"totalMilliseconds":180000}}}}]},
            "albums":{"items":[{"uri":"spotify:album:1234567890123456789012","name":"Wrong"}]}}}}
            """);
        var track = Assert.Single(SpotifyLyricsProvider.ReadSearch(json.RootElement));
        Assert.Equal(Id, track.Id);
        Assert.True(TrackMatcher.IsMatch(Query, track.Metadata));
    }

    [Fact]
    public void Unsynced_lyrics_do_not_become_zero_timestamp_synced_lyrics()
    {
        using var json = JsonDocument.Parse(Lyrics.Replace("LINE_SYNCED", "UNSYNCED"));
        var result = LyricsService.FromRecord(SpotifyLyricsProvider.ReadLyrics(json.RootElement, Metadata));
        Assert.Equal(LyricsKind.PlainOnly, result.Kind);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void Invalid_times_and_empty_payloads_do_not_win_the_race()
    {
        using var invalid = JsonDocument.Parse(Lyrics.Replace("1234", "-1").Replace("60345", "9999999999999999999999"));
        Assert.Equal(LyricsKind.PlainOnly, LyricsService.FromRecord(SpotifyLyricsProvider.ReadLyrics(invalid.RootElement, Metadata)).Kind);
        using var empty = JsonDocument.Parse("{\"lyrics\":{\"syncType\":\"LINE_SYNCED\",\"lines\":[]}}");
        Assert.Null(SpotifyLyricsProvider.ReadLyrics(empty.RootElement, Metadata));
    }
}
