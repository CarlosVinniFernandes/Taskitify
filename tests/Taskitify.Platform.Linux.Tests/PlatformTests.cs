using Taskitify.Core.Media;
using Taskitify.Platform.Linux;
using Taskitify.Platform.Linux.Mpris;

namespace Taskitify.Platform.Linux.Tests;

public class XdgTests
{
    private static Xdg With(Dictionary<string, string> env) => new(k => env.GetValueOrDefault(k));

    [Fact]
    public void Falls_back_to_home_when_nothing_is_set()
    {
        var xdg = With(new() { ["HOME"] = "/home/ana" });

        Assert.Equal("/home/ana/.config/taskitify/settings.json", xdg.SettingsFile);
        Assert.Equal("/home/ana/.cache/taskitify/lyrics", xdg.LyricsCacheDirectory);
        Assert.Equal("/home/ana/.local/share/taskitify/lyrics", xdg.UserLyricsDirectory);
        Assert.Equal("/home/ana/.config/autostart/taskitify.desktop", xdg.AutostartFile);
        Assert.Null(xdg.LineFile);
    }

    [Fact]
    public void Honours_absolute_xdg_variables_and_ignores_relative_ones()
    {
        var xdg = With(new()
        {
            ["HOME"] = "/home/ana",
            ["XDG_CONFIG_HOME"] = "/cfg",
            ["XDG_CACHE_HOME"] = "relative/cache",
            ["XDG_RUNTIME_DIR"] = "/run/user/1000",
        });

        Assert.Equal("/cfg/taskitify/settings.json", xdg.SettingsFile);
        Assert.Equal("/home/ana/.cache/taskitify/lyrics", xdg.LyricsCacheDirectory);
        Assert.Equal("/run/user/1000/taskitify/line.json", xdg.LineFile);
    }
}

public class AutostartTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "taskitify-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Enabling_writes_a_valid_desktop_entry_and_disabling_removes_it()
    {
        var entry = new AutostartEntry(Path.Combine(_dir, "autostart", "taskitify.desktop"));
        Assert.False(entry.IsEnabled);

        entry.Set(true, "/opt/taskitify/Taskitify");

        Assert.True(entry.IsEnabled);
        var text = File.ReadAllText(Path.Combine(_dir, "autostart", "taskitify.desktop"));
        Assert.StartsWith("[Desktop Entry]\n", text);
        Assert.Contains("Exec=/opt/taskitify/Taskitify --background\n", text);
        Assert.Contains("Type=Application\n", text);

        entry.Set(false, "/opt/taskitify/Taskitify");
        Assert.False(entry.IsEnabled);
    }

    [Theory]
    [InlineData("/usr/bin/taskitify", "/usr/bin/taskitify")]
    [InlineData("/home/a b/Taskitify", "\"/home/a b/Taskitify\"")]
    [InlineData("/x/$y", "\"/x/\\$y\"")]
    public void Quotes_exec_paths_that_need_it(string path, string expected) =>
        Assert.Equal(expected, AutostartEntry.EscapeExec(path));
}

public class LineFileAndSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "taskitify-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Line_file_is_written_atomically_as_json_and_can_be_cleared()
    {
        var path = Path.Combine(_dir, "run", "line.json");
        var file = new LineFile(path);

        file.Write("hello \"world\"", "Song", "Artist", playing: true);

        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        Assert.Equal("hello \"world\"", json.GetProperty("line").GetString());
        Assert.Equal("Song", json.GetProperty("title").GetString());
        Assert.True(json.GetProperty("playing").GetBoolean());
        Assert.False(File.Exists(path + ".tmp"));

        file.Clear();
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Line_file_without_a_runtime_dir_does_nothing()
    {
        var file = new LineFile(null);
        file.Write("x", null, null, false);
        file.Clear();
    }

    [Fact]
    public void Settings_round_trip_and_clamp_out_of_range_values()
    {
        var path = Path.Combine(_dir, "settings.json");
        new LinuxSettings { FontSize = 20, SyncOffsetMs = -250, StartAtLogin = true, WindowX = 12.5 }.Save(path);

        var loaded = LinuxSettings.Load(path);
        Assert.Equal((20, -250, true, 12.5), (loaded.FontSize, loaded.SyncOffsetMs, loaded.StartAtLogin, loaded.WindowX));

        File.WriteAllText(path, "{\"fontSize\": 900, \"width\": 1, \"syncOffsetMs\": 99999}");
        var clamped = LinuxSettings.Load(path);
        Assert.Equal((48, 200, 10_000), (clamped.FontSize, clamped.Width, clamped.SyncOffsetMs));
    }

    [Fact]
    public void Corrupt_or_missing_settings_fall_back_to_defaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        Assert.Equal(16, LinuxSettings.Load(path).FontSize);

        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "{ not json");
        Assert.Equal(16, LinuxSettings.Load(path).FontSize);
    }
}

public class MprisMetadataTests
{
    [Fact]
    public void Parses_a_spotify_style_metadata_map()
    {
        var meta = new Dictionary<string, object>
        {
            ["mpris:trackid"] = "/com/spotify/track/abc",
            ["mpris:length"] = 215_000_000L,
            ["mpris:artUrl"] = "https://i.scdn.co/image/xyz",
            ["xesam:title"] = "Song",
            ["xesam:artist"] = new[] { "A", "B" },
            ["xesam:album"] = "Album",
        };

        var track = MprisMetadata.ParseTrack(meta)!;

        Assert.Equal(("Song", "A, B", "Album"), (track.Title, track.Artist, track.Album));
        Assert.Equal(TimeSpan.FromSeconds(215), track.Duration);
        Assert.Equal("https://i.scdn.co/image/xyz", track.ArtUrl);
        Assert.Equal("/com/spotify/track/abc", MprisMetadata.TrackId(meta));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(100)]
    public void Duration_accepts_other_integer_widths_and_ignores_missing(object? length)
    {
        var meta = new Dictionary<string, object> { ["xesam:title"] = "T", ["xesam:artist"] = "A" };
        if (length is int i)
            meta["mpris:length"] = i * 1_000_000;

        var track = MprisMetadata.ParseTrack(meta)!;

        Assert.Equal(length is null ? null : TimeSpan.FromSeconds(100), track.Duration);
    }

    [Fact]
    public void Empty_metadata_is_no_track()
    {
        Assert.Null(MprisMetadata.ParseTrack(null));
        Assert.Null(MprisMetadata.ParseTrack(new Dictionary<string, object>()));
        Assert.Null(MprisMetadata.ParseTrack(new Dictionary<string, object> { ["mpris:trackid"] = "/x" }));
    }

    [Fact]
    public void Maps_status_and_loop_values_both_ways()
    {
        Assert.Equal(MediaPlaybackStatus.Playing, MprisMetadata.ParseStatus("Playing"));
        Assert.Equal(MediaPlaybackStatus.Paused, MprisMetadata.ParseStatus("Paused"));
        Assert.Equal(MediaPlaybackStatus.Stopped, MprisMetadata.ParseStatus("Stopped"));
        Assert.Equal(MediaPlaybackStatus.NoSession, MprisMetadata.ParseStatus(null));
        Assert.Equal(MediaRepeatMode.List, MprisMetadata.ParseLoop("Playlist"));
        Assert.Equal("Playlist", MprisMetadata.ToLoopStatus(MediaRepeatMode.List));
        Assert.Equal(MediaRepeatMode.List, MprisMetadata.NextRepeat(MediaRepeatMode.None));
        Assert.Equal(MediaRepeatMode.Track, MprisMetadata.NextRepeat(MediaRepeatMode.List));
        Assert.Equal(MediaRepeatMode.None, MprisMetadata.NextRepeat(MediaRepeatMode.Track));
    }
}

public class MprisStateTests
{
    [Fact]
    public void Reads_a_full_reply_and_defaults_what_the_player_omits()
    {
        var full = MprisState.From(new Dictionary<string, object>
        {
            ["PlaybackStatus"] = "Paused",
            ["Shuffle"] = true,
            ["Position"] = 5L,
            ["CanSeek"] = true,
            ["Metadata"] = new Dictionary<string, object> { ["xesam:title"] = "T" },
        });
        Assert.Equal(("Paused", true, 5L, true), (full.PlaybackStatus, full.Shuffle, full.Position, full.CanSeek));
        Assert.False(full.CanGoNext);
        Assert.Equal("T", MprisMetadata.ParseTrack(full.Metadata)!.Title);

        var empty = MprisState.From(new Dictionary<string, object>());
        Assert.Equal((null, false, 0L), (empty.PlaybackStatus, empty.Shuffle, empty.Position));
        Assert.Null(empty.Metadata);
    }
}

public class GnomeExtensionTests
{
    [Theory]
    [InlineData("['ubuntu-dock@ubuntu.com', 'taskitify-lyrics@carlosvinnifernandes.github.io']", true)]
    [InlineData("[\"taskitify-lyrics@carlosvinnifernandes.github.io\"]", true)]
    [InlineData("@as []", false)]
    [InlineData("['taskitify-lyrics@carlosvinnifernandes.github.io.old']", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Detects_the_extension_in_the_enabled_list(string? output, bool expected) =>
        Assert.Equal(expected, GnomeExtension.IsListed(output));
}
