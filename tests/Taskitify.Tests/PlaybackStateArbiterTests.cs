using Taskitify.Core.Lyrics;
using Taskitify.Core.Media;

namespace Taskitify.Tests;

public class PlaybackStateArbiterTests
{
    private DateTimeOffset _now = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);

    private PlaybackStateArbiter Create() => new(() => _now);

    private void Advance(double ms) => _now += TimeSpan.FromMilliseconds(ms);

    private void Samples(PlaybackStateArbiter a, float peak, double ms, double step = 100)
    {
        for (double t = 0; t < ms; t += step)
        {
            a.Audio(peak);
            Advance(step);
        }
        a.Audio(peak);
    }

    [Fact]
    public void Without_audio_or_command_follows_the_report()
    {
        var a = Create();
        a.Reported(true);
        a.Audio(null);
        Assert.Null(a.PlayingOverride);
    }

    [Fact]
    public void Sound_while_reported_paused_means_playing()
    {
        // Measured: Spotify resumes sound 0.03 s after play, its session says Playing 3 s later.
        var a = Create();
        a.Reported(false);
        Samples(a, 0f, 2000);
        Assert.Null(a.PlayingOverride);
        var soundAt = _now;
        a.Audio(0.3f);
        Assert.True(a.PlayingOverride);
        Assert.Equal(soundAt, a.OverrideSince);
    }

    [Fact]
    public void Silence_after_sound_while_reported_playing_means_paused()
    {
        // Measured: Spotify's audio stops 0.4 s after a pause; the session may never say Paused.
        var a = Create();
        a.Reported(true);
        Samples(a, 0.3f, 1000);
        var silentAt = _now + TimeSpan.FromMilliseconds(100);
        Advance(100);
        Samples(a, 0f, 500);
        Assert.NotEqual(false, a.PlayingOverride); // a short gap is not a pause yet
        Samples(a, 0f, 400);
        Assert.False(a.PlayingOverride);
        Assert.Equal(silentAt, a.OverrideSince);

        a.Reported(false); // the session catches up
        Assert.Null(a.PlayingOverride);
    }

    [Fact]
    public void Silence_never_heard_as_sound_is_not_a_pause()
    {
        // e.g. Spotify muted from the start: nothing says it stopped.
        var a = Create();
        a.Reported(true);
        Samples(a, 0f, 3000);
        Assert.Null(a.PlayingOverride);
    }

    [Fact]
    public void Disproved_silence_stops_counting_as_pause_until_sound_returns()
    {
        var a = Create();
        a.Reported(true);
        Samples(a, 0.3f, 500);
        Samples(a, 0f, 1500);
        Assert.False(a.PlayingOverride);

        a.SilenceDisproved();
        Assert.Null(a.PlayingOverride);
        Samples(a, 0f, 3000);
        Assert.Null(a.PlayingOverride);

        Samples(a, 0.3f, 200);
        Samples(a, 0f, 1000);
        Assert.False(a.PlayingOverride);
    }

    [Fact]
    public void Pause_command_shows_at_once_and_holds_through_the_fade()
    {
        var a = Create();
        a.Reported(true);
        Samples(a, 0.3f, 1000);
        var sentAt = _now;
        a.Commanded(false);
        Assert.False(a.PlayingOverride);
        Assert.Equal(sentAt, a.OverrideSince);

        Samples(a, 0.1f, 400); // Spotify fades out
        Assert.False(a.PlayingOverride);
        Samples(a, 0f, 3000); // the session still says Playing
        Assert.False(a.PlayingOverride);
    }

    [Fact]
    public void Ignored_pause_command_gives_way_to_the_sound()
    {
        var a = Create();
        a.Reported(true);
        Samples(a, 0.3f, 500);
        a.Commanded(false);
        Samples(a, 0.3f, 2800);
        Assert.False(a.PlayingOverride);
        Samples(a, 0.3f, 300);
        Assert.True(a.PlayingOverride);
    }

    [Fact]
    public void Play_command_shows_at_once_and_sound_confirms_it()
    {
        var a = Create();
        a.Reported(false);
        Samples(a, 0f, 1000);
        a.Commanded(true);
        Assert.True(a.PlayingOverride);
        Samples(a, 0f, 1000); // buffering
        Assert.True(a.PlayingOverride);
        Samples(a, 0.3f, 100);
        Assert.True(a.PlayingOverride);
    }

    [Fact]
    public void Refused_command_is_forgotten()
    {
        var a = Create();
        a.Reported(false);
        Samples(a, 0f, 500);
        a.Commanded(true);
        a.CommandFailed();
        Assert.Null(a.PlayingOverride);
    }

    [Fact]
    public void Without_audio_a_command_waits_for_the_report()
    {
        var a = Create();
        a.Audio(null);
        a.Reported(true);
        a.Commanded(false);
        Advance(3000);
        Assert.False(a.PlayingOverride);
        a.Reported(false);
        Assert.Null(a.PlayingOverride);

        a.Commanded(true);
        Advance(5000);
        Assert.Null(a.PlayingOverride); // the report never agreed: back to it
    }
}

public class PlaybackClockSinceTests
{
    [Fact]
    public void Pause_noticed_late_stops_where_it_happened()
    {
        var now = new DateTimeOffset(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);
        var clock = new PlaybackClock(() => now);
        clock.Sync(TimeSpan.FromSeconds(10), now, playing: true);
        now += TimeSpan.FromSeconds(5);
        clock.SetPlaying(false, now - TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(14), clock.Position);
        now += TimeSpan.FromSeconds(10);
        Assert.Equal(TimeSpan.FromSeconds(14), clock.Position);
    }

    [Fact]
    public void Resume_noticed_late_counts_from_when_it_happened()
    {
        var now = new DateTimeOffset(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);
        var clock = new PlaybackClock(() => now);
        clock.Sync(TimeSpan.FromSeconds(10), now, playing: false);
        now += TimeSpan.FromSeconds(5);
        clock.SetPlaying(true, now - TimeSpan.FromMilliseconds(200));
        Assert.Equal(TimeSpan.FromSeconds(10.2), clock.Position);
    }

    [Fact]
    public void Since_before_the_last_report_is_ignored()
    {
        var now = new DateTimeOffset(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);
        var clock = new PlaybackClock(() => now);
        clock.Sync(TimeSpan.FromSeconds(10), now, playing: true);
        now += TimeSpan.FromSeconds(2);
        clock.SetPlaying(false, now - TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Position);
    }
}
