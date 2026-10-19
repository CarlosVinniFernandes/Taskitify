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
    public async Task Retry_after_a_fallback_failure_does_not_ask_the_primary_again()
    {
        var now = DateTimeOffset.UtcNow;
        var primary = Returns("Spotify", null);
        var fallback = new FakeProvider("LRCLIB", false, _ => Task.FromException<LrclibRecord?>(new HttpRequestException("429")));
        var chain = new FallbackLyricsProvider(primary, fallback, () => now);

        // The failure surfaces (LyricsSyncService retries); the retry asks only the fallback.
        await Assert.ThrowsAsync<HttpRequestException>(() => chain.FindAsync(Query, default));
        await Assert.ThrowsAsync<HttpRequestException>(() => chain.FindAsync(Query, default));
        Assert.Equal(1, primary.Calls);
        Assert.Equal(2, fallback.Calls);

        now += TimeSpan.FromMinutes(11);
        await Assert.ThrowsAsync<HttpRequestException>(() => chain.FindAsync(Query, default));
        Assert.Equal(2, primary.Calls);
    }

    [Fact]
    public async Task Retries_do_not_extend_the_remembered_primary_answer()
    {
        var now = DateTimeOffset.UtcNow;
        var primary = Returns("Spotify", null);
        var fallback = new FakeProvider("LRCLIB", false, _ => Task.FromException<LrclibRecord?>(new HttpRequestException("429")));
        var chain = new FallbackLyricsProvider(primary, fallback, () => now);

        await Assert.ThrowsAsync<HttpRequestException>(() => chain.FindAsync(Query, default));
        now += TimeSpan.FromMinutes(6);
        await Assert.ThrowsAsync<HttpRequestException>(() => chain.FindAsync(Query, default));
        Assert.Equal(1, primary.Calls);

        // 11 min after the primary answered, though only 5 after the last retry: asked again.
        now += TimeSpan.FromMinutes(5);
        await Assert.ThrowsAsync<HttpRequestException>(() => chain.FindAsync(Query, default));
        Assert.Equal(2, primary.Calls);
    }

    [Fact]
    public async Task Reloading_a_song_asks_the_primary_again()
    {
        // As after signing in to Spotify: its "nothing" was remembered while LRCLIB was failing.
        var primary = Returns("Spotify", null);
        var fallback = new FakeProvider("LRCLIB", false, _ => Task.FromException<LrclibRecord?>(new HttpRequestException("429")));
        var service = new LyricsService([new FallbackLyricsProvider(primary, fallback)], null);

        Assert.Equal(LyricsKind.Error, (await service.GetLyricsAsync(Query, default)).Kind);
        Assert.Equal(LyricsKind.Error, (await service.GetLyricsAsync(Query, default)).Kind);
        Assert.Equal(1, primary.Calls);

        await service.ForgetAsync(Query);
        await service.GetLyricsAsync(Query, default);
        Assert.Equal(2, primary.Calls);
    }

    [Fact]
    public async Task Answers_that_need_no_retry_are_not_remembered()
    {
        var primary = Returns("Spotify", null);
        var chain = new FallbackLyricsProvider(primary, Returns("LRCLIB", null));

        await chain.FindAsync(Query, default);
        await chain.FindAsync(Query, default);

        // "Not found" is cached by LyricsService; a reload (after signing in) asks the primary again.
        Assert.Equal(2, primary.Calls);
    }

    [Fact]
    public async Task Retries_after_a_primary_failure_ask_only_the_primary()
    {
        var calls = 0;
        var primary = new FakeProvider("Spotify", false, _ => ++calls < 3
            ? Task.FromException<LrclibRecord?>(new HttpRequestException("503"))
            : Task.FromResult<LrclibRecord?>(Synced("Primary")));
        var fallback = Returns("LRCLIB", null);
        var service = new LyricsService([new FallbackLyricsProvider(primary, fallback)], null);

        Assert.Equal(LyricsKind.Error, (await service.GetLyricsAsync(Query, default)).Kind);
        Assert.Equal(LyricsKind.Error, (await service.GetLyricsAsync(Query, default)).Kind);
        var recovered = await service.GetLyricsAsync(Query, default);

        // LRCLIB's "nothing" is reused by the retries; only the source that failed is asked again.
        Assert.Equal(("Primary", "Spotify"), (Assert.Single(recovered.Lines).Text, recovered.Source));
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task Primary_failures_are_not_remembered()
    {
        var failing = new FakeProvider("Spotify", false, _ => Task.FromException<LrclibRecord?>(new HttpRequestException("down")));
        var chain = new FallbackLyricsProvider(failing, Returns("LRCLIB", null));

        await Assert.ThrowsAsync<HttpRequestException>(() => chain.FindAsync(Query, default));
        await Assert.ThrowsAsync<HttpRequestException>(() => chain.FindAsync(Query, default));

        Assert.Equal(2, failing.Calls);
    }

    [Fact]
    public async Task Results_name_the_provider_that_found_them_and_whether_they_were_cached()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TaskitifyTests", Guid.NewGuid().ToString("N"));
        try
        {
            var chain = new FallbackLyricsProvider(Returns("Spotify", null), Returns("LRCLIB", Synced("Fallback")));
            var fresh = await new LyricsService([chain], new LyricsCache(dir)).GetLyricsAsync(Query, default);
            Assert.Equal(("LRCLIB", false), (fresh.Source, fresh.FromCache));

            // A new service starts from the disk cache, which keeps the source.
            var service = new LyricsService([chain], new LyricsCache(dir));
            var fromDisk = await service.GetLyricsAsync(Query, default);
            var fromMemory = await service.GetLyricsAsync(Query, default);
            Assert.Equal(("LRCLIB", true), (fromDisk.Source, fromDisk.FromCache));
            Assert.Equal(("LRCLIB", true), (fromMemory.Source, fromMemory.FromCache));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Primary_and_local_results_carry_their_own_names()
    {
        var primary = await new FallbackLyricsProvider(Returns("Spotify", Synced("Primary")), Returns("LRCLIB", null)).FindAsync(Query, default);
        Assert.Equal("Spotify", primary?.Source);

        var local = await new LyricsService([Returns("Local files", Synced("Mine"), isLocal: true)], null).GetLyricsAsync(Query, default);
        Assert.Equal("Local files", local.Source);
    }

    [Fact]
    public async Task Cache_entries_saved_before_sources_were_recorded_are_looked_up_again()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TaskitifyTests", Guid.NewGuid().ToString("N"));
        try
        {
            // Possibly LRCLIB's version from a time Spotify was failing: kept only for a while.
            var savedAt = DateTimeOffset.Parse("2026-10-01T00:00:00+00:00");
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, LyricsCache.KeyFor(Query) + ".json"),
                """{"version":2,"kind":0,"synced":"[00:01.00]Old","plain":null,"matchedTitle":"No. 1 Party Anthem","matchedArtist":"Arctic Monkeys","savedAt":"2026-10-01T00:00:00+00:00"}""");

            var fresh = await new LyricsCache(dir, () => savedAt.AddHours(1)).TryGetAsync(Query, default);
            Assert.Equal("Old", Assert.Single(fresh!.Lines).Text);
            Assert.Null(fresh.Source);

            var later = new LyricsCache(dir, () => savedAt.AddHours(7));
            Assert.Null(await later.TryGetAsync(Query, default));
            var service = new LyricsService([new FallbackLyricsProvider(Returns("Spotify", Synced("New")), Returns("LRCLIB", null))], later);
            var again = await service.GetLyricsAsync(Query, default);
            Assert.Equal(("New", "Spotify", false), (Assert.Single(again.Lines).Text, again.Source, again.FromCache));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Lyrics_found_while_the_primary_failed_are_kept_only_for_a_while()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TaskitifyTests", Guid.NewGuid().ToString("N"));
        try
        {
            var now = DateTimeOffset.UtcNow;
            var calls = 0;
            var primary = new FakeProvider("Spotify", false, _ => ++calls == 1
                ? Task.FromException<LrclibRecord?>(new HttpRequestException("503"))
                : Task.FromResult<LrclibRecord?>(Synced("Primary")));
            var chain = new FallbackLyricsProvider(primary, Returns("LRCLIB", Synced("Fallback")));

            var first = await new LyricsService([chain], new LyricsCache(dir, () => now)).GetLyricsAsync(Query, default);
            var soon = await new LyricsService([chain], new LyricsCache(dir, () => now.AddHours(1))).GetLyricsAsync(Query, default);
            var later = await new LyricsService([chain], new LyricsCache(dir, () => now.AddHours(7))).GetLyricsAsync(Query, default);

            Assert.Equal(("Fallback", "LRCLIB", true), (Assert.Single(first.Lines).Text, first.Source, first.Provisional));
            Assert.Equal(("Fallback", true), (Assert.Single(soon.Lines).Text, soon.FromCache));
            Assert.Equal(("Primary", "Spotify"), (Assert.Single(later.Lines).Text, later.Source));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Lyrics_only_the_fallback_has_stay_cached()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TaskitifyTests", Guid.NewGuid().ToString("N"));
        try
        {
            var now = DateTimeOffset.UtcNow;
            var primary = Returns("Spotify", null);
            var chain = new FallbackLyricsProvider(primary, Returns("LRCLIB", Synced("Fallback")));

            var first = await new LyricsService([chain], new LyricsCache(dir, () => now)).GetLyricsAsync(Query, default);
            var later = await new LyricsService([chain], new LyricsCache(dir, () => now.AddDays(30))).GetLyricsAsync(Query, default);

            // The primary answered (no lyrics): the fallback's version is the real answer.
            Assert.False(first.Provisional);
            Assert.Equal(("Fallback", true), (Assert.Single(later.Lines).Text, later.FromCache));
            Assert.Equal(1, primary.Calls);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task An_expired_stand_in_beats_no_lyrics_when_every_source_fails()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TaskitifyTests", Guid.NewGuid().ToString("N"));
        try
        {
            var now = DateTimeOffset.UtcNow;
            var failing = new FakeProvider("Spotify", false, _ => Task.FromException<LrclibRecord?>(new HttpRequestException("offline")));
            await new LyricsService([new FallbackLyricsProvider(failing, Returns("LRCLIB", Synced("Fallback")))], new LyricsCache(dir, () => now))
                .GetLyricsAsync(Query, default);

            // Offline a day later: both sources fail, and the expired stand-in is shown instead of an error.
            var offline = new FakeProvider("LRCLIB", false, _ => Task.FromException<LrclibRecord?>(new HttpRequestException("offline")));
            var result = await new LyricsService([new FallbackLyricsProvider(failing, offline)], new LyricsCache(dir, () => now.AddDays(1)))
                .GetLyricsAsync(Query, default);

            Assert.Equal(("Fallback", true), (Assert.Single(result.Lines).Text, result.FromCache));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
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
