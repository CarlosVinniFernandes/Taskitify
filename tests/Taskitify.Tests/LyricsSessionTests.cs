using Taskitify.Core.Lyrics;
using Taskitify.Core.Media;

namespace Taskitify.Tests;

public class LyricsSessionTests
{
    private sealed class FakeMedia : IMediaSource
    {
        public bool HasSession => Status != MediaPlaybackStatus.NoSession;
        public MediaTrack? Track { get; set; }
        public MediaPlaybackStatus Status { get; set; } = MediaPlaybackStatus.Playing;
        public TimeSpan Position { get; set; }
        public bool CanSeek => true;
        public bool CanGoNext => true;
        public bool CanGoPrevious => true;
        public bool IsShuffleActive => false;
        public MediaRepeatMode Repeat => MediaRepeatMode.None;
        public event EventHandler? TrackChanged;
        public event EventHandler? PlaybackChanged;
        public event EventHandler? TimelineChanged;
        public void RaiseTrack() => TrackChanged?.Invoke(this, EventArgs.Empty);
        public void RaisePlayback() => PlaybackChanged?.Invoke(this, EventArgs.Empty);
        public void RaiseTimeline() => TimelineChanged?.Invoke(this, EventArgs.Empty);
        public Task StartAsync() => Task.CompletedTask;
        public Task PlayPauseAsync() => Task.CompletedTask;
        public Task NextAsync() => Task.CompletedTask;
        public Task PreviousAsync() => Task.CompletedTask;
        public Task SeekAsync(TimeSpan position) => Task.CompletedTask;
        public Task ToggleShuffleAsync() => Task.CompletedTask;
        public Task CycleRepeatAsync() => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class FakeLyrics(Func<TrackQuery, LyricsResult> answer) : ILyricsService
    {
        public List<TrackQuery> Queries { get; } = [];
        public Task<LyricsResult> GetLyricsAsync(TrackQuery query, CancellationToken ct)
        {
            lock (Queries) Queries.Add(query);
            return Task.FromResult(answer(query));
        }
        public Task ForgetAsync(TrackQuery query) => Task.CompletedTask;
    }

    private static LyricsResult Synced() => new(LyricsKind.Synced,
        [new LyricsLine(TimeSpan.FromSeconds(1), "one"), new LyricsLine(TimeSpan.FromSeconds(5), "two"), new LyricsLine(TimeSpan.FromSeconds(9), "three")],
        null) { Source = "LRCLIB" };

    private static FakeMedia NewMedia() => new()
    {
        Track = new MediaTrack("Song", "Artist", "Album", TimeSpan.FromSeconds(30), null),
    };

    private static async Task<bool> WaitFor(Func<bool> condition, int ms = 3000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end)
        {
            if (condition())
                return true;
            await Task.Delay(10);
        }
        return condition();
    }

    [Fact]
    public async Task Loads_synced_lyrics_for_the_current_track_and_picks_the_line_at_the_position()
    {
        var media = NewMedia();
        media.Position = TimeSpan.FromSeconds(6);
        var lyrics = new FakeLyrics(_ => Synced());
        using var session = new LyricsSession(media, lyrics) { LoadDebounce = TimeSpan.Zero };

        await session.InitializeAsync();

        Assert.Equal(LyricsSessionState.Synced, session.State);
        Assert.Equal("LRCLIB", session.Source);
        Assert.Equal(1, session.CurrentIndex);
        Assert.Equal("two", session.CurrentLine!.Text);
        Assert.Equal(("Song", "Artist"), (lyrics.Queries[0].Title, lyrics.Queries[0].Artist));
        Assert.Equal(TimeSpan.FromSeconds(30), lyrics.Queries[0].Duration);
    }

    [Fact]
    public async Task Offset_shifts_the_active_line()
    {
        var media = NewMedia();
        media.Position = TimeSpan.FromSeconds(4);
        using var session = new LyricsSession(media, new FakeLyrics(_ => Synced())) { LoadDebounce = TimeSpan.Zero };
        await session.InitializeAsync();
        Assert.Equal(0, session.CurrentIndex);

        session.OffsetMs = 1500;

        Assert.Equal(1, session.CurrentIndex);
    }

    [Fact]
    public async Task Line_advances_by_itself_while_playing()
    {
        var media = NewMedia();
        var clockStart = DateTime.UtcNow;
        media.Position = TimeSpan.Zero;
        var lyrics = new FakeLyrics(_ => new LyricsResult(LyricsKind.Synced,
            [new LyricsLine(TimeSpan.FromMilliseconds(100), "a"), new LyricsLine(TimeSpan.FromMilliseconds(300), "b")], null));
        using var session = new LyricsSession(media, lyrics) { LoadDebounce = TimeSpan.Zero };
        await session.InitializeAsync();
        Assert.Equal(-1, session.CurrentIndex);

        // Position is projected by the source; emulate a source whose position follows the wall clock.
        media.Position = TimeSpan.Zero;
        var ticker = Task.Run(async () =>
        {
            while (DateTime.UtcNow - clockStart < TimeSpan.FromSeconds(2))
            {
                media.Position = DateTime.UtcNow - clockStart;
                await Task.Delay(5);
            }
        });
        media.RaiseTimeline();

        Assert.True(await WaitFor(() => session.CurrentIndex == 1, 2500));
        await ticker;
    }

    [Fact]
    public async Task Paused_playback_keeps_the_line_but_arms_no_timer()
    {
        var media = NewMedia();
        media.Position = TimeSpan.FromSeconds(6);
        media.Status = MediaPlaybackStatus.Paused;
        using var session = new LyricsSession(media, new FakeLyrics(_ => Synced())) { LoadDebounce = TimeSpan.Zero };
        await session.InitializeAsync();

        Assert.Equal(1, session.CurrentIndex);
    }

    [Fact]
    public async Task Track_change_cancels_the_stale_lookup_and_loads_the_new_song()
    {
        var media = NewMedia();
        var lyrics = new FakeLyrics(q => q.Title == "Song2"
            ? new LyricsResult(LyricsKind.Synced, [new LyricsLine(TimeSpan.Zero, "second")], null)
            : Synced());
        using var session = new LyricsSession(media, lyrics) { LoadDebounce = TimeSpan.FromMilliseconds(30) };
        await session.InitializeAsync();

        media.Track = new MediaTrack("Song2", "Artist", "Album", TimeSpan.FromSeconds(20), null);
        media.Position = TimeSpan.Zero;
        media.RaiseTrack();

        Assert.True(await WaitFor(() => session.CurrentLine?.Text == "second"));
    }

    [Fact]
    public async Task No_session_clears_the_state()
    {
        var media = NewMedia();
        using var session = new LyricsSession(media, new FakeLyrics(_ => Synced())) { LoadDebounce = TimeSpan.Zero };
        await session.InitializeAsync();

        media.Track = null;
        media.Status = MediaPlaybackStatus.NoSession;
        media.RaiseTrack();

        Assert.True(await WaitFor(() => session.State == LyricsSessionState.None));
        Assert.Null(session.CurrentLine);
    }

    [Theory]
    [InlineData(LyricsKind.NotFound, LyricsSessionState.NotFound)]
    [InlineData(LyricsKind.Instrumental, LyricsSessionState.Instrumental)]
    [InlineData(LyricsKind.Error, LyricsSessionState.Error)]
    public async Task Maps_non_synced_results_to_states(LyricsKind kind, LyricsSessionState expected)
    {
        var media = NewMedia();
        using var session = new LyricsSession(media, new FakeLyrics(_ => new LyricsResult(kind, [], null, ErrorMessage: kind == LyricsKind.Error ? "boom" : null)))
        { LoadDebounce = TimeSpan.Zero };

        await session.InitializeAsync();

        Assert.Equal(expected, session.State);
        if (kind == LyricsKind.Error)
            Assert.Equal("boom", session.ErrorMessage);
    }

    [Fact]
    public async Task Plain_only_lyrics_are_exposed_as_text()
    {
        var media = NewMedia();
        using var session = new LyricsSession(media, new FakeLyrics(_ => new LyricsResult(LyricsKind.PlainOnly, [], "la la la")))
        { LoadDebounce = TimeSpan.Zero };

        await session.InitializeAsync();

        Assert.Equal(LyricsSessionState.PlainOnly, session.State);
        Assert.Equal("la la la", session.PlainText);
    }
}
