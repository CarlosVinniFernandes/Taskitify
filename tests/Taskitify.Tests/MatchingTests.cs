using Taskitify.Core.Lyrics;

namespace Taskitify.Tests;

public class TrackMatcherTests
{
    [Theory]
    [InlineData("No. 1 Party Anthem", "no 1 party anthem")]
    [InlineData("Here Comes The Sun - Remastered 2009", "here comes the sun")]
    [InlineData("Something - 2019 Mix", "something 2019 mix")]
    [InlineData("Song (feat. Someone)", "song")]
    [InlineData("Song [Remastered 2015]", "song")]
    [InlineData("Canção Ação", "cancao acao")]
    [InlineData("Don't Stop Me Now", "dont stop me now")]
    public void NormalizeTitle(string input, string expected)
    {
        Assert.Equal(expected, TrackMatcher.NormalizeTitle(input));
    }

    [Theory]
    [InlineData("Arctic Monkeys", "Arctic Monkeys", true)]
    [InlineData("Simon & Garfunkel", "Simon and Garfunkel", true)]
    [InlineData("Daft Punk", "Daft Punk, Pharrell Williams", true)]
    [InlineData("Arctic Monkeys", "The Strokes", false)]
    [InlineData("", "Anyone", false)]
    public void ArtistsMatch(string a, string b, bool expected)
    {
        Assert.Equal(expected, TrackMatcher.ArtistsMatch(a, b));
    }

    [Fact]
    public void Rejects_same_title_with_different_duration()
    {
        var q = new TrackQuery("Intro", "The xx", null, TimeSpan.FromSeconds(128));
        Assert.True(TrackMatcher.IsMatch(q, Record("Intro", "The xx", 127)));
        Assert.False(TrackMatcher.IsMatch(q, Record("Intro", "The xx", 140)));
    }

    [Fact]
    public void Accepts_small_spotify_vs_lrclib_duration_drift()
    {
        // Real case: Spotify reports 271.7 s, LRCLIB has the same recording at 276 s.
        var q = new TrackQuery("Why Are Sundays So Depressing", "The Strokes", "The New Abnormal", TimeSpan.FromSeconds(271.7));
        Assert.True(TrackMatcher.IsMatch(q, Record("Why Are Sundays So Depressing", "The Strokes", 276)));
        Assert.False(TrackMatcher.IsMatch(q, Record("Why Are Sundays So Depressing", "The Strokes", 342))); // other cut
    }

    [Fact]
    public void Rejects_different_title_or_artist()
    {
        var q = new TrackQuery("Yellow", "Coldplay", null, TimeSpan.FromSeconds(269));
        Assert.False(TrackMatcher.IsMatch(q, Record("Yellow Submarine", "Coldplay", 269)));
        Assert.False(TrackMatcher.IsMatch(q, Record("Yellow", "Someone Else", 269)));
    }

    internal static LrclibRecord Record(string title, string artist, double? duration, string? synced = "[00:01.00]x", bool instrumental = false) =>
        new(1, title, artist, "Album", duration, instrumental, "x", synced);
}

public class LyricsServiceTests
{
    private sealed class FakeClient : ILrclibClient
    {
        public LrclibRecord? Get { get; set; }
        public List<LrclibRecord> Search { get; } = [];
        public int Calls { get; private set; }
        public Exception? Throw { get; set; }

        public Task<LrclibRecord?> GetAsync(TrackQuery query, CancellationToken ct)
        {
            Calls++;
            return Throw is not null ? Task.FromException<LrclibRecord?>(Throw) : Task.FromResult(Get);
        }

        public Task<IReadOnlyList<LrclibRecord>> SearchAsync(string title, string artist, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<LrclibRecord>>(Search);
        }
    }

    private static readonly TrackQuery Query = new("No. 1 Party Anthem", "Arctic Monkeys", "AM", TimeSpan.FromSeconds(243));

    [Fact]
    public async Task Search_alone_is_enough_when_it_has_synced_lyrics()
    {
        // One request instead of two: LRCLIB's rate limit makes each one count.
        var client = new FakeClient();
        client.Search.Add(TrackMatcherTests.Record("No. 1 Party Anthem", "Arctic Monkeys", 244, "[00:01.00]Found"));

        var result = await new LyricsService(client, null).GetLyricsAsync(Query, default);

        Assert.Equal("Found", Assert.Single(result.Lines).Text);
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task Uses_exact_match_with_synced_lyrics()
    {
        var client = new FakeClient { Get = TrackMatcherTests.Record("No. 1 Party Anthem", "Arctic Monkeys", 243, "[00:01.00]Hello") };
        var result = await new LyricsService(client, null).GetLyricsAsync(Query, default);

        Assert.Equal(LyricsKind.Synced, result.Kind);
        Assert.Equal("Hello", Assert.Single(result.Lines).Text);
        Assert.Equal(2, client.Calls); // empty search, then the exact lookup
    }

    [Fact]
    public async Task Falls_back_to_search_for_synced_and_ignores_wrong_songs()
    {
        var client = new FakeClient { Get = TrackMatcherTests.Record("No. 1 Party Anthem", "Arctic Monkeys", 243, synced: null) };
        client.Search.Add(TrackMatcherTests.Record("Do I Wanna Know?", "Arctic Monkeys", 272, "[00:01.00]Wrong song"));
        client.Search.Add(TrackMatcherTests.Record("No. 1 Party Anthem", "Arctic Monkeys", 300, "[00:01.00]Wrong version"));
        client.Search.Add(TrackMatcherTests.Record("No. 1 Party Anthem", "Arctic Monkeys", 244, "[00:01.00]Right"));

        var result = await new LyricsService(client, null).GetLyricsAsync(Query, default);

        Assert.Equal("Right", Assert.Single(result.Lines).Text);
    }

    [Fact]
    public async Task Ambiguous_search_results_are_not_found()
    {
        var client = new FakeClient();
        client.Search.Add(TrackMatcherTests.Record("No. 1 Party Anthem (Live)", "Arctic Monkeys", 243));
        client.Search.Add(TrackMatcherTests.Record("Party Anthem", "Someone", 243));

        var result = await new LyricsService(client, null).GetLyricsAsync(Query, default);

        Assert.Equal(LyricsKind.NotFound, result.Kind);
    }

    [Fact]
    public async Task Plain_only_and_instrumental_are_reported()
    {
        var plain = new FakeClient { Get = TrackMatcherTests.Record("No. 1 Party Anthem", "Arctic Monkeys", 243, synced: null) };
        Assert.Equal(LyricsKind.PlainOnly, (await new LyricsService(plain, null).GetLyricsAsync(Query, default)).Kind);

        var instrumental = new FakeClient { Get = TrackMatcherTests.Record("No. 1 Party Anthem", "Arctic Monkeys", 243, synced: null, instrumental: true) };
        Assert.Equal(LyricsKind.Instrumental, (await new LyricsService(instrumental, null).GetLyricsAsync(Query, default)).Kind);
    }

    [Fact]
    public async Task Network_errors_are_reported_and_not_cached()
    {
        var client = new FakeClient { Throw = new HttpRequestException("offline") };
        var service = new LyricsService(client, null);

        Assert.Equal(LyricsKind.Error, (await service.GetLyricsAsync(Query, default)).Kind);

        client.Throw = null;
        client.Get = TrackMatcherTests.Record("No. 1 Party Anthem", "Arctic Monkeys", 243);
        Assert.Equal(LyricsKind.Synced, (await service.GetLyricsAsync(Query, default)).Kind);
    }

    [Fact]
    public async Task Disk_cache_avoids_repeat_requests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TaskitifyTests", Guid.NewGuid().ToString("N"));
        try
        {
            var client = new FakeClient { Get = TrackMatcherTests.Record("No. 1 Party Anthem", "Arctic Monkeys", 243, "[00:01.00]Cached") };
            await new LyricsService(client, new LyricsCache(dir)).GetLyricsAsync(Query, default);
            var callsAfterFirstLookup = client.Calls;

            // A new service (fresh memory cache) must be served from disk: no new request.
            var result = await new LyricsService(client, new LyricsCache(dir)).GetLyricsAsync(Query, default);
            Assert.Equal(callsAfterFirstLookup, client.Calls);
            Assert.Equal("Cached", Assert.Single(result.Lines).Text);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Negative_cache_expires()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TaskitifyTests", Guid.NewGuid().ToString("N"));
        var now = DateTimeOffset.UtcNow;
        try
        {
            var cache = new LyricsCache(dir, () => now);
            await cache.SaveAsync(Query, LyricsResult.NotFound, null, default);
            Assert.Equal(LyricsKind.NotFound, (await cache.TryGetAsync(Query, default))?.Kind);

            now += TimeSpan.FromDays(2);
            Assert.Null(await cache.TryGetAsync(Query, default));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
