using System.Text.Json;
using Taskitify.Core.Lyrics;

namespace Taskitify.Tests;

public sealed class SpotifySearchTemplateTests
{
    private const string Endpoint = "https://api-partner.spotify.com/pathfinder/v2/query";
    private const string Body = """
        {"variables":{"searchTerm":"M1 A1 Gorillaz","offset":0,"limit":20,"numberOfTopResults":20},
        "operationName":"searchTracks","extensions":{"persistedQuery":{"version":1,"sha256Hash":"b026831900000000"}}}
        """;

    private static readonly KeyValuePair<string, string>[] Headers =
    [
        new("Authorization", "Bearer old-token"),
        new("client-token", "old-client"),
        new("Cookie", "sp_dc=secret"),
        new("Content-Type", "application/json;charset=UTF-8"),
        new("app-platform", "WebPlayer"),
        new("spotify-app-version", "1.2.3"),
        new("Accept-Encoding", "gzip, deflate, br, zstd"),
    ];

    [Fact]
    public void Captures_the_web_player_track_search()
    {
        var template = SpotifySearchTemplate.TryCapture("POST", Endpoint, Body, Headers);

        Assert.Equal("searchTracks", template?.OperationName);
        Assert.Equal("b026831900000000", template?.QueryHash);
    }

    [Theory]
    [InlineData("GET", Endpoint, Body)]
    [InlineData("POST", "https://api-partner.example.com/pathfinder/v2/query", Body)]
    [InlineData("POST", "http://api-partner.spotify.com/pathfinder/v2/query", Body)]
    [InlineData("POST", Endpoint, """{"variables":{"uri":"spotify:user:x"},"operationName":"profileAttributes"}""")]
    [InlineData("POST", Endpoint, """{"variables":{},"operationName":"searchTracks"}""")]
    [InlineData("POST", Endpoint, "not json")]
    [InlineData("POST", Endpoint, null)]
    public void Ignores_everything_that_is_not_a_track_search(string method, string uri, string? body)
    {
        Assert.Null(SpotifySearchTemplate.TryCapture(method, uri, body, Headers));
    }

    [Fact]
    public async Task Replays_with_the_new_search_term_and_current_credentials_only()
    {
        var template = SpotifySearchTemplate.TryCapture("POST", Endpoint, Body, Headers)!;

        using var request = template.CreateRequest("Ms. Jackson Outkast", new SpotifyCredentials("new-token", "new-client"));

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(Endpoint, request.RequestUri!.ToString());
        Assert.Equal("new-token", request.Headers.Authorization?.Parameter);
        Assert.Equal("new-client", Assert.Single(request.Headers.GetValues("Client-Token")));
        Assert.Equal("WebPlayer", Assert.Single(request.Headers.GetValues("app-platform")));
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.False(request.Headers.Contains("Accept-Encoding"));
        using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        var variables = json.RootElement.GetProperty("variables");
        Assert.Equal("Ms. Jackson Outkast", variables.GetProperty("searchTerm").GetString());
        Assert.Equal(20, variables.GetProperty("limit").GetInt32());
        Assert.Equal("b026831900000000", json.RootElement.GetProperty("extensions").GetProperty("persistedQuery").GetProperty("sha256Hash").GetString());

        // The template itself is not changed by a replay.
        using var again = template.CreateRequest("Patience Tame Impala", new SpotifyCredentials("t", null));
        Assert.Contains("Patience Tame Impala", await again.Content!.ReadAsStringAsync());
    }
}
