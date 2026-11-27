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
        // Minimal indeed: three verses, the current one larger, no background, no buttons, clicks to the game below.
        Assert.Equal(MinimalLines.Three, settings.Minimal.Lines);
        Assert.Equal((1, 1), settings.Minimal.Around);
        Assert.True(settings.Minimal.SizeAt(-2) < settings.Minimal.SizeAt(-1) && settings.Minimal.SizeAt(-1) < settings.Minimal.SizeAt(0));
        Assert.Equal(settings.Minimal.SizeAt(1), settings.Minimal.SizeAt(-1));
        Assert.Equal(MinimalBackdrop.Clear, settings.Minimal.Backdrop);
        Assert.True(settings.Minimal.ClickThrough);
        Assert.False(settings.Minimal.ShowSong || settings.Minimal.ShowControls);
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

    [Fact]
    public void AutoFitShrinksTheTextSoTheChosenVersesFitButNeverGrowsIt()
    {
        var s = new MinimalModeSettings { FontSize = 30, Lines = MinimalLines.Three };
        // Room enough: the chosen size.
        Assert.Equal(30, s.FitFontSize(400, 900, 0));
        // A third of the height: about a third of the size, and the three verses fit.
        var small = s.FitFontSize(60, 900, 0);
        Assert.True(small < 30 && 3 * s.VerseHeight(small) <= 60);
        // Five verses need a smaller size than three in the same window.
        Assert.True(new MinimalModeSettings { FontSize = 30, Lines = MinimalLines.Five }.FitFontSize(120, 900, 0) < s.FitFontSize(120, 900, 0));
        // A narrow window shrinks long verses too, but not below 60% of the chosen size.
        var narrow = s.FitFontSize(400, 300, 1200);
        Assert.True(narrow < 30 && narrow >= 10);
    }

    [Theory]
    [InlineData(FloatingSpot.Bottom, 600, 900)]
    [InlineData(FloatingSpot.Top, 600, 32)]
    [InlineData(FloatingSpot.Center, 600, 466)]
    [InlineData(FloatingSpot.BottomLeft, 32, 900)]
    [InlineData(FloatingSpot.BottomRight, 1168, 900)]
    public void QuickPositionsKeepTheSizeOnTheSameScreen(FloatingSpot spot, int x, int y)
    {
        var r = FloatingPlacement.Snap(new PixelRect(5, 5, 720, 200), new PixelRect(0, 0, 1920, 1132), spot, 32);
        Assert.Equal(new PixelRect(x, y, 720, 200), r);
    }

    [Fact]
    public void ShortcutAndPauseDefaultsSuitGames()
    {
        var s = new MinimalModeSettings();
        Assert.True(s.HotkeyEnabled);
        Assert.Equal((3, 0x4C), (s.HotkeyModifiers, s.HotkeyKey)); // Ctrl + Alt + L
        Assert.Equal(MinimalPaused.Hide, s.WhenPaused);
        Assert.Empty(s.AutoApps);
    }
}
