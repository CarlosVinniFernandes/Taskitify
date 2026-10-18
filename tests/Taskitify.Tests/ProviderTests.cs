using Taskitify.Core.Lyrics;

namespace Taskitify.Tests;

public sealed class LocalLyricsProviderTests : IDisposable
{
    private static readonly TrackQuery Query = new("No. 1 Party Anthem", "Arctic Monkeys", "AM", TimeSpan.FromSeconds(243));

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "TaskitifyTests", Guid.NewGuid().ToString("N"));

    public LocalLyricsProviderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private void Write(string relativePath, string text)
    {
        var path = Path.Combine(_dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public async Task Matches_by_file_name()
    {
        Write("Arctic Monkeys - No. 1 Party Anthem.lrc", "[00:01.00]Mine");

        var record = await new LocalLyricsProvider(_dir).FindAsync(Query, default);

        Assert.Equal("Mine", Assert.Single(LyricsService.FromRecord(record).Lines).Text);
    }

    [Fact]
    public async Task Matches_by_tags_in_subfolders_whatever_the_file_name()
    {
        Write(Path.Combine("AM", "track03.lrc"), "[ti:No. 1 Party Anthem]\r\n[ar:Arctic Monkeys]\r\n[length:04:03]\r\n[00:01.00]Tagged");

        var record = await new LocalLyricsProvider(_dir).FindAsync(Query, default);

        Assert.Equal("Tagged", Assert.Single(LyricsService.FromRecord(record).Lines).Text);
    }

    [Fact]
    public async Task Rejects_other_songs_and_other_lengths()
    {
        Write("Arctic Monkeys - Do I Wanna Know.lrc", "[00:01.00]Other song");
        Write("live.lrc", "[ti:No. 1 Party Anthem]\n[ar:Arctic Monkeys]\n[length:5:30]\n[00:01.00]Live version");

        Assert.Null(await new LocalLyricsProvider(_dir).FindAsync(Query, default));
    }

    [Fact]
    public async Task Untimed_file_is_plain_lyrics_without_tag_lines()
    {
        Write("Arctic Monkeys - No. 1 Party Anthem.lrc", "[ar:Arctic Monkeys]\nLine one\nLine two\n");

        var result = LyricsService.FromRecord(await new LocalLyricsProvider(_dir).FindAsync(Query, default));

        Assert.Equal(LyricsKind.PlainOnly, result.Kind);
        Assert.Equal("Line one\nLine two", result.PlainText);
    }

    [Fact]
    public async Task Missing_folder_finds_nothing()
    {
        Assert.Null(await new LocalLyricsProvider(Path.Combine(_dir, "nope")).FindAsync(Query, default));
    }
}

public class ProviderChainTests
{
    private static readonly TrackQuery Query = new("No. 1 Party Anthem", "Arctic Monkeys", "AM", TimeSpan.FromSeconds(243));

    private sealed class FakeProvider(string name, bool isLocal, Func<CancellationToken, Task<LrclibRecord?>> find) : ILyricsProvider
    {
        public int Calls { get; private set; }

        public string Name => name;

        public bool IsLocal => isLocal;

        public Task<LrclibRecord?> FindAsync(TrackQuery query, CancellationToken ct)
        {
            Calls++;
            return find(ct);
        }
    }

    private static FakeProvider Returns(string name, LrclibRecord? record, bool isLocal = false) =>
        new(name, isLocal, _ => Task.FromResult(record));

    private static LrclibRecord Synced(string text) => TrackMatcherTests.Record("No. 1 Party Anthem", "Arctic Monkeys", 243, $"[00:01.00]{text}");

    private static LrclibRecord Plain() => TrackMatcherTests.Record("No. 1 Party Anthem", "Arctic Monkeys", 243, synced: null);

    [Fact]
    public async Task All_remote_providers_are_started_even_when_one_completes_immediately()
    {
        var first = Returns("first", Synced("First"));
        var second = Returns("second", Synced("Second"));

        var result = await new LyricsService([first, second], null).GetLyricsAsync(Query, default);

        Assert.Equal("First", Assert.Single(result.Lines).Text);
        Assert.Equal(1, second.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fastest_source_wins_and_cancels_the_other_source(bool spotifyWins)
    {
        var spotify = new TaskCompletionSource<LrclibRecord?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new TaskCompletionSource<LrclibRecord?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken spotifyToken = default, apiToken = default;
        var service = new LyricsService([
            new FakeProvider("Spotify", false, ct => { spotifyToken = ct; return spotify.Task; }),
            new FakeProvider("API", false, ct => { apiToken = ct; return api.Task; })], null);

        var lookup = service.GetLyricsAsync(Query, default);
        Assert.True(spotifyToken.CanBeCanceled);
        Assert.True(apiToken.CanBeCanceled);
        (spotifyWins ? spotify : api).SetResult(Synced("Winner"));

        var result = await lookup.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Winner", Assert.Single(result.Lines).Text);
        Assert.True((spotifyWins ? apiToken : spotifyToken).IsCancellationRequested);
        (spotifyWins ? api : spotify).SetResult(Synced("Late"));
        Assert.Equal("Winner", Assert.Single((await service.GetLyricsAsync(Query, default)).Lines).Text);
    }

    [Fact]
    public async Task Caller_cancellation_stops_all_sources_even_if_they_ignore_the_token()
    {
        var never = new TaskCompletionSource<LrclibRecord?>();
        using var cts = new CancellationTokenSource();
        var service = new LyricsService([
            new FakeProvider("one", false, _ => never.Task),
            new FakeProvider("two", false, _ => never.Task)], null);
        var lookup = service.GetLyricsAsync(Query, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lookup.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Malformed_timestamps_do_not_win_over_valid_lyrics()
    {
        var invalid = Synced("Invalid") with { SyncedLyrics = "not a timestamp", PlainLyrics = null };
        var result = await new LyricsService([
            Returns("invalid", invalid), Returns("valid", Synced("Valid"))], null).GetLyricsAsync(Query, default);
        Assert.Equal("Valid", Assert.Single(result.Lines).Text);
    }

    [Fact]
    public async Task Unresponsive_source_is_bounded_even_when_it_ignores_cancellation()
    {
        var never = new TaskCompletionSource<LrclibRecord?>();
        var service = new LyricsService([new FakeProvider("stuck", false, _ => never.Task)], null)
        { ProviderTimeout = TimeSpan.FromMilliseconds(50) };
        var result = await service.GetLyricsAsync(Query, default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(LyricsKind.Error, result.Kind);
    }

    [Fact]
    public async Task Missing_or_plain_lyrics_fall_through_to_synced_ones()
    {
        var plain = Returns("plain", Plain());
        var none = Returns("none", null);
        var synced = Returns("synced", Synced("Later"));

        var result = await new LyricsService([plain, none, synced], null).GetLyricsAsync(Query, default);

        Assert.Equal("Later", Assert.Single(result.Lines).Text);
    }

    [Fact]
    public async Task Plain_lyrics_are_kept_when_nobody_has_synced_ones()
    {
        var result = await new LyricsService([Returns("plain", Plain()), Returns("none", null)], null).GetLyricsAsync(Query, default);

        Assert.Equal(LyricsKind.PlainOnly, result.Kind);
    }

    [Fact]
    public async Task Slow_provider_times_out_and_the_next_one_answers()
    {
        var slow = new FakeProvider("slow", false, async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        });
        var service = new LyricsService([slow, Returns("fallback", Synced("Fallback"))], null) { ProviderTimeout = TimeSpan.FromMilliseconds(50) };

        var result = await service.GetLyricsAsync(Query, default);

        Assert.Equal("Fallback", Assert.Single(result.Lines).Text);
    }

    [Fact]
    public async Task Failing_provider_falls_back_and_an_error_is_reported_only_when_nothing_was_found()
    {
        var failing = new FakeProvider("failing", false, _ => Task.FromException<LrclibRecord?>(new HttpRequestException("down")));

        var found = await new LyricsService([failing, Returns("ok", Synced("Ok"))], null).GetLyricsAsync(Query, default);
        Assert.Equal(LyricsKind.Synced, found.Kind);

        var nothing = await new LyricsService([failing, Returns("none", null)], null).GetLyricsAsync(Query, default);
        Assert.Equal(LyricsKind.Error, nothing.Kind);
    }

    [Fact]
    public async Task Fallback_is_not_asked_when_the_primary_has_synced_lyrics()
    {
        var fallback = Returns("LRCLIB", Synced("Fallback"));

        var record = await new FallbackLyricsProvider(Returns("Spotify", Synced("Primary")), fallback).FindAsync(Query, default);

        Assert.Equal("Primary", Assert.Single(LyricsService.FromRecord(record).Lines).Text);
        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public async Task Fallback_supplies_synced_lyrics_when_the_primary_has_none_or_plain_ones()
    {
        foreach (var primary in new[] { Returns("Spotify", null), Returns("Spotify", Plain()) })
        {
            var record = await new FallbackLyricsProvider(primary, Returns("LRCLIB", Synced("Fallback"))).FindAsync(Query, default);

            Assert.Equal("Fallback", Assert.Single(LyricsService.FromRecord(record).Lines).Text);
        }
    }

    [Fact]
    public async Task Primary_plain_lyrics_win_when_the_fallback_has_no_synced_ones()
    {
        var primary = Plain() with { PlainLyrics = "Primary words" };

        var record = await new FallbackLyricsProvider(Returns("Spotify", primary), Returns("LRCLIB", Plain())).FindAsync(Query, default);

        Assert.Equal("Primary words", record?.PlainLyrics);
    }

    [Fact]
    public async Task Primary_failure_falls_back_and_surfaces_only_when_nothing_was_found()
    {
        var failing = new FakeProvider("Spotify", false, _ => Task.FromException<LrclibRecord?>(new HttpRequestException("denied")));

        var found = await new FallbackLyricsProvider(failing, Returns("LRCLIB", Synced("Fallback"))).FindAsync(Query, default);
        Assert.Equal(LyricsKind.Synced, LyricsService.FromRecord(found).Kind);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new FallbackLyricsProvider(failing, Returns("LRCLIB", null)).FindAsync(Query, default));
    }

    [Fact]
    public async Task Local_files_come_before_the_cache_and_are_not_cached()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TaskitifyTests", Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new LyricsCache(dir);
            await cache.SaveAsync(Query, LyricsResult.NotFound, null, default);
            var local = Returns("local", Synced("Local"), isLocal: true);
            var remote = Returns("remote", Synced("Remote"));
            var service = new LyricsService([remote, local], cache);

            Assert.Equal("Local", Assert.Single((await service.GetLyricsAsync(Query, default)).Lines).Text);
            Assert.Equal(0, remote.Calls);
            Assert.Equal(LyricsKind.NotFound, (await cache.TryGetAsync(Query, default))?.Kind);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
