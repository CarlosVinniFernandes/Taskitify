using Taskitify.Core.Lyrics;

namespace Taskitify.Tests;

public class LyricFlowTests
{
    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    private static LyricFlow Flow(string lrc, double? duration = null) =>
        new(new LyricsTimeline(LrcParser.Parse(lrc)), duration is { } end ? At(end) : null);

    [Fact]
    public void Linear_uses_position_in_line_window_and_track_end()
    {
        var flow = Flow("[00:02]First\n[00:06]Last", 10);
        Assert.Equal(0, flow.At(0, At(1), LyricFlowMode.Linear).Value);
        Assert.Equal(0.5, flow.At(0, At(4), LyricFlowMode.Linear).Value);
        Assert.Equal(0.75, flow.At(1, At(9), LyricFlowMode.Linear).Value);
        Assert.Equal(LyricFill.Full, flow.At(1, At(10), LyricFlowMode.Linear));
    }

    [Fact]
    public void Adaptive_finishes_short_phrase_before_an_instrumental_gap()
    {
        var flow = Flow("[00:01]Before we fall\n[00:03]Stay!\n[00:23]After the lights\n[00:25]Beyond the sea");
        Assert.Equal(LyricFill.Full, flow.At(1, At(7), LyricFlowMode.Adaptive));
        Assert.Equal(0.2, flow.At(1, At(7), LyricFlowMode.Linear).Value);
        Assert.Equal(LyricFill.Full, flow.At(1, At(7), LyricFlowMode.Automatic));
    }

    [Fact]
    public void Adaptive_keeps_room_for_a_long_note_without_a_long_gap()
    {
        var flow = Flow("[00:01]Stay\n[00:04.50]Go\n[00:04.65]Now");
        Assert.False(flow.At(0, At(3), LyricFlowMode.Adaptive).IsComplete);
        Assert.False(flow.At(1, At(4.55), LyricFlowMode.Adaptive).IsComplete);
        Assert.True(flow.At(1, At(4.65), LyricFlowMode.Adaptive).IsComplete);
    }

    [Fact]
    public void Enhanced_LRC_survives_repeat_offset_and_unicode_normalization_of_spaces()
    {
        var lines = LrcParser.Parse("[offset:+250]\n[00:01][00:05]<00:01>  déjà   <00:02.50>🌙 <00:03>");
        Assert.Equal("déjà 🌙", lines[0].Text);
        Assert.Equal(new[] { new LyricAnchor(At(.75), 0), new LyricAnchor(At(2.25), 5), new LyricAnchor(At(2.75), 7) }, lines[0].Anchors);
        Assert.Equal(new LyricAnchor(At(6.25), 5), lines[1].Anchors![1]);
        var flow = new LyricFlow(new LyricsTimeline(lines), At(9));
        Assert.Equal(new LyricFill(5, true, false), flow.At(0, At(2.25), LyricFlowMode.Automatic));
        Assert.Equal(LyricFill.Full, flow.At(0, At(2.75), LyricFlowMode.Automatic));
        Assert.False(flow.At(0, At(2.75), LyricFlowMode.Linear).IsComplete);
    }

    [Theory]
    [InlineData("<00:01>Hello <00:01>again")]
    [InlineData("<00:01>Hello <00:09>again")]
    [InlineData("<00:01>Hello <00:99>again")]
    [InlineData("<00:02>Hello <00:01.50>again")]
    public void Invalid_anchors_fall_back_to_adaptive(string tagged)
    {
        var flow = Flow($"[00:01]{tagged}\n[00:04]Next line");
        Assert.Equal(flow.At(0, At(2), LyricFlowMode.Adaptive), flow.At(0, At(2), LyricFlowMode.Automatic));
    }

    [Fact]
    public void Duplicate_lines_keep_last_active_and_gaps_have_no_wipe()
    {
        var timeline = new LyricsTimeline(LrcParser.Parse("[00:02]First\n[00:02]Second\n[00:04]\n[00:06]♪"));
        var flow = new LyricFlow(timeline, null);
        var current = timeline.IndexAt(At(3));
        Assert.Equal(1, current);
        Assert.Equal(.5, flow.At(current, At(3), LyricFlowMode.Linear).Value);
        Assert.Equal(LyricFill.Full, flow.At(2, At(5), LyricFlowMode.Adaptive));
        Assert.Equal(LyricFill.Full, flow.At(3, At(7), LyricFlowMode.Automatic));
        Assert.Equal(LyricFill.Full, Flow("not timed").At(0, At(1), LyricFlowMode.Automatic));
        Assert.Equal(LyricFill.Full, Flow("[00:01]One line").At(0, At(1), LyricFlowMode.Automatic));
    }

    [Fact]
    public void Adaptive_is_monotone_across_mixed_words_and_graphemes()
    {
        var flow = Flow("[00:01]a extraordinarily, e\u0301 🌙 مرحبًا 世界\n[00:07]Next");
        var previous = 0.0;
        for (var i = 0; i < 120; i++)
        {
            var fill = flow.At(0, At(1 + i / 20.0), LyricFlowMode.Adaptive);
            Assert.True(fill.ByText);
            Assert.True(fill.Value >= previous);
            previous = fill.Value;
        }
    }

    [Fact]
    public void Automatic_preserves_a_real_internal_syllable_anchor_and_estimates_missing_end()
    {
        var flow = Flow("[00:01]<00:01>rea<00:02.70>lly\n[00:09]following");
        var anchor = flow.At(0, At(2.7), LyricFlowMode.Automatic);
        Assert.True(anchor.ByText);
        Assert.Equal(3, anchor.Value);
        Assert.True(flow.At(0, At(5), LyricFlowMode.Automatic).IsComplete);
        Assert.False(flow.At(0, At(5), LyricFlowMode.Linear).IsComplete);
    }

    [Fact]
    public void Invalid_grapheme_boundary_cannot_be_used_as_a_real_anchor()
    {
        var flow = Flow("[00:01]<00:01>e<00:02>\u0301lan\n[00:09]Another line");
        Assert.Equal(flow.At(0, At(3), LyricFlowMode.Adaptive), flow.At(0, At(3), LyricFlowMode.Automatic));
    }

    [Fact]
    public void Sampling_uses_the_same_clock_after_pause_rate_change_seek_and_offset()
    {
        var now = DateTimeOffset.UnixEpoch;
        var clock = new PlaybackClock(() => now);
        var flow = Flow("[00:02]First\n[00:06]Next", 10);
        clock.Reset(At(2));
        clock.SetPlaying(true);
        now += At(1);
        clock.SetRate(2);
        now += At(.5);
        clock.SetPlaying(false);
        now += At(9);
        Assert.Equal(.5, flow.At(0, clock.Position, LyricFlowMode.Linear).Value);
        Assert.Equal(.75, flow.At(0, clock.Position + At(1), LyricFlowMode.Linear).Value);
        clock.Sync(At(7), now, playing: false);
        Assert.Equal(.25, flow.At(1, clock.Position, LyricFlowMode.Linear).Value);
        Assert.Equal(LyricFill.Full, flow.At(1, clock.Position, LyricFlowMode.Off));
    }
}
