using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Taskitify.Services;

namespace Taskitify.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void CatalogDiscoversAllSupportedLanguagesWithEnglishFirst()
    {
        var catalog = new LocalizationCatalog();
        Assert.Equal(new[] { "en", "pt", "es", "fr", "de", "it" }, catalog.Languages.Select(l => l.Code));
        Assert.Equal("Português", catalog.Languages.Single(l => l.Code == "pt").Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void UnsupportedLanguagesFallBackToEnglish(string? code)
    {
        var catalog = new LocalizationCatalog();
        Assert.Equal("en", catalog.Normalize(code));
        Assert.Equal("Settings…", catalog.Get(code, "Settings"));
    }

    [Fact]
    public void ExistingSettingsDefaultToEnglishAndLanguageRoundTrips()
    {
        var settings = JsonSerializer.Deserialize<Taskitify.Models.AppSettings>("{\"FontSize\":15}")!;
        Assert.Equal("en", settings.Language);
        Assert.Equal(15, settings.FontSize);
        settings.Language = "fr";
        var restored = JsonSerializer.Deserialize<Taskitify.Models.AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal("fr", restored.Language);
        Assert.Equal(15, restored.FontSize);
    }

    [Fact]
    public void EveryTranslationIsCompleteAndPreservesFormatArguments()
    {
        var assembly = typeof(LocalizationCatalog).Assembly;
        Dictionary<string, string> Read(string code)
        {
            using var stream = assembly.GetManifestResourceStream($"Taskitify.Languages.{code}.json")!;
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        }
        var english = Read("en");
        var catalog = new LocalizationCatalog();
        foreach (var language in catalog.Languages)
        {
            var translated = Read(language.Code);
            Assert.Equal(english.Keys.Order(), translated.Keys.Order());
            foreach (var (key, original) in english)
            {
                Assert.False(string.IsNullOrWhiteSpace(translated[key]), $"{language.Code}: {key}");
                static IEnumerable<string> Arguments(string value) => Regex.Matches(value, @"\{\d+\}").Select(m => m.Value).Order();
                Assert.Equal(Arguments(original), Arguments(translated[key]));
                _ = string.Format(CultureInfo.InvariantCulture, translated[key], 1, 2, 3, 4);
            }
        }
    }

    [Fact]
    public void FormattingUsesSelectedLanguageAndLookupIsCaseInsensitive()
    {
        var catalog = new LocalizationCatalog();
        Assert.Equal("Paramètres…", catalog.Get("FR", "Settings"));
        Assert.Contains("12,5%", catalog.Format("fr", "VolumeDetail", 12.5));
        Assert.Contains("12.5%", catalog.Format("en", "VolumeDetail", 12.5));
    }
}
