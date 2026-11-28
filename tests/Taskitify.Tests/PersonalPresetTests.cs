using System.Text.Json;
using System.Text.Json.Serialization;
using Taskitify.Models;

namespace Taskitify.Tests;

public sealed class PersonalPresetTests
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public void SavedLookKeepsEverythingButTheLayoutAndTheWidth()
    {
        var settings = new AppSettings();
        settings.MiniPlayerLook.Background = MiniPlayerBackground.Velvet;
        settings.MiniPlayerLook.Typeface = LyricsTypeface.Serif;
        settings.MiniPlayerLook.Motion = LyricMotionMode.Expressive;
        settings.MiniPlayerLook.LyricsScale = 1.2;
        settings.MiniPlayer.ShowVolume = false;
        var preset = MiniPlayerPreset.From("Late night", settings);

        // Something else, wide, narrower.
        var other = new AppSettings();
        other.MiniPlayerLook.Layout = MiniPlayerLayout.Cinema;
        other.MiniPlayer.Width = 420;
        Assert.False(preset.Matches(other));
        preset.ApplyTo(other);

        Assert.True(preset.Matches(other));
        Assert.Equal(MiniPlayerLayout.Cinema, other.MiniPlayerLook.Layout);
        Assert.Equal(420, other.MiniPlayer.Width);
        Assert.Equal(MiniPlayerBackground.Velvet, other.MiniPlayerLook.Background);
        Assert.Equal(LyricMotionMode.Expressive, other.MiniPlayerLook.Motion);
        Assert.Equal(1.2, other.MiniPlayerLook.LyricsScale);
        Assert.False(other.MiniPlayer.ShowVolume);
    }

    [Fact]
    public void ChangingTheSettingsAfterSavingDoesNotChangeThePreset()
    {
        var settings = new AppSettings();
        var preset = MiniPlayerPreset.From("Mine", settings);
        settings.MiniPlayerLook.Background = MiniPlayerBackground.Aurora;
        settings.MiniPlayer.ShowCover = false;
        Assert.Equal(MiniPlayerBackground.Default, preset.Look.Background);
        Assert.True(preset.Elements.ShowCover);
        Assert.False(preset.Matches(settings));
    }

    [Fact]
    public void PresetsSurviveARestartAndOldFilesHaveNone()
    {
        Assert.Empty(JsonSerializer.Deserialize<AppSettings>("{\"FontSize\":15}", Json)!.MiniPlayerPresets);
        var settings = new AppSettings();
        settings.MiniPlayerLook.Highlight = LyricsHighlight.Progressive;
        settings.MiniPlayerPresets.Add(MiniPlayerPreset.From("Calm evening", settings));
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, Json), Json)!;
        var preset = Assert.Single(restored.MiniPlayerPresets);
        Assert.Equal("Calm evening", preset.Name);
        Assert.True(preset.Matches(restored));
    }

    [Fact]
    public void FloatingPlayerStartsWithACopyOfTheLegacyLookAndSavesItsOwnChanges()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""
            {
              "MiniPlayer": { "ShowVolume": false },
              "MiniPlayerLook": { "Background": "Velvet" },
              "MiniPlayerPresets": [{ "Name": "Late night" }]
            }
            """, Json)!;
        var floating = settings.FloatingPlayerAppearance;
        Assert.Equal(MiniPlayerBackground.Velvet, floating.Look.Background);
        Assert.False(floating.Elements.ShowVolume);
        Assert.Equal("Late night", Assert.Single(floating.Presets).Name);

        floating.Look.ShowKaraokeButton = false;
        floating.Elements.ShowVolume = true;
        floating.Presets[0].Name = "Full screen";

        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, Json), Json)!;
        Assert.True(restored.MiniPlayerLook.ShowKaraokeButton);
        Assert.False(restored.MiniPlayer.ShowVolume);
        Assert.Equal("Late night", Assert.Single(restored.MiniPlayerPresets).Name);
        Assert.False(restored.FloatingPlayerAppearance.Look.ShowKaraokeButton);
        Assert.True(restored.FloatingPlayerAppearance.Elements.ShowVolume);
        Assert.Equal("Full screen", Assert.Single(restored.FloatingPlayerAppearance.Presets).Name);
    }

    [Fact]
    public void SharingTheFloatingLookPreservesThePlayersOwnLookAndToolbar()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""
            {
              "FloatingPlayerAppearance": { "Look": { "Background": "Velvet" } },
              "Minimal": { "PlayerShowSizeButtons": false, "ControlsOnHover": true }
            }
            """, Json)!;
        Assert.Equal(MiniPlayerBackground.Velvet, settings.PresentationAppearance.Look.Background);
        Assert.False(settings.PresentationToolbar.Shows(PlayerToolIds.Size));
        Assert.True(settings.PlayerControlsOnHover);

        settings.PresentationAppearance.Look.Background = MiniPlayerBackground.Theme;
        settings.PresentationToolbar.Hidden.Clear();
        settings.PlayerControlsOnHover = false;
        settings.PlayerUsesFloatingLook = true;
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, Json), Json)!;

        Assert.Equal(MiniPlayerBackground.Velvet, restored.PlayerLook(restored.EffectivePlayerSurface(PlayerSurface.Player)).Background);
        Assert.True(restored.PresentationToolbar.Shows(PlayerToolIds.Size));
        Assert.False(restored.PlayerControlsOnHover);
        Assert.False(restored.Minimal.PlayerShowSizeButtons);
        Assert.True(restored.Minimal.ControlsOnHover);
        restored.PlayerUsesFloatingLook = false;
        Assert.Equal(MiniPlayerBackground.Theme, restored.PlayerLook(restored.EffectivePlayerSurface(PlayerSurface.Player)).Background);
        Assert.Equal(MiniPlayerBackground.Velvet, restored.PlayerLook(PlayerSurface.Floating).Background);
    }
}
