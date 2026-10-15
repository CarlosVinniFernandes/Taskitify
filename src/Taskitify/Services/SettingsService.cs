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

    public SettingsService()
    {
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Save();
        };
    }

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
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
                Current.MigrateLegacy();
                return;
            }
            IsFirstRun = true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Error("Could not read settings, using defaults", ex);
        }
        Current = new AppSettings();
    }

    /// <summary>Mutates the settings, notifies listeners and saves shortly after (debounced).</summary>
    public void Update(Action<AppSettings> change)
    {
        change(Current);
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
