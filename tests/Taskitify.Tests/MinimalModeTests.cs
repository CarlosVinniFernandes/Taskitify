using System.Text.Json;
using System.Text.Json.Serialization;
using Taskitify.Core.Layout;
using Taskitify.Models;

namespace Taskitify.Tests;

public sealed class MinimalModeTests
{
    private static readonly PixelRect Primary = new(0, 0, 1920, 1152);
    private static readonly PixelRect Right = new(1920, 0, 2560, 1400);

    [Fact]
    public void FirstPlacementIsCentredOnThePrimaryScreenAboveTheTaskbar()
    {
        var r = FloatingPlacement.Resolve(default, [Primary, Right], (720, 220));
        Assert.Equal(new PixelRect(600, 788, 720, 220), r);
    }

    [Fact]
    public void SavedPlaceOnAnyScreenIsKept()
    {
        var saved = new PixelRect(2500, 300, 900, 180);
        Assert.Equal(saved, FloatingPlacement.Resolve(saved, [Primary, Right], (720, 220)));
        // Partly off screen but still reachable.
        var edge = new PixelRect(1850, 1100, 720, 220);
        Assert.Equal(edge, FloatingPlacement.Resolve(edge, [Primary], (720, 220)));
    }

    [Fact]
    public void LostPlaceComesBackToThePrimaryScreenKeepingItsSize()
    {
        // The second monitor was unplugged.
        var r = FloatingPlacement.Resolve(new PixelRect(2500, 300, 900, 180), [Primary], (720, 220));
        Assert.Equal((900, 180), (r.Width, r.Height));
        Assert.True(Primary.Contains(r));
        // Larger than the new resolution: shrunk to fit.
        var big = FloatingPlacement.Resolve(new PixelRect(-5000, 0, 3000, 2000), [Primary], (720, 220));
        Assert.True(Primary.Contains(big));
    }

    [Fact]
    public void OlderSettingsGetMinimalModeOffWithItsDefaults()
    {
        var json = "{\"FontSize\":15,\"MiniPlayerLook\":{\"Background\":\"Aurora\"}}";
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var settings = JsonSerializer.Deserialize<AppSettings>(json, options)!;
        Assert.False(settings.Minimal.Enabled);
        Assert.True(settings.Minimal.Topmost);
        Assert.Equal(MinimalLines.Three, settings.Minimal.Lines);
        Assert.Equal((1, 1), settings.Minimal.Around);
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, options), options)!;
        Assert.Equal(MiniPlayerBackground.Aurora, restored.MiniPlayerLook.Background);
        Assert.Equal(settings.Minimal.FontSize, restored.Minimal.FontSize);
    }

    [Theory]
    [InlineData(MinimalLines.Current, 0, 0)]
    [InlineData(MinimalLines.Next, 0, 1)]
    [InlineData(MinimalLines.Five, 2, 2)]
    public void VersesAroundTheCurrentOne(MinimalLines lines, int before, int after) =>
        Assert.Equal((before, after), new MinimalModeSettings { Lines = lines }.Around);
}
