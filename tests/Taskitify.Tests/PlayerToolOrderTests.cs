using System.Text.Json;
using Taskitify.Models;

namespace Taskitify.Tests;

public class PlayerToolOrderTests
{
    [Fact]
    public void Default_order_starts_with_settings_and_ends_with_close()
    {
        Assert.Equal(PlayerToolIds.Settings, PlayerToolIds.DefaultOrder[0]);
        Assert.Equal(PlayerToolIds.Close, PlayerToolIds.DefaultOrder[^1]);
        Assert.Equal(PlayerToolIds.DefaultOrder, PlayerToolIds.Normalize(null));
    }

    [Fact]
    public void A_saved_order_is_kept_and_unknown_or_repeated_ids_are_dropped()
    {
        var saved = new[] { "Close", "lock", "Nope", "Settings", "Close", "Size", "Maximize", "FullScreen", "Switch", "ClickThrough" };
        Assert.Equal(new[] { "Close", "Lock", "Settings", "Size", "Maximize", "FullScreen", "Switch", "ClickThrough" }, PlayerToolIds.Normalize(saved));
    }

    [Fact]
    public void A_button_added_later_takes_its_default_place()
    {
        // An order saved before full screen existed.
        var saved = new[] { "Settings", "Size", "Switch", "ClickThrough", "Lock", "Maximize", "Close" };
        Assert.Equal(PlayerToolIds.DefaultOrder, PlayerToolIds.Normalize(saved));
    }

    [Fact]
    public void Window_buttons_come_last_full_screen_then_maximize_then_close()
    {
        Assert.Equal(new[] { "FullScreen", "Maximize", "Close" }, PlayerToolIds.DefaultOrder.TakeLast(3));
    }

    [Fact]
    public void The_first_default_order_left_untouched_follows_the_new_default()
    {
        var first = new[] { "Settings", "Size", "Maximize", "FullScreen", "Switch", "ClickThrough", "Lock", "Close" };
        Assert.Equal(PlayerToolIds.DefaultOrder, PlayerToolIds.Normalize(first));
        // An order the user changed is kept.
        var mine = new[] { "Close", "Settings", "Size", "Maximize", "FullScreen", "Switch", "ClickThrough", "Lock" };
        Assert.Equal(mine, PlayerToolIds.Normalize(mine));
    }

    [Fact]
    public void Older_settings_get_the_default_order_and_it_round_trips()
    {
        var settings = JsonSerializer.Deserialize<MinimalModeSettings>("{\"PlayerShowFillButton\":false}")!;
        Assert.Equal(PlayerToolIds.DefaultOrder, settings.PlayerToolOrder);
        Assert.True(settings.PlayerShowFullScreenButton);
        settings.PlayerToolOrder = ["Close", "Settings"];
        var restored = JsonSerializer.Deserialize<MinimalModeSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(new[] { "Close", "Settings" }, restored.PlayerToolOrder);
    }
}

public class LargeCoverTests
{
    [Theory]
    [InlineData("https://i.scdn.co/image/ab67616d00001e02abc", "https://i.scdn.co/image/ab67616d0000b273abc")]
    [InlineData("https://i.scdn.co/image/ab67616d00004851abc", "https://i.scdn.co/image/ab67616d0000b273abc")]
    [InlineData("https://i.scdn.co/image/ab67616d0000b273abc", "https://i.scdn.co/image/ab67616d0000b273abc")]
    public void Spotify_cover_urls_ask_for_the_640_px_image(string url, string expected) =>
        Assert.Equal(expected, Taskitify.Core.Lyrics.SpotifyLyricsProvider.LargeCoverUrl(url));

    [Fact]
    public void Glow_and_depth_are_clamped_and_older_looks_get_the_defaults()
    {
        Assert.Equal(0, MiniPlayerStyle.ClampGlow(-1));
        Assert.Equal(1, MiniPlayerStyle.ClampGlow(3));
        Assert.Equal(1.6, MiniPlayerStyle.ClampDepth(9));
        var look = System.Text.Json.JsonSerializer.Deserialize<MiniPlayerStyle>("{\"Highlight\":3}")!;
        Assert.Equal(MiniPlayerStyle.DefaultGlow, look.LyricsGlow);
        Assert.Equal(MiniPlayerStyle.DefaultDepth, look.LyricsDepth);
    }
}
