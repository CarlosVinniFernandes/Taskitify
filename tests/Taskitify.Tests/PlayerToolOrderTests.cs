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
        var saved = new[] { "Settings", "Size", "Maximize", "Switch", "ClickThrough", "Lock", "Close" };
        Assert.Equal(PlayerToolIds.DefaultOrder, PlayerToolIds.Normalize(saved));
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
