using System.Text.Json.Serialization;

namespace Taskitify.Models;

public sealed record TrackInfo(string Title, string Artist, string Album);

public enum PlaybackState
{
    NoSession,
    Stopped,
    Paused,
    Changing,
    Playing,
}

public enum NoLyricsBehavior
{
    /// <summary>Keep covering the weather widget and show the track title.</summary>
    ShowTitle,

    /// <summary>Hide the overlay, so the weather widget shows.</summary>
    HideOverlay,
}

public enum LyricsState
{
    None,
    Loading,
    Synced,
    PlainOnly,
    Instrumental,
    NotFound,
    Error,
}

public enum OverlayBackground
{
    /// <summary>Spotify-like gradient made from the album art colours.</summary>
    AlbumGradient,

    /// <summary>Blend with the taskbar (sampled colour).</summary>
    Taskbar,
}

public enum PrimaryText
{
    /// <summary>Current lyric line (track name when there are no synced lyrics).</summary>
    Lyrics,

    /// <summary>Track name, like a regular media widget.</summary>
    Track,
}

public enum PausedBehavior
{
    /// <summary>Hide the overlay; the weather widget shows again.</summary>
    Hide,

    /// <summary>Keep a compact player (cover, title, play button) the size of the weather widget.</summary>
    Minimize,
}

public enum ProgressPlacement
{
    /// <summary>Full width along the bottom, under the cover too.</summary>
    Below,

    /// <summary>Starts right of the cover, which then uses the full height (a few px larger).</summary>
    BesideCover,
}

public enum RepeatMode
{
    None,
    Track,
    List,
}

/// <summary>
/// What one taskbar player shows. Two of these exist: the normal player and the minimized one
/// shown while paused (when "minimize" is chosen). Both are edited with the same picker.
/// </summary>
public sealed class BarLayout
{
    public bool ShowCover { get; set; } = true;

    /// <summary>Main line: the lyric (normal) or the track name (minimized).</summary>
    public bool ShowText { get; set; } = true;

    /// <summary>Second line: "track · artist" (normal) or the artist (minimized).</summary>
    public bool ShowCaption { get; set; }

    public bool ShowShuffle { get; set; }

    public bool ShowPrevious { get; set; }

    public bool ShowPlayPause { get; set; }

    public bool ShowNext { get; set; }

    public bool ShowRepeat { get; set; }

    public bool ShowVolume { get; set; }

    public bool ShowProgress { get; set; }

    /// <summary>DIP width for cover + text; selected buttons add their own room.</summary>
    public int Width { get; set; } = 300;

    [JsonIgnore]
    public bool AnyControl => ShowShuffle || ShowPrevious || ShowPlayPause || ShowNext || ShowRepeat || ShowVolume;

    [JsonIgnore]
    public bool AnyText => ShowText || ShowCaption;

    public static BarLayout DefaultNormal() => new() { ShowCover = true, ShowText = true, ShowCaption = true, Width = 300 };

    /// <summary>Cover, track name and a play button: about the size of the weather widget.</summary>
    public static BarLayout DefaultMinimized() => new() { ShowCover = true, ShowText = true, ShowPlayPause = true, Width = 110 };
}

/// <summary>Persisted to %AppData%\Taskitify\settings.json.</summary>
public sealed class AppSettings
{
    public OverlayBackground Background { get; set; } = OverlayBackground.AlbumGradient;

    public PrimaryText PrimaryText { get; set; } = PrimaryText.Lyrics;

    /// <summary>The normal (playing) taskbar player.</summary>
    public BarLayout Bar { get; set; } = BarLayout.DefaultNormal();

    /// <summary>The minimized player shown while paused, when <see cref="PausedBehavior"/> is Minimize.</summary>
    public BarLayout MinimizedBar { get; set; } = BarLayout.DefaultMinimized();

    public BarLayout Layout(bool minimized) => minimized ? MinimizedBar : Bar;

    public ProgressPlacement ProgressPlacement { get; set; } = ProgressPlacement.Below;

    /// <summary>Scroll single-line text that does not fit (uses a little more CPU while scrolling).</summary>
    public bool MarqueeLongText { get; set; }

    public bool EnableAnimations { get; set; } = true;

    public double FontSize { get; set; } = 12;

    public int SyncOffsetMs { get; set; }

    public NoLyricsBehavior NoLyricsBehavior { get; set; } = NoLyricsBehavior.ShowTitle;

    public PausedBehavior PausedBehavior { get; set; } = PausedBehavior.Hide;

    /// <summary>Device name (e.g. \\.\DISPLAY1) of the monitor whose taskbar is used; null = primary.</summary>
    public string? MonitorDeviceName { get; set; }

    public bool ShowTrayIcon { get; set; } = true;

    public bool DebugMode { get; set; }

    /// <summary>When true, ManualX/Y/Width/Height (physical pixels) replace automatic detection.</summary>
    public bool ManualPlacement { get; set; }

    public int ManualX { get; set; }

    public int ManualY { get; set; }

    public int ManualWidth { get; set; }

    public int ManualHeight { get; set; }

    // ---- Settings files from older versions (read once, folded into Bar, never written) ----

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowCover { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowArtist { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowShuffle { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowPrevious { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowPlayPause { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowNext { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowRepeat { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowVolume { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowProgress { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? OverlayWidth { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowControls { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowExtraControls { get; set; }

    /// <summary>Fold the flat switches of older versions into <see cref="Bar"/>.</summary>
    public void MigrateLegacy()
    {
        var b = Bar;
        if (ShowControls == true)
        {
            b.ShowPrevious = b.ShowPlayPause = b.ShowNext = true;
            if (ShowExtraControls == true)
                b.ShowShuffle = b.ShowRepeat = b.ShowVolume = true;
        }
        if (ShowCover is { } cover) b.ShowCover = cover;
        if (ShowArtist is { } artist) b.ShowCaption = artist;
        if (ShowShuffle is { } shuffle) b.ShowShuffle = shuffle;
        if (ShowPrevious is { } previous) b.ShowPrevious = previous;
        if (ShowPlayPause is { } play) b.ShowPlayPause = play;
        if (ShowNext is { } next) b.ShowNext = next;
        if (ShowRepeat is { } repeat) b.ShowRepeat = repeat;
        if (ShowVolume is { } volume) b.ShowVolume = volume;
        if (ShowProgress is { } progress) b.ShowProgress = progress;
        if (OverlayWidth is { } width) b.Width = width;

        ShowCover = ShowArtist = ShowShuffle = ShowPrevious = ShowPlayPause = ShowNext = null;
        ShowRepeat = ShowVolume = ShowProgress = ShowControls = ShowExtraControls = null;
        OverlayWidth = null;
    }
}
