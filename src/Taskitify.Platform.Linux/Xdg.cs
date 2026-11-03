namespace Taskitify.Platform.Linux;

/// <summary>XDG base directories, with the environment injectable for tests.</summary>
public sealed class Xdg
{
    public const string AppName = "taskitify";

    private readonly Func<string, string?> _env;

    public Xdg(Func<string, string?>? env = null) => _env = env ?? Environment.GetEnvironmentVariable;

    private string Home => Nonempty("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string ConfigHome => Absolute("XDG_CONFIG_HOME") ?? Path.Combine(Home, ".config");

    public string DataHome => Absolute("XDG_DATA_HOME") ?? Path.Combine(Home, ".local", "share");

    public string CacheHome => Absolute("XDG_CACHE_HOME") ?? Path.Combine(Home, ".cache");

    /// <summary>Per-user runtime dir (tmpfs, cleared at logout); null when the system has none.</summary>
    public string? RuntimeDir => Absolute("XDG_RUNTIME_DIR");

    public string SettingsFile => Path.Combine(ConfigHome, AppName, "settings.json");

    public string LyricsCacheDirectory => Path.Combine(CacheHome, AppName, "lyrics");

    /// <summary>Where the user drops their own .lrc files.</summary>
    public string UserLyricsDirectory => Path.Combine(DataHome, AppName, "lyrics");

    public string AutostartFile => Path.Combine(ConfigHome, "autostart", "taskitify.desktop");

    /// <summary>The file the GNOME Shell extension watches for the current lyric line.</summary>
    public string? LineFile => RuntimeDir is { } r ? Path.Combine(r, AppName, "line.json") : null;

    private string? Nonempty(string name) => _env(name) is { Length: > 0 } v ? v : null;

    // The spec says relative values must be ignored.
    private string? Absolute(string name) => Nonempty(name) is { } v && Path.IsPathRooted(v) ? v : null;
}
