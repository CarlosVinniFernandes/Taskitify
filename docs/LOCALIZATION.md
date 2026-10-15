# Interface languages

English is the default, including for existing settings files without a language preference. Choose **Settings → General → Language** to switch immediately. The language code is saved in `settings.json`. Song metadata and lyrics remain in their original language; technical log messages remain in English.

The bundled languages are English (`en`), Portuguese (`pt`), Spanish (`es`), French (`fr`), German (`de`), and Italian (`it`). Names in the selector use each language's native spelling.

## Adding a language

1. Copy `src/Taskitify/Resources/Languages/en.json` to `<language-code>.json` in the same folder. Use a valid .NET culture code, such as `ja` or `pt-PT`.
2. Set `_name` to the language's native name and translate the values, preserving keys and numbered placeholders such as `{0}`.
3. Build the app. Catalogs are embedded and discovered automatically; no enum or selector changes are required. English stays first in the selector.
4. Run `dotnet test Taskitify.sln`. Extend the expected language list in `LocalizationTests` when adding a language. Tests check key coverage, formatting arguments, discovery, defaults, and settings serialization.

Use `{DynamicResource Loc.Key}` in WPF markup and `LocalizationService.Get("Key")` or `LocalizationService.Format("Key", args)` in code. Add new keys to every catalog. Update text that is cached in view models when the language changes, and unsubscribe event handlers when their owner is disposed. Missing translations fall back to English; unsupported saved language codes also fall back to English.
