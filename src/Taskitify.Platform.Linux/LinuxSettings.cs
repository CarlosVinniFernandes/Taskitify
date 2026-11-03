using System.Text.Json;
using System.Text.Json.Serialization;

namespace Taskitify.Platform.Linux;

public sealed class LinuxSettings
{
    public int FontSize { get; set; } = 16;

    public int Width { get; set; } = 420;

    public int SyncOffsetMs { get; set; }

    /// <summary>Hide the lyric window while paused/stopped instead of leaving it on screen.</summary>
    public bool HideWhenPaused { get; set; } = true;

    public bool ShowTrackWhenNoLyrics { get; set; } = true;

    /// <summary>
    /// Show the floating lyrics bar. Null (the default) shows it unless the GNOME top-bar extension
    /// is enabled, which already shows the line.
    /// </summary>
    public bool? FloatingBar { get; set; }

    public bool StartAtLogin { get; set; }

    /// <summary>Two-letter UI language; null follows the system locale.</summary>
    public string? Language { get; set; }

    public double? WindowX { get; set; }

    public double? WindowY { get; set; }

    [JsonIgnore]
    public static JsonSerializerOptions Json { get; } = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static LinuxSettings Load(string file)
    {
        try
        {
            if (File.Exists(file))
                return Normalize(JsonSerializer.Deserialize<LinuxSettings>(File.ReadAllText(file), Json) ?? new());
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A corrupt file must not stop the app: start from defaults.
        }
        return new LinuxSettings();
    }

    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, file, overwrite: true);
    }

    private static LinuxSettings Normalize(LinuxSettings s)
    {
        s.FontSize = Math.Clamp(s.FontSize, 10, 48);
        s.Width = Math.Clamp(s.Width, 200, 1600);
        s.SyncOffsetMs = Math.Clamp(s.SyncOffsetMs, -10_000, 10_000);
        return s;
    }
}
