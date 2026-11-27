using System.Text.Json;
using System.Text.Json.Serialization;
using Taskitify.Models;

namespace Taskitify.Tests;

public sealed class BackgroundIntensityTests
{
    [Fact]
    public void ExistingLooksKeepTheirBackgroundsAsTheyWere()
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var look = JsonSerializer.Deserialize<AppSettings>("{\"MiniPlayerLook\":{\"Background\":\"Velvet\",\"Revision\":1}}", options)!.MiniPlayerLook;
        Assert.Equal((1d, 0d, 1d), (look.BackgroundEffect, look.BackgroundDim, look.BackgroundBlur));
    }

    [Theory]
    [InlineData(MiniPlayerBackground.Aurora, true, true, false)]
    [InlineData(MiniPlayerBackground.Velvet, true, true, false)]
    [InlineData(MiniPlayerBackground.BlurredCover, false, true, true)]
    [InlineData(MiniPlayerBackground.AlbumGradient, false, true, false)]
    [InlineData(MiniPlayerBackground.Theme, false, false, false)]
    public void OnlyWhatABackgroundHasCanBeTuned(MiniPlayerBackground background, bool effect, bool dim, bool blur) =>
        Assert.Equal((effect, dim, blur), MiniPlayerStyle.Tunable(background, defaultIsGradient: true));

    [Fact]
    public void LikeTheBarFollowsTheBarsBackground()
    {
        Assert.True(MiniPlayerStyle.Tunable(MiniPlayerBackground.Default, defaultIsGradient: true).Dim);
        Assert.False(MiniPlayerStyle.Tunable(MiniPlayerBackground.Default, defaultIsGradient: false).Dim);
    }

    [Fact]
    public void ValuesStayWithinReadableLimits()
    {
        Assert.Equal(0.6, MiniPlayerStyle.ClampDim(0.95));
        Assert.Equal(0.3, MiniPlayerStyle.ClampEffect(0));
        Assert.Equal(1.8, MiniPlayerStyle.ClampBlur(5));
    }

    [Fact]
    public void APersonalLookCarriesItsIntensity()
    {
        var settings = new AppSettings();
        settings.MiniPlayerLook.BackgroundDim = 0.4;
        var preset = MiniPlayerPreset.From("Dark", settings);
        settings.MiniPlayerLook.BackgroundDim = 0;
        Assert.False(preset.Matches(settings));
        preset.ApplyTo(settings);
        Assert.Equal(0.4, settings.MiniPlayerLook.BackgroundDim);
    }
}
