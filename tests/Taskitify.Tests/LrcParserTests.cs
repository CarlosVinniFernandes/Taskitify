using Taskitify.Core.Lyrics;

namespace Taskitify.Tests;

public class LrcParserTests
{
    [Fact]
    public void Parses_basic_lines_in_order()
    {
        var lines = LrcParser.Parse("[00:01.00]First\n[00:03.50]Second\r\n[01:02.345]Third");

        Assert.Equal(3, lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), lines[0].Time);
        Assert.Equal("First", lines[0].Text);
        Assert.Equal(TimeSpan.FromMilliseconds(3500), lines[1].Time);
        Assert.Equal(new TimeSpan(0, 0, 1, 2, 345), lines[2].Time);
        Assert.Equal("Third", lines[2].Text);
    }

    [Theory]
    [InlineData("[00:10]x", 10_000)]
    [InlineData("[00:10.5]x", 10_500)]
    [InlineData("[00:10.05]x", 10_050)]
    [InlineData("[00:10.005]x", 10_005)]
    [InlineData("[00:10:25]x", 10_250)]
    [InlineData("[100:00.00]x", 6_000_000)]
    public void Parses_timestamp_formats(string lrc, int expectedMs)
    {
        var line = Assert.Single(LrcParser.Parse(lrc));
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), line.Time);
    }

    [Fact]
    public void Expands_repeated_time_tags_and_sorts()
    {
        var lines = LrcParser.Parse("[00:20.00][00:05.00]Chorus\n[00:10.00]Verse");

        Assert.Equal(["Chorus", "Verse", "Chorus"], lines.Select(l => l.Text));
        Assert.Equal([5, 10, 20], lines.Select(l => (int)l.Time.TotalSeconds));
    }

    [Fact]
    public void Skips_metadata_and_keeps_empty_lines_as_gaps()
    {
        var lines = LrcParser.Parse("[ar:Artist]\n[ti:Title]\n[length: 03:00]\n[00:01.00]Hi\n[00:02.00]\n");

        Assert.Equal(2, lines.Count);
        Assert.Equal("", lines[1].Text);
    }

    [Fact]
    public void Applies_offset_tag()
    {
        var lines = LrcParser.Parse("[offset:+500]\n[00:02.00]A\n[00:00.20]B");

        Assert.Equal(TimeSpan.Zero, lines[0].Time); // clamped, never negative
        Assert.Equal(TimeSpan.FromMilliseconds(1500), lines[1].Time);
    }

    [Fact]
    public void Strips_enhanced_word_tags()
    {
        var line = Assert.Single(LrcParser.Parse("[00:01.00]<00:01.00>Hello <00:01.50>world"));
        Assert.Equal("Hello world", line.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plain text without tags")]
    public void Empty_or_unsynced_input_yields_no_lines(string? lrc)
    {
        Assert.Empty(LrcParser.Parse(lrc));
    }
}
