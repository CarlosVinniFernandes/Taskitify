using System.Net;
using System.Text;
using System.Text.Json;
using Taskitify.Core.Lyrics;

namespace Taskitify.Tests;

// Fixtures mirror the real documents' structure (lrc.red TTML, LRCLIB Lyricsfile, Spotify's
// color-lyrics JSON) with invented words: real lyrics are not stored in the repository.
public class TtmlParserTests
{
    private const string Duet = """
        <tt xmlns="http://www.w3.org/ns/ttml" xmlns:ttm="http://www.w3.org/ns/ttml#metadata" xmlns:lrc="http://lrc.red/lyric-ttml-internal" lrc:timing="Word" xml:lang="en">
          <head><metadata><ttm:agent type="person" xml:id="v1"/><ttm:agent type="person" xml:id="v2"/></metadata></head>
          <body dur="1:00.000"><div begin="1.000" end="20.000">
            <p begin="1.000" end="4.000" ttm:agent="v1"><span begin="1.000" end="1.500">Paper</span> <span begin="1.600" end="2.000">boats</span> <span begin="2.000" end="2.400">sail</span><span begin="2.400" end="3.000">ing</span><span ttm:role="x-bg" begin="3.000" end="4.000"><span begin="3.000" end="3.500">(oh</span> <span begin="3.500" end="4.000">oh)</span></span></p>
            <p begin="5.000" end="7.000" ttm:agent="v2"><span begin="5.000" end="5.400">over</span> <span begin="6.200" end="7.000">rooftops</span></p>
            <p begin="8.000" end="9.000" ttm:agent="v1000"><span begin="8.000" end="9.000">together</span></p>
          </div></body></tt>
        """;

    [Fact]
    public void Reads_words_syllables_backing_vocals_and_duet_sides()
    {
        var lyrics = TtmlParser.Parse(Duet, "test")!;
        Assert.Equal(LyricsSyncLevel.Syllable, lyrics.Sync); // "sail" + "ing"
        Assert.True(lyrics.HasBackground);
        Assert.True(lyrics.HasDuet);
        var first = lyrics.Lines[0];
        Assert.Equal("Paper boats sailing", first.Text);
        Assert.Equal(TimeSpan.FromSeconds(1), first.Time);
        Assert.Equal(TimeSpan.FromSeconds(4), first.End);
        Assert.Equal(0, first.Singer);
        // A syllable anchor inside the word, and the line closed at its last syllable's end.
        Assert.Contains(first.Anchors!, a => a.TextOffset == "Paper boats sail".Length && a.Time == TimeSpan.FromSeconds(2.4));
        Assert.Equal(new LyricAnchor(TimeSpan.FromSeconds(3), first.Text.Length), first.Anchors![^1]);
        var bg = Assert.Single(first.Background!);
        Assert.Equal("(oh oh)", bg.Text);
        Assert.Equal(TimeSpan.FromSeconds(3), bg.Start);
        Assert.Equal(TimeSpan.FromSeconds(4), bg.End);
        Assert.Equal(1, lyrics.Lines[1].Singer);
        Assert.Equal(0, lyrics.Lines[2].Singer); // everyone together is not a side
    }

    [Fact]
    public void A_pause_between_words_holds_the_fill_on_the_space()
    {
        var line = TtmlParser.Parse(Duet, "test")!.Lines[1]; // "over" ends 5.4, "rooftops" starts 6.2
        Assert.Contains(line.Anchors!, a => a.Time == TimeSpan.FromSeconds(5.4) && a.TextOffset == "over".Length);
        Assert.Contains(line.Anchors!, a => a.Time == TimeSpan.FromSeconds(6.2) && a.TextOffset == "over ".Length);
    }

    [Fact]
    public void Line_timed_ttml_has_no_anchors()
    {
        const string xml = """<tt xmlns="http://www.w3.org/ns/ttml"><body><div><p begin="00:00:01.000" end="00:00:02.000">Only a line</p></div></body></tt>""";
        var lyrics = TtmlParser.Parse(xml, "test")!;
        Assert.Equal(LyricsSyncLevel.Line, lyrics.Sync);
        Assert.Null(lyrics.Lines[0].Anchors);
        Assert.Equal("Only a line", lyrics.Lines[0].Text);
    }

    [Theory]
    [InlineData("<tt")]
    [InlineData("<html><body>not lyrics</body></html>")]
    [InlineData("")]
    [InlineData("<tt xmlns=\"http://www.w3.org/ns/ttml\"><body/></tt>")]
    public void Malformed_or_empty_documents_are_rejected(string xml) => Assert.Null(TtmlParser.Parse(xml, "test"));

    [Theory]
    [InlineData("12.5", 12.5)]
    [InlineData("1:02.25", 62.25)]
    [InlineData("00:01:02.250", 62.25)]
    [InlineData("3.5s", 3.5)]
    [InlineData("1500ms", 1.5)]
    public void Clock_values(string value, double seconds) => Assert.Equal(TimeSpan.FromSeconds(seconds), TtmlParser.ParseTime(value));
}

public class LyricsfileParserTests
{
    private const string WordSynced = """
        version: '1.0'
        metadata:
          title: Paper Boats
          artist: Nobody
          duration_ms: 60000
          instrumental: false
        lines:
        - text: Paper boats sailing
          start_ms: 1000
          end_ms: 4000
          words:
          - text: 'Paper '
            start_ms: 1000
          - text: 'boats '
            start_ms: 1600
            end_ms: 2000
          - text: sailing
            start_ms: 2500
        - text: 'It''s raining'
          start_ms: 5000
          end_ms: 7000
          words:
          - text: 'It''s '
            start_ms: 5000
          - text: "raining"
            start_ms: 5500
            end_ms: 7000
        - text: An untimed line
          start_ms: 8000
        """;

    [Fact]
    public void Reads_word_timings_quotes_and_line_ends()
    {
        var lyrics = LyricsfileParser.Parse(WordSynced, "LRCLIB")!;
        Assert.Equal(LyricsSyncLevel.Word, lyrics.Sync);
        Assert.Equal(3, lyrics.Lines.Count);
        var first = lyrics.Lines[0];
        Assert.Equal("Paper boats sailing", first.Text);
        Assert.Equal(TimeSpan.FromSeconds(4), first.End);
        Assert.Contains(first.Anchors!, a => a.Time == TimeSpan.FromSeconds(1.6) && a.TextOffset == "Paper ".Length);
        // "boats" ends at 2.0, "sailing" starts at 2.5: the fill holds on the space.
        Assert.Contains(first.Anchors!, a => a.Time == TimeSpan.FromSeconds(2.0) && a.TextOffset == "Paper boats".Length);
        Assert.Equal("It's raining", lyrics.Lines[1].Text);
        Assert.Null(lyrics.Lines[2].Anchors);
    }

    [Fact]
    public void Line_only_lyricsfile_is_line_level()
    {
        const string yaml = "version: '1.0'\nlines:\n- text: One\n  start_ms: 1000\n- text: Two\n  start_ms: 2000\n";
        var lyrics = LyricsfileParser.Parse(yaml, "LRCLIB")!;
        Assert.Equal(LyricsSyncLevel.Line, lyrics.Sync);
        Assert.Equal(["One", "Two"], lyrics.Lines.Select(l => l.Text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("version: '1.0'\nmetadata:\n  title: x\n")]
    [InlineData("lines:\n- text: no time\n")]
    public void Malformed_or_empty_documents_are_rejected(string yaml) => Assert.Null(LyricsfileParser.Parse(yaml, "LRCLIB"));
}

public class SpotifySyllableTests
{
    [Fact]
    public void Syllable_synced_spotify_lyrics_keep_their_timing_and_gain_syllable_anchors()
    {
        const string json = """
            {"lyrics":{"syncType":"SYLLABLE_SYNCED","provider":"Musixmatch","lines":[
              {"startTimeMs":"1000","words":"Paper boats","syllables":[{"startTimeMs":"1000","numChars":5},{"startTimeMs":"1600","numChars":5}]},
              {"startTimeMs":"3000","words":"No syllables here","syllables":[]}]}}
            """;
        using var doc = JsonDocument.Parse(json);
        var record = SpotifyLyricsProvider.ReadLyrics(doc.RootElement, new LrclibRecord(0, "Paper Boats", "Nobody", null, null, false, null, null))!;
        Assert.NotNull(record.SyncedLyrics);
        var lines = LrcParser.Parse(record.SyncedLyrics);
        Assert.Equal(2, lines.Count);
        Assert.Equal("Paper boats", lines[0].Text);
        Assert.Contains(lines[0].Anchors!, a => a.Time == TimeSpan.FromSeconds(1.6) && a.TextOffset == "Paper ".Length);
        Assert.Null(lines[1].Anchors);
    }
}

public class KaraokeServiceTests
{
    private static readonly TrackQuery Song = new("Paper Boats", "Nobody", "Rivers", TimeSpan.FromSeconds(200));

    private sealed class FakeProvider(string id, Func<KaraokeQuery, CancellationToken, Task<KaraokeLyrics?>> find) : IKaraokeProvider
    {
        public string Id => id;
        public int Calls;

        public Task<KaraokeLyrics?> FindAsync(KaraokeQuery query, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return find(query, ct);
        }
    }

    private static KaraokeLyrics Lyrics(LyricsSyncLevel sync, string source, bool background = false, string? title = null) =>
        new([new LyricsLine(TimeSpan.FromSeconds(1), "Paper boats")
        {
            Anchors = [new(TimeSpan.FromSeconds(1), 0), new(TimeSpan.FromSeconds(2), 11)],
            Background = background ? [new LyricsVocal("(oh)", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), null)] : null,
        }], sync, source) { MatchedTitle = title };

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "taskitify-karaoke-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task The_finest_result_wins_and_ties_follow_the_users_order()
    {
        var word = new FakeProvider("a", (_, _) => Task.FromResult<KaraokeLyrics?>(Lyrics(LyricsSyncLevel.Word, "a")));
        var syllable = new FakeProvider("b", (_, _) => Task.FromResult<KaraokeLyrics?>(Lyrics(LyricsSyncLevel.Syllable, "b")));
        var service = new KaraokeService([word, syllable], null) { Enabled = () => ["a", "b"] };
        Assert.Equal("b", (await service.FindAsync(new KaraokeQuery(Song), default))!.Source);

        var sameA = new FakeProvider("a", (_, _) => Task.FromResult<KaraokeLyrics?>(Lyrics(LyricsSyncLevel.Word, "a")));
        var sameB = new FakeProvider("b", (_, _) => Task.FromResult<KaraokeLyrics?>(Lyrics(LyricsSyncLevel.Word, "b")));
        Assert.Equal("b", (await new KaraokeService([sameA, sameB], null) { Enabled = () => ["b", "a"] }.FindAsync(new KaraokeQuery(Song), default))!.Source);
        // Backing vocals beat plain word timing.
        var bg = new FakeProvider("c", (_, _) => Task.FromResult<KaraokeLyrics?>(Lyrics(LyricsSyncLevel.Word, "c", background: true)));
        Assert.Equal("c", (await new KaraokeService([sameA, bg], null) { Enabled = () => ["a", "c"] }.FindAsync(new KaraokeQuery(Song), default))!.Source);
    }

    [Fact]
    public async Task Disabled_providers_are_not_asked()
    {
        var a = new FakeProvider("a", (_, _) => Task.FromResult<KaraokeLyrics?>(Lyrics(LyricsSyncLevel.Word, "a")));
        var b = new FakeProvider("b", (_, _) => Task.FromResult<KaraokeLyrics?>(Lyrics(LyricsSyncLevel.Syllable, "b")));
        var service = new KaraokeService([a, b], null) { Enabled = () => ["a"] };
        Assert.Equal("a", (await service.FindAsync(new KaraokeQuery(Song), default))!.Source);
        Assert.Equal(0, b.Calls);
        Assert.Null(await new KaraokeService([a, b], null) { Enabled = () => [] }.FindAsync(new KaraokeQuery(Song), default));
    }

    [Fact]
    public async Task A_failing_or_slow_provider_does_not_hide_another_ones_answer()
    {
        var broken = new FakeProvider("a", (_, _) => throw new HttpRequestException("down"));
        var slow = new FakeProvider("b", async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return null; });
        var good = new FakeProvider("c", (_, _) => Task.FromResult<KaraokeLyrics?>(Lyrics(LyricsSyncLevel.Word, "c")));
        var failures = new List<string>();
        var service = new KaraokeService([broken, slow, good], null) { Enabled = () => ["a", "b", "c"], ProviderTimeout = TimeSpan.FromMilliseconds(200) };
        service.ProviderFailed += (id, _) => failures.Add(id);
        Assert.Equal("c", (await service.FindAsync(new KaraokeQuery(Song), default))!.Source);
        Assert.Equal(["a", "b"], failures.Order());
    }

    [Fact]
    public async Task A_namesake_from_a_search_is_rejected()
    {
        var wrong = new FakeProvider("a", (_, _) => Task.FromResult<KaraokeLyrics?>(Lyrics(LyricsSyncLevel.Syllable, "a", title: "Another Song Entirely")));
        var right = new FakeProvider("b", (_, _) => Task.FromResult<KaraokeLyrics?>(Lyrics(LyricsSyncLevel.Word, "b", title: "Paper Boats")));
        var service = new KaraokeService([wrong, right], null) { Enabled = () => ["a", "b"] };
        Assert.Equal("b", (await service.FindAsync(new KaraokeQuery(Song), default))!.Source);
    }

    [Fact]
    public async Task Results_and_none_are_cached_but_a_failure_is_not()
    {
        var dir = TempDir();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var cache = new KaraokeCache(dir, () => now) { NegativeTtl = TimeSpan.FromHours(1) };
            var found = new FakeProvider("a", (_, _) => Task.FromResult<KaraokeLyrics?>(Lyrics(LyricsSyncLevel.Syllable, "a", background: true)));
            var service = new KaraokeService([found], cache, () => now) { Enabled = () => ["a"] };
            var first = await service.FindAsync(new KaraokeQuery(Song), default);
            var second = await service.FindAsync(new KaraokeQuery(Song), default);
            Assert.Equal(1, found.Calls);
            Assert.Equal(first!.Lines[0].Text, second!.Lines[0].Text);
            Assert.True(second.HasBackground); // survives the round trip

            var other = Song with { Title = "Other" };
            var none = new FakeProvider("a", (_, _) => Task.FromResult<KaraokeLyrics?>(null));
            var noneService = new KaraokeService([none], cache, () => now) { Enabled = () => ["a"] };
            Assert.Null(await noneService.FindAsync(new KaraokeQuery(other), default));
            Assert.Null(await noneService.FindAsync(new KaraokeQuery(other), default));
            Assert.Equal(1, none.Calls); // remembered as none
            now += TimeSpan.FromHours(2);
            Assert.Null(await noneService.FindAsync(new KaraokeQuery(other), default));
            Assert.Equal(2, none.Calls); // the negative entry expired

            var third = Song with { Title = "Third" };
            var failing = new FakeProvider("a", (_, _) => throw new HttpRequestException("offline"));
            var failingService = new KaraokeService([failing], cache, () => now) { Enabled = () => ["a"] };
            await failingService.FindAsync(new KaraokeQuery(third), default);
            await failingService.FindAsync(new KaraokeQuery(third), default);
            Assert.Equal(2, failing.Calls); // a failure is asked again
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Concurrent_lookups_of_one_song_share_one_request_and_a_caller_can_give_up()
    {
        var gate = new TaskCompletionSource<KaraokeLyrics?>();
        var provider = new FakeProvider("a", (_, _) => gate.Task);
        var service = new KaraokeService([provider], null) { Enabled = () => ["a"] };
        using var cancelled = new CancellationTokenSource();
        var gaveUp = service.FindAsync(new KaraokeQuery(Song), cancelled.Token);
        var waiting = service.FindAsync(new KaraokeQuery(Song), default);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gaveUp);
        gate.SetResult(Lyrics(LyricsSyncLevel.Word, "a"));
        Assert.NotNull(await waiting); // the song changed for one caller; the shared lookup went on
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task A_provider_asking_to_slow_down_is_skipped_until_then()
    {
        var now = DateTimeOffset.UtcNow;
        var throttled = new FakeProvider("a", (_, _) => throw new ProviderCooldownException(TimeSpan.FromSeconds(30)));
        var service = new KaraokeService([throttled], null, () => now) { Enabled = () => ["a"] };
        await service.FindAsync(new KaraokeQuery(Song), default);
        await service.FindAsync(new KaraokeQuery(Song with { Title = "B" }), default);
        Assert.Equal(1, throttled.Calls);
        now += TimeSpan.FromSeconds(31);
        await service.FindAsync(new KaraokeQuery(Song with { Title = "C" }), default);
        Assert.Equal(2, throttled.Calls);
    }

    [Fact]
    public async Task Lrcred_searches_matches_the_song_and_reads_its_ttml()
    {
        var requests = new List<string>();
        var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.PathAndQuery);
            return request.RequestUri!.AbsolutePath switch
            {
                "/search.json" => Json("""{"hits":[{"isrc":"XX0000000001","title":"Paper Boats (Live)","artist":"Nobody","album":"Live","duration":260},{"isrc":"XX0000000002","title":"Paper Boats","artist":"Nobody","album":"Rivers","duration":200.4}]}"""),
                "/s/XX0000000002.ttml" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""<tt xmlns="http://www.w3.org/ns/ttml"><body><div><p begin="1" end="2"><span begin="1" end="1.5">Paper</span> <span begin="1.5" end="2">boats</span></p></div></body></tt>"""),
                },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });
        var provider = new LrcRedProvider(new HttpClient(handler));
        var lyrics = await provider.FindAsync(new KaraokeQuery(Song), default);
        Assert.NotNull(lyrics);
        Assert.Equal(LyricsSyncLevel.Word, lyrics!.Sync);
        Assert.Equal("lrc.red", lyrics.Credit);
        Assert.DoesNotContain(requests, r => r.Contains("XX0000000001")); // the live version is another recording
    }

    [Fact]
    public async Task Lrcred_throttling_becomes_a_cooldown()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(42)) } });
        var ex = await Assert.ThrowsAsync<ProviderCooldownException>(() => new LrcRedProvider(new HttpClient(handler)).FindAsync(new KaraokeQuery(Song), default));
        Assert.Equal(TimeSpan.FromSeconds(42), ex.RetryAfter);
    }

    [Fact]
    public async Task Lrclib_karaoke_reads_the_word_synced_lyricsfile_and_shares_requests_with_the_line_fallback()
    {
        var client = new CountingLrclib(new LrclibRecord(1, "Paper Boats", "Nobody", "Rivers", 200, false, "Paper boats", "[00:01.00]Paper boats")
        {
            HasWordSync = true,
            Lyricsfile = "version: '1.0'\nlines:\n- text: Paper boats\n  start_ms: 1000\n  end_ms: 2000\n  words:\n  - text: 'Paper '\n    start_ms: 1000\n  - text: boats\n    start_ms: 1500\n",
        });
        var memo = new MemoizingLrclibClient(client);
        await new LrclibProvider(memo).FindAsync(Song, default); // the line fallback
        var lyrics = await new LrclibKaraokeProvider(memo).FindAsync(new KaraokeQuery(Song), default);
        Assert.Equal(LyricsSyncLevel.Word, lyrics!.Sync);
        // The fallback's search and get answered the karaoke lookup too: each was sent once.
        Assert.True(client.Gets <= 1 && client.Searches <= 1, $"gets {client.Gets}, searches {client.Searches}");
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    private sealed class CountingLrclib(LrclibRecord record) : ILrclibClient
    {
        public int Gets, Searches;

        public Task<LrclibRecord?> GetAsync(TrackQuery query, CancellationToken ct)
        {
            Interlocked.Increment(ref Gets);
            return Task.FromResult<LrclibRecord?>(record);
        }

        public Task<IReadOnlyList<LrclibRecord>> SearchAsync(string title, string artist, CancellationToken ct)
        {
            Interlocked.Increment(ref Searches);
            return Task.FromResult<IReadOnlyList<LrclibRecord>>([record]);
        }
    }
}

public class KaraokeInterludeTests
{
    [Fact]
    public void Long_rests_and_the_intro_become_instrumental_breaks()
    {
        LyricsLine Line(double start, double end, string text) => new(TimeSpan.FromSeconds(start), text) { End = TimeSpan.FromSeconds(end) };
        var lines = KaraokeLyrics.WithInterludes([Line(12, 15, "one"), Line(15.5, 18, "two"), Line(30, 33, "three")]);
        Assert.Equal(["", "one", "two", "", "three"], lines.Select(l => l.Text));
        Assert.Equal(TimeSpan.Zero, lines[0].Time);              // the intro
        Assert.Equal(TimeSpan.FromSeconds(18), lines[3].Time);   // from the end of "two"
        Assert.Equal(3, KaraokeLyrics.WithInterludes([Line(1, 2, "a"), Line(2.5, 3, "b"), Line(4, 5, "c")]).Count); // short rests: none
    }

    [Fact]
    public void Several_backing_vocals_combine_with_their_anchors_in_place()
    {
        var a = new LyricsVocal("(oh", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), [new(TimeSpan.FromSeconds(1), 0)]);
        var b = new LyricsVocal("yeah)", TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), [new(TimeSpan.FromSeconds(3), 0)]);
        var both = LyricsVocal.Combine([b, a])!;
        Assert.Equal("(oh yeah)", both.Text);
        Assert.Equal(TimeSpan.FromSeconds(1), both.Start);
        Assert.Equal(TimeSpan.FromSeconds(4), both.End);
        Assert.Equal([0, 4], both.Anchors!.Select(x => x.TextOffset));
        Assert.True(LyricFlow.ForVocal(both, TimeSpan.FromSeconds(10)).IsComplete);
        Assert.False(LyricFlow.ForVocal(both, TimeSpan.FromSeconds(3.5)).IsComplete);
    }
}
