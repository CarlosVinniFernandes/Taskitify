using System.Globalization;
using System.Text.Json;

namespace Taskitify.Services;

public sealed record LanguageOption(string Code, string Name);

/// <summary>Embedded catalogs, discovered automatically. English is the per-key fallback.</summary>
public sealed class LocalizationCatalog
{
    private readonly Dictionary<string, Dictionary<string, string>> _catalogs = new(StringComparer.OrdinalIgnoreCase);

    public LocalizationCatalog()
    {
        var assembly = typeof(LocalizationCatalog).Assembly;
        const string prefix = "Taskitify.Languages.";
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix) && n.EndsWith(".json")))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            _catalogs[name[prefix.Length..^5]] = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        }
        Languages = _catalogs.Select(c => new LanguageOption(c.Key, c.Value["_name"]))
            .OrderBy(l => l.Code == "en" ? 0 : l.Code == "pt" ? 1 : l.Code == "es" ? 2 : l.Code == "fr" ? 3 : l.Code == "de" ? 4 : 5)
            .ThenBy(l => l.Name, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<LanguageOption> Languages { get; }
    public IEnumerable<string> Keys => _catalogs["en"].Keys;
    public string Normalize(string? code) => code is not null && _catalogs.ContainsKey(code) ? code.ToLowerInvariant() : "en";
    public string Get(string? code, string key) =>
        _catalogs[Normalize(code)].TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : _catalogs["en"].GetValueOrDefault(key, key);
    public string Format(string? code, string key, params object[] args) =>
        string.Format(CultureInfo.GetCultureInfo(Normalize(code)), Get(code, key), args);
}
