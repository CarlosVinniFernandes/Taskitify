using Taskitify.Core.Lyrics;

namespace Taskitify.Tests;

public class LyricsTimelineTests
{
    private static readonly LyricsTimeline Timeline = new(
    [
        new(TimeSpan.FromSeconds(5), "a"),
        new(TimeSpan.FromSeconds(10), "b"),
        new(TimeSpan.FromSeconds(10), "b2"),
        new(TimeSpan.FromSeconds(20), "c"),
    ]);

    [Theory]
    [InlineData(0, -1)]
    [InlineData(4.999, -1)]
    [InlineData(5, 0)]
    [InlineData(9.9, 0)]
    [InlineData(10, 2)] // same timestamp: the last one wins
    [InlineData(19.99, 2)]
    [InlineData(20, 3)]
    [InlineData(999, 3)]
    public void IndexAt_returns_last_line_started(double seconds, int expected)
    {
        Assert.Equal(expected, Timeline.IndexAt(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void NextChangeAfter_points_to_following_line()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), Timeline.NextChangeAfter(TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromSeconds(10), Timeline.NextChangeAfter(TimeSpan.FromSeconds(6)));
        Assert.Equal(TimeSpan.FromSeconds(20), Timeline.NextChangeAfter(TimeSpan.FromSeconds(10)));
        Assert.Null(Timeline.NextChangeAfter(TimeSpan.FromSeconds(25)));
    }

    [Fact]
    public void Empty_timeline_is_safe()
    {
        var empty = new LyricsTimeline([]);
        Assert.Equal(-1, empty.IndexAt(TimeSpan.FromSeconds(3)));
        Assert.Null(empty.NextChangeAfter(TimeSpan.Zero));
    }
}

public class PlaybackClockTests
{
    private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private PlaybackClock NewClock() => new(() => _now);

    private void Advance(double seconds) => _now += TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Projects_while_playing()
    {
        var clock = NewClock();
        clock.Sync(TimeSpan.FromSeconds(30), _now, playing: true);
        Advance(2.5);
        Assert.Equal(TimeSpan.FromSeconds(32.5), clock.Position);
    }

    [Fact]
    public void Accounts_for_report_latency()
    {
        var clock = NewClock();
        var sampledAt = _now;
        Advance(0.4); // the event reached us 400 ms after the session sampled it
        clock.Sync(TimeSpan.FromSeconds(10), sampledAt, playing: true);
        Assert.Equal(TimeSpan.FromSeconds(10.4), clock.Position);
    }

    [Fact]
    public void Freezes_on_pause_and_resumes_from_same_point()
    {
        var clock = NewClock();
        clock.Sync(TimeSpan.FromSeconds(10), _now, playing: true);
        Advance(5);
        clock.SetPlaying(false);
        Advance(60);
        Assert.Equal(TimeSpan.FromSeconds(15), clock.Position);

        clock.SetPlaying(true);
        Advance(1);
        Assert.Equal(TimeSpan.FromSeconds(16), clock.Position);
    }

    [Fact]
    public void Seek_report_replaces_projection()
    {
        var clock = NewClock();
        clock.Sync(TimeSpan.FromSeconds(10), _now, playing: true);
        Advance(3);
        clock.Sync(TimeSpan.FromSeconds(120), _now, playing: true);
        Advance(1);
        Assert.Equal(TimeSpan.FromSeconds(121), clock.Position);
    }

    [Fact]
    public void Paused_report_is_not_projected()
    {
        var clock = NewClock();
        var sampledAt = _now;
        Advance(10);
        clock.Sync(TimeSpan.FromSeconds(42), sampledAt, playing: false);
        Advance(10);
        Assert.Equal(TimeSpan.FromSeconds(42), clock.Position);
    }

    [Fact]
    public void Clamps_to_duration_and_zero()
    {
        var clock = NewClock();
        clock.Duration = TimeSpan.FromSeconds(100);
        clock.Sync(TimeSpan.FromSeconds(99), _now, playing: true);
        Advance(10);
        Assert.Equal(TimeSpan.FromSeconds(100), clock.Position);

        clock.Sync(TimeSpan.FromSeconds(-5), _now, playing: false);
        Assert.Equal(TimeSpan.Zero, clock.Position);
    }

    [Fact]
    public void Ignores_absurd_report_timestamps()
    {
        var clock = NewClock();
        clock.Sync(TimeSpan.FromSeconds(10), DateTimeOffset.MinValue, playing: true);
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Position);

        clock.Sync(TimeSpan.FromSeconds(10), _now.AddMinutes(5), playing: true); // future
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Position);
    }

    [Fact]
    public void Reset_starts_new_track_at_zero()
    {
        var clock = NewClock();
        clock.Sync(TimeSpan.FromSeconds(200), _now, playing: true);
        clock.Reset();
        Advance(1);
        Assert.Equal(TimeSpan.FromSeconds(1), clock.Position);
    }

    [Fact]
    public void Clock_and_timeline_follow_a_seek_back()
    {
        var timeline = new LyricsTimeline(LrcParser.Parse("[00:05.00]a\n[00:10.00]b\n[00:15.00]c"));
        var clock = NewClock();
        clock.Sync(TimeSpan.FromSeconds(14), _now, playing: true);
        Advance(2);
        Assert.Equal("c", timeline.Lines[timeline.IndexAt(clock.Position)].Text);

        clock.Sync(TimeSpan.FromSeconds(6), _now, playing: true);
        Assert.Equal("a", timeline.Lines[timeline.IndexAt(clock.Position)].Text);
    }
}
