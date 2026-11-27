using System.Text.Json;
using Taskitify.Models;

namespace Taskitify.Tests;

public sealed class TrackOffsetTests
{
    [Fact]
    public void KeyIgnoresCaseAndSpacingButKeepsVersionsApart()
    {
        Assert.Equal(AppSettings.TrackKey("Famous", "Kanye West"), AppSettings.TrackKey("  famous ", "KANYE   WEST"));
        Assert.NotEqual(AppSettings.TrackKey("Famous", "Kanye West"), AppSettings.TrackKey("Famous - Live", "Kanye West"));
        Assert.NotEqual(AppSettings.TrackKey("Famous", "Kanye West"), AppSettings.TrackKey("Famous", "Other"));
    }

    [Fact]
    public void EffectiveOffsetAddsTheSongsToTheGlobalOneWithinLimits()
    {
        var s = new AppSettings { SyncOffsetMs = 250 };
        var song = AppSettings.TrackKey("A", "B");
        var other = AppSettings.TrackKey("C", "D");
        s.SetTrackOffset(song, -100);
        Assert.Equal(150, s.EffectiveOffsetMs(song));
        Assert.Equal(250, s.EffectiveOffsetMs(other));
        Assert.Equal(250, s.EffectiveOffsetMs(null));
        s.SyncOffsetMs = 4500;
        s.SetTrackOffset(song, 9000);
        Assert.Equal(AppSettings.TrackOffsetLimit, s.TrackOffsetMs(song));
        Assert.Equal(AppSettings.GlobalOffsetLimit, s.EffectiveOffsetMs(song));
    }

    [Fact]
    public void ZeroRemovesTheSongAndOnlyAdjustedSongsAreSaved()
    {
        var s = new AppSettings();
        var song = AppSettings.TrackKey("A", "B");
        s.SetTrackOffset(song, 300);
        s.SetTrackOffset(AppSettings.TrackKey("C", "D"), 0);
        Assert.Single(s.TrackOffsets);
        s.SetTrackOffset(song, 0);
        Assert.Empty(s.TrackOffsets);
    }

    [Fact]
    public void AdjustmentsSurviveARestartAndOldFilesHaveNone()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"SyncOffsetMs\":-200}")!;
        Assert.Empty(old.TrackOffsets);
        Assert.Equal(-200, old.EffectiveOffsetMs(AppSettings.TrackKey("A", "B")));
        old.SetTrackOffset(AppSettings.TrackKey("Ação", "Artista"), 400);
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(old))!;
        Assert.Equal(200, restored.EffectiveOffsetMs(AppSettings.TrackKey("ação", "artista")));
    }
}
