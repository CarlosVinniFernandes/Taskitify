using System.Net;
using System.Text;
using Taskitify.Core.Lyrics;

namespace Taskitify.Tests;

public class LrclibClientTests
{
    private const string RecordJson =
        """{"id":1,"trackName":"Song","artistName":"Artist","albumName":"Album","duration":200,"instrumental":false,"plainLyrics":"x","syncedLyrics":"[00:01.00]x"}""";

    private sealed class ScriptedHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _next;

        public int Calls => _next;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(responses[Math.Min(_next++, responses.Length - 1)]());
    }

    private static HttpResponseMessage Status(HttpStatusCode code, TimeSpan? retryAfter = null)
    {
        var r = new HttpResponseMessage(code) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        if (retryAfter is { } ra)
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(ra);
        return r;
    }

    private static HttpResponseMessage Ok() =>
        new(HttpStatusCode.OK) { Content = new StringContent(RecordJson, Encoding.UTF8, "application/json") };

    private static readonly TrackQuery Query = new("Song", "Artist", "Album", TimeSpan.FromSeconds(200));

    private static (LrclibClient Client, ScriptedHandler Handler, List<TimeSpan> Delays) Create(params Func<HttpResponseMessage>[] responses)
    {
        var handler = new ScriptedHandler(responses);
        var delays = new List<TimeSpan>();
        var client = new LrclibClient(new HttpClient(handler) { BaseAddress = LrclibClient.BaseAddress }, (d, _) =>
        {
            delays.Add(d);
            return Task.CompletedTask;
        })
        { MinSpacing = TimeSpan.Zero };
        return (client, handler, delays);
    }

    [Fact]
    public async Task Retries_rate_limited_requests_with_backoff()
    {
        var (client, handler, delays) = Create(
            () => Status(HttpStatusCode.TooManyRequests),
            () => Status(HttpStatusCode.ServiceUnavailable),
            Ok);

        var record = await client.GetAsync(Query, default);

        Assert.Equal("Song", record?.TrackName);
        Assert.Equal(3, handler.Calls);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], delays);
        Assert.Equal(2, client.RateLimitedCount);
    }

    [Fact]
    public async Task Honours_longer_retry_after_header()
    {
        var (client, _, delays) = Create(() => Status(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(5)), Ok);

        await client.GetAsync(Query, default);

        Assert.Equal([TimeSpan.FromSeconds(5)], delays);
    }

    [Fact]
    public async Task Not_found_is_not_retried()
    {
        var (client, handler, delays) = Create(() => Status(HttpStatusCode.NotFound));

        Assert.Null(await client.GetAsync(Query, default));
        Assert.Equal(1, handler.Calls);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task Gives_up_after_the_last_retry()
    {
        var (client, handler, _) = Create(() => Status(HttpStatusCode.TooManyRequests));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(Query, default));
        Assert.Equal(10, handler.Calls); // first try + 9 retries
    }

    [Fact]
    public async Task Spaces_consecutive_requests()
    {
        var handler = new ScriptedHandler(Ok);
        var delays = new List<TimeSpan>();
        var client = new LrclibClient(new HttpClient(handler) { BaseAddress = LrclibClient.BaseAddress }, (d, _) =>
        {
            delays.Add(d);
            return Task.CompletedTask;
        })
        { MinSpacing = TimeSpan.FromSeconds(1) };

        await client.GetAsync(Query, default);
        await client.GetAsync(Query, default);

        var wait = Assert.Single(delays);
        Assert.InRange(wait, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1));
    }
}
