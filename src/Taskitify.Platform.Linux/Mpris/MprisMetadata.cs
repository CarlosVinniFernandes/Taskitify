using Taskitify.Core.Media;

namespace Taskitify.Platform.Linux.Mpris;

/// <summary>Turns MPRIS property values into the app's neutral types.</summary>
public static class MprisMetadata
{
    public static MediaTrack? ParseTrack(IDictionary<string, object>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
            return null;

        var title = AsString(Get(metadata, "xesam:title")) ?? "";
        var artist = AsString(Get(metadata, "xesam:artist")) ?? "";
        var album = AsString(Get(metadata, "xesam:album")) ?? "";
        if (title.Length == 0 && artist.Length == 0)
            return null;

        TimeSpan? duration = null;
        if (Get(metadata, "mpris:length") is { } len && TryLong(len, out var micros) && micros > 0)
            duration = TimeSpan.FromTicks(micros * 10);

        return new MediaTrack(title, artist, album, duration, AsString(Get(metadata, "mpris:artUrl")));
    }

    /// <summary>The identity of a track, to tell "a new song" from "same song, metadata refreshed".</summary>
    public static string? TrackId(IDictionary<string, object>? metadata) =>
        metadata is not null && Get(metadata, "mpris:trackid") is { } id ? id.ToString() : null;

    public static MediaPlaybackStatus ParseStatus(string? status) => status switch
    {
        "Playing" => MediaPlaybackStatus.Playing,
        "Paused" => MediaPlaybackStatus.Paused,
        "Stopped" => MediaPlaybackStatus.Stopped,
        _ => MediaPlaybackStatus.NoSession,
    };

    public static MediaRepeatMode ParseLoop(string? loop) => loop switch
    {
        "Playlist" => MediaRepeatMode.List,
        "Track" => MediaRepeatMode.Track,
        _ => MediaRepeatMode.None,
    };

    public static string ToLoopStatus(MediaRepeatMode mode) => mode switch
    {
        MediaRepeatMode.List => "Playlist",
        MediaRepeatMode.Track => "Track",
        _ => "None",
    };

    public static MediaRepeatMode NextRepeat(MediaRepeatMode mode) => mode switch
    {
        MediaRepeatMode.None => MediaRepeatMode.List,
        MediaRepeatMode.List => MediaRepeatMode.Track,
        _ => MediaRepeatMode.None,
    };

    private static object? Get(IDictionary<string, object> d, string key) => d.TryGetValue(key, out var v) ? v : null;

    // xesam:artist is a list of strings; the others are plain strings.
    private static string? AsString(object? value) => value switch
    {
        null => null,
        string s => s,
        string[] arr => string.Join(", ", arr.Where(a => !string.IsNullOrWhiteSpace(a))),
        IEnumerable<string> list => string.Join(", ", list.Where(a => !string.IsNullOrWhiteSpace(a))),
        _ => value.ToString(),
    };

    // Players disagree on the integer width of mpris:length.
    private static bool TryLong(object value, out long result)
    {
        switch (value)
        {
            case long l: result = l; return true;
            case int i: result = i; return true;
            case ulong ul when ul <= long.MaxValue: result = (long)ul; return true;
            case uint ui: result = ui; return true;
            case double d: result = (long)d; return true;
            default: result = 0; return false;
        }
    }
}

/// <summary>The Player properties of one <c>GetAll</c> reply. Players omit what they do not support, so every field has a default.</summary>
public sealed record MprisState(
    string? PlaybackStatus,
    string? LoopStatus,
    bool Shuffle,
    IDictionary<string, object>? Metadata,
    long Position,
    bool CanGoNext,
    bool CanGoPrevious,
    bool CanSeek)
{
    public static MprisState From(IDictionary<string, object> all) => new(
        all.TryGetValue("PlaybackStatus", out var ps) ? ps as string : null,
        all.TryGetValue("LoopStatus", out var ls) ? ls as string : null,
        all.TryGetValue("Shuffle", out var sh) && sh is true,
        all.TryGetValue("Metadata", out var md) ? md as IDictionary<string, object> : null,
        all.TryGetValue("Position", out var pos) && pos is long l ? l : 0,
        all.TryGetValue("CanGoNext", out var gn) && gn is true,
        all.TryGetValue("CanGoPrevious", out var gp) && gp is true,
        all.TryGetValue("CanSeek", out var cs) && cs is true);
}
