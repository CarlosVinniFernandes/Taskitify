using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using Taskitify.Infrastructure;
using Taskitify.Models;

namespace Taskitify.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly DispatcherTimer _saveTimer;
    private bool _inMemory;

    public SettingsService()
    {
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Save();
        };
    }

    /// <summary>Settings that live only in memory (the welcome tour's demonstrations): never saved, never change the language.</summary>
    public static SettingsService InMemory(AppSettings current) => new() { Current = current, _inMemory = true };

    public static string FilePath { get; } = Path.Combine(AppPaths.RoamingDirectory, "settings.json");

    public AppSettings Current { get; private set; } = new();

    public bool IsFirstRun { get; private set; }

    public event EventHandler? Changed;

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var text = File.ReadAllText(FilePath);
                Current = JsonSerializer.Deserialize<AppSettings>(text, Json) ?? new AppSettings();
                Current.MigrateLegacy();
                // Looks saved before the display typeface had Windows' font as their default.
                if (text.Contains("\"MiniPlayerLook\"", StringComparison.Ordinal) && !text.Contains("\"Revision\"", StringComparison.Ordinal)
                    && Current.MiniPlayerLook.Typeface == Models.LyricsTypeface.System)
                    Current.MiniPlayerLook.Typeface = Models.LyricsTypeface.Modern;
                // Older files shared these settings. Take the snapshot before either player is edited.
                _ = Current.FloatingPlayerAppearance;
                return;
            }
            IsFirstRun = true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Error("Could not read settings, using defaults", ex);
            // Keep the unreadable file: the next save would otherwise replace it with the defaults.
            try { File.Copy(FilePath, FilePath + ".invalid", overwrite: true); }
            catch (Exception copy) when (copy is IOException or UnauthorizedAccessException) { Log.Warn($"Could not keep the unreadable settings: {copy.Message}"); }
        }
        // New settings already have every button they default to; only older files are migrated.
        Current = new AppSettings { LikeButtonAdded = true };
        _ = Current.FloatingPlayerAppearance;
    }

    /// <summary>Mutates the settings, notifies listeners and saves shortly after (debounced).</summary>
    public void Update(Action<AppSettings> change)
    {
        change(Current);
        if (_inMemory)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }
        LocalizationService.SetLanguage(Current.Language);
        Changed?.Invoke(this, EventArgs.Empty);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void Flush()
    {
        if (_saveTimer.IsEnabled)
        {
            _saveTimer.Stop();
            Save();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.RoamingDirectory);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not save settings", ex);
        }
    }
}
