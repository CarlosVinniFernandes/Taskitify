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
        public Task<SpotifyCredentials?> GetCredentialsAsync(CancellationToken ct) => Task.FromResult(Credentials);
        public Task<IReadOnlyList<SpotifyTrack>> SearchAsync(TrackQuery query, CancellationToken ct)
        { Searches++; return Task.FromResult(Tracks); }
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
