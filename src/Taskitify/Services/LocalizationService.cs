using System.Windows;

namespace Taskitify.Services;

public static class LocalizationService
{
    private static readonly LocalizationCatalog Catalog = new();
    public static string Language { get; private set; } = "en";
    public static IReadOnlyList<LanguageOption> Languages => Catalog.Languages;
    public static event EventHandler? Changed;
    public static string Get(string key) => Catalog.Get(Language, key);
    public static string Format(string key, params object[] args) => Catalog.Format(Language, key, args);

    public static void SetLanguage(string? code)
    {
        var language = Catalog.Normalize(code);
        if (Language == language && Application.Current.Resources.Contains("Loc.Language"))
            return;
        Language = language;
        foreach (var key in Catalog.Keys)
            Application.Current.Resources["Loc." + key] = Get(key);
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
