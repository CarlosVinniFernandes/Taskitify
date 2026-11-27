using System.Text.Json.Serialization;
using Taskitify.Core.Lyrics;

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

    /// <summary>Heart that adds the song to Liked Songs (only while Spotify is connected).</summary>
    public bool ShowLike { get; set; }

    public bool ShowProgress { get; set; }

    /// <summary>DIP width for cover + text; selected buttons add their own room.</summary>
    public int Width { get; set; } = 300;

    /// <summary>The range of <see cref="Width"/> (its slider's) for the normal and the minimized taskbar player.</summary>
    public static (int Min, int Max) WidthRange(bool minimized) => minimized ? (60, 600) : (100, 900);

    public BarLayout Clone() => (BarLayout)MemberwiseClone();

    /// <summary>Copies what is shown, not the width.</summary>
    public void CopyElementsTo(BarLayout target)
    {
        var width = target.Width;
        foreach (var property in typeof(BarLayout).GetProperties())
        {
            if (property.CanWrite)
                property.SetValue(target, property.GetValue(this));
        }
        target.Width = width;
    }

    public bool SameElements(BarLayout other) => typeof(BarLayout).GetProperties()
        .Where(p => p.CanWrite && p.Name != nameof(Width))
        .All(p => Equals(p.GetValue(this), p.GetValue(other)));

    [JsonIgnore]
    public bool AnyControl => ShowShuffle || ShowPrevious || ShowPlayPause || ShowNext || ShowRepeat || ShowVolume || ShowLike;

    [JsonIgnore]
    public bool AnyText => ShowText || ShowCaption;

    /// <summary>Cover, lyric, the playback buttons, volume and progress.</summary>
    public static BarLayout DefaultNormal() => new()
    {
        ShowCover = true, ShowText = true, ShowPrevious = true, ShowPlayPause = true, ShowNext = true, ShowVolume = true,
        ShowLike = true, ShowProgress = true, Width = 300,
    };

    /// <summary>Cover, track name, artist and a play button.</summary>
    public static BarLayout DefaultMinimized() => new() { ShowCover = true, ShowText = true, ShowCaption = true, ShowPlayPause = true, Width = 180 };

    /// <summary>The mini player as it always looked; Width is its card's (the lyrics are always there).</summary>
    public static BarLayout DefaultMiniPlayer() => new()
    {
        ShowCover = true, ShowText = true, ShowCaption = true, ShowPrevious = true, ShowPlayPause = true, ShowNext = true,
        ShowVolume = true, ShowLike = true, ShowProgress = true, Width = 360,
    };
}

/// <summary>Persisted to %AppData%\Taskitify\settings.json.</summary>
public sealed class AppSettings
{
    public string Language { get; set; } = "en";

    public OverlayBackground Background { get; set; } = OverlayBackground.AlbumGradient;

    public PrimaryText PrimaryText { get; set; } = PrimaryText.Lyrics;

    /// <summary>The normal (playing) taskbar player.</summary>
    public BarLayout Bar { get; set; } = BarLayout.DefaultNormal();

    /// <summary>The minimized player shown while paused, when <see cref="PausedBehavior"/> is Minimize.</summary>
    public BarLayout MinimizedBar { get; set; } = BarLayout.DefaultMinimized();

    public BarLayout Layout(bool minimized) => minimized ? MinimizedBar : Bar;

    /// <summary>The popup with the lyrics, opened by clicking the taskbar player.</summary>
    public BarLayout MiniPlayer { get; set; } = BarLayout.DefaultMiniPlayer();

    public ProgressPlacement ProgressPlacement { get; set; } = ProgressPlacement.BesideCover;

    /// <summary>Scroll single-line text that does not fit (uses a little more CPU while scrolling).</summary>
    public bool MarqueeLongText { get; set; }

    public bool EnableAnimations { get; set; } = true;

    public LyricFlowMode TaskbarLyricFlow { get; set; }

    public double FontSize { get; set; } = 15;

    public int SyncOffsetMs { get; set; }

    /// <summary>
    /// Adjustments for single songs whose lyrics run early or late, added to <see cref="SyncOffsetMs"/>;
    /// keyed by <see cref="TrackKey"/>. Only songs with an adjustment are kept.
    /// </summary>
    public Dictionary<string, int> TrackOffsets { get; set; } = [];

    public const int GlobalOffsetLimit = 5000;
    public const int TrackOffsetLimit = 3000;

    /// <summary>
    /// A song's stable identity: its artist and title as Windows reports them, ignoring case and
    /// spacing. Windows' media session gives no Spotify id, and the id Spotify's lyrics lookup
    /// learns is missing for songs found in the cache, local files or LRCLIB, so the names are
    /// what is always there. Remasters and live versions keep their own (their names differ).
    /// </summary>
    public static string TrackKey(string title, string artist)
    {
        static string Clean(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        return Clean(artist) + " — " + Clean(title);
    }

    public int TrackOffsetMs(string? key) => key is not null && TrackOffsets.TryGetValue(key, out var ms) ? ms : 0;

    /// <summary>What the lyrics are moved by for this song: both adjustments together, within safe limits.</summary>
    public int EffectiveOffsetMs(string? key) => Math.Clamp(SyncOffsetMs + TrackOffsetMs(key), -GlobalOffsetLimit, GlobalOffsetLimit);

    /// <summary>Sets a song's adjustment; zero removes it.</summary>
    public void SetTrackOffset(string key, int ms)
    {
        ms = Math.Clamp(ms, -TrackOffsetLimit, TrackOffsetLimit);
        if (ms == 0)
            TrackOffsets.Remove(key);
        else
            TrackOffsets[key] = ms;
    }

    public NoLyricsBehavior NoLyricsBehavior { get; set; } = NoLyricsBehavior.ShowTitle;

    public PausedBehavior PausedBehavior { get; set; } = PausedBehavior.Minimize;

    /// <summary>Device name (e.g. \\.\DISPLAY1) of the monitor whose taskbar is used; null = primary.</summary>
    public string? MonitorDeviceName { get; set; }

    public bool ShowTrayIcon { get; set; } = true;

    /// <summary>Keep a desktop shortcut that opens the latest copy of the app.</summary>
    public bool DesktopShortcut { get; set; } = true;

    public bool DebugMode { get; set; }

    public bool SpotifyLyricsEnabled { get; set; }

    /// <summary>The like button was added to the layouts saved by older versions (once).</summary>
    public bool LikeButtonAdded { get; set; }

    /// <summary>The welcome tour seen (0: never). Shown once per tour version, to new and updating users.</summary>
    public int TourVersion { get; set; }

    /// <summary>Local only, for the one-time rating invitation: first day used, days with music, whether it was shown.</summary>
    public string? FirstUseDay { get; set; }

    public int ListeningDays { get; set; }

    public string? LastListeningDay { get; set; }

    public bool RatingInviteShown { get; set; }

    /// <summary>The lyric card as it was last made (style, format, typography...).</summary>
    public LyricCardOptions LyricCard { get; set; } = new();

    /// <summary>How the mini player looks: layout, background, lyrics and controls, each on its own.</summary>
    public MiniPlayerStyle MiniPlayerLook { get; set; } = new();

    /// <summary>Minimal Mode: the lyrics floating over the desktop, in a window of their own.</summary>
    public MinimalModeSettings Minimal { get; set; } = new();

    /// <summary>Mini player looks the user saved under a name (shown with the ready-made templates).</summary>
    public List<MiniPlayerPreset> MiniPlayerPresets { get; set; } = [];

    /// <summary>Card settings the user saved under a name.</summary>
    public List<LyricCardPreset> LyricCardPresets { get; set; } = [];

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
        // The like button came after these layouts were saved: it shows up once on the normal
        // player and the mini player (it only appears while Spotify is connected), like for new users.
        if (!LikeButtonAdded)
        {
            Bar.ShowLike = true;
            MiniPlayer.ShowLike = true;
            LikeButtonAdded = true;
        }

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

/// <summary>A lyric card's composition; each one is laid out differently, not just recoloured.</summary>
public enum LyricCardStyle
{
    Classic,
    Gradient,
    Blur,
    Polaroid,
    Minimal,
    Editorial,
    Player,
    Vinyl,
    Karaoke,
    Retro,
    PhotoBooth,
    Letter,
    FilmNegative,
    Telegram,
    MuseumExhibit,
    MsnMessenger,
    WindowsXpError,
    FloppyDisk,
    Terminal,
    BlueScreen,
    AimAway,
    Calculator,
    Paint98,
    Orkut,
    IpodClassic,
    CassetteTape,
    ConcertTicket,
    LyricReceipt,
    BoardingPass,
    LibraryCard,
    MusicPassport,
    ClassifiedDocument,
    Dictionary,
    BookPage,
    PeriodicTable,
    Crossword,
    GameBoy,
    NintendoDs,
    PokemonEncounter,
    RpgInventory,
    MagicCard,
    Chess,
}

public enum LyricCardFormat
{
    /// <summary>1080 × 1920, stories and reels.</summary>
    Story,

    /// <summary>1080 × 1080.</summary>
    Square,

    /// <summary>1080 × 1350 (4:5), feed posts.</summary>
    Portrait,

    /// <summary>1920 × 1080 (16:9), wallpapers, videos and X.</summary>
    Landscape,
}

/// <summary>The lyrics' typeface; Auto lets each style use its own.</summary>
public enum LyricCardFont
{
    Auto,
    Modern,
    Serif,
    Handwritten,
    Mono,
    Condensed,
}

public enum LyricCardAlign
{
    Auto,
    Left,
    Center,
    Right,
}

/// <summary>The signature in a corner of the card.</summary>
public enum LyricCardMark
{
    Spotify,
    Taskitify,
    None,
}

public sealed class LyricCardOptions
{
    public LyricCardStyle Style { get; set; } = LyricCardStyle.Classic;

    public LyricCardFormat Format { get; set; } = LyricCardFormat.Story;

    public LyricCardFont Font { get; set; } = LyricCardFont.Auto;

    /// <summary>The lyrics' size relative to the style's own (0.7 to 1.4); text that would not fit still shrinks.</summary>
    public double FontScale { get; set; } = 1;

    public LyricCardAlign Align { get; set; } = LyricCardAlign.Auto;

    /// <summary>#RRGGBB; null for the album's own colour.</summary>
    public string? Color { get; set; }

    public bool ShowCover { get; set; } = true;

    public LyricCardMark Mark { get; set; } = LyricCardMark.Spotify;

    /// <summary>The time of the first chosen line, as in the song.</summary>
    public bool ShowTimestamp { get; set; }

    public Dictionary<LyricCardStyle, LyricCardVariant> Variants { get; set; } = [];

    public LyricCardOptions Clone()
    {
        var copy = (LyricCardOptions)MemberwiseClone();
        copy.Variants = Variants?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()) ?? [];
        return copy;
    }
}

public enum StudioImageSource
{
    Album,
    Personal,
    None,
}

public sealed class LyricCardVariant
{
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.Ordinal);
    public LyricCardFont? Typeface { get; set; }
    public StudioImageSource ImageSource { get; set; }
    public string? PhotoPath { get; set; }

    public LyricCardVariant Clone() => new()
    {
        Fields = new Dictionary<string, string>(Fields ?? [], StringComparer.Ordinal),
        Typeface = Typeface,
        ImageSource = ImageSource,
        PhotoPath = PhotoPath,
    };
}

public sealed class LyricCardPreset
{
    public string Name { get; set; } = "";

    public LyricCardOptions Options { get; set; } = new();
}

public enum MiniPlayerLayout
{
    /// <summary>Cover and song on top, lyrics, then progress and controls at the bottom.</summary>
    Vertical,

    /// <summary>Wide: a large cover on the left, the lyrics on the right, controls across the bottom.</summary>
    Cinema,
}

public enum MiniPlayerBackground
{
    /// <summary>Whatever the taskbar player uses (album gradient or the app theme), as before.</summary>
    Default,
    Theme,
    AlbumGradient,
    Aurora,
    Atmosphere,
    Velvet,
    BlurredCover,
}

public enum LyricMotionMode
{
    Off,
    Smooth,
    Expressive,
}

public enum LyricsHighlight
{
    /// <summary>Lines the same size, the current one brighter.</summary>
    Classic,

    /// <summary>The current line larger and bolder, the others quiet.</summary>
    Expressive,

    /// <summary>Lines fade the further they are from the current one.</summary>
    Progressive,
}

public enum LyricsTypeface
{
    /// <summary>Windows' own (Segoe UI).</summary>
    System,

    /// <summary>Inter Display: heavy, tight display type.</summary>
    Modern,

    Serif,

    /// <summary>Figtree: round, geometric.</summary>
    Geometric,
}

public enum LyricsAlignment
{
    Center,
    Left,
}

public enum ControlsLook
{
    /// <summary>Outline icons, play in a filled circle.</summary>
    Classic,

    /// <summary>Solid icons, a large play without a circle.</summary>
    Solid,
}

/// <summary>The mini player's appearance. Every part is independent of the others.</summary>
public sealed class MiniPlayerStyle
{
    public MiniPlayerLayout Layout { get; set; } = MiniPlayerLayout.Vertical;

    public MiniPlayerBackground Background { get; set; } = MiniPlayerBackground.Default;

    public LyricsHighlight Highlight { get; set; } = LyricsHighlight.Classic;

    public LyricFlowMode Flow { get; set; }

    public LyricMotionMode Motion { get; set; }

    public LyricsTypeface Typeface { get; set; } = LyricsTypeface.Modern;

    /// <summary>1: the display typeface became the default (looks saved before keep their other choices).</summary>
    public int Revision { get; set; } = 1;

    /// <summary>The lyrics' size: 0.85 to 1.3 of the usual 14 px.</summary>
    public double LyricsScale { get; set; } = 1;

    public LyricsAlignment Alignment { get; set; } = LyricsAlignment.Center;

    public ControlsLook Controls { get; set; } = ControlsLook.Classic;

    /// <summary>The atmospheric backgrounds' colour clouds: 0.3 (faint) to 1.5 (vivid); 1 as they always were.</summary>
    public double BackgroundEffect { get; set; } = 1;

    /// <summary>How much the album backgrounds are darkened: 0 (as always) to 0.6.</summary>
    public double BackgroundDim { get; set; }

    /// <summary>The blurred cover's blur: 0.4 to 1.8; 1 as it always was.</summary>
    public double BackgroundBlur { get; set; } = 1;

    public static double ClampEffect(double v) => Math.Clamp(Math.Round(v * 20) / 20, 0.3, 1.5);
    public static double ClampDim(double v) => Math.Clamp(Math.Round(v * 20) / 20, 0, 0.6);
    public static double ClampBlur(double v) => Math.Clamp(Math.Round(v * 20) / 20, 0.4, 1.8);

    /// <summary>Which of the intensity settings the background has (the others have nothing to act on).</summary>
    public static (bool Effect, bool Dim, bool Blur) Tunable(MiniPlayerBackground background, bool defaultIsGradient) => background switch
    {
        MiniPlayerBackground.Aurora or MiniPlayerBackground.Atmosphere or MiniPlayerBackground.Velvet => (true, true, false),
        MiniPlayerBackground.BlurredCover => (false, true, true),
        MiniPlayerBackground.AlbumGradient => (false, true, false),
        MiniPlayerBackground.Default => (false, defaultIsGradient, false),
        _ => (false, false, false),
    };

    public MiniPlayerStyle Clone() => (MiniPlayerStyle)MemberwiseClone();

    /// <summary>Copies every part of the look except the layout: a look never turns a vertical player wide or back.</summary>
    public void CopyLookTo(MiniPlayerStyle target)
    {
        var layout = target.Layout;
        foreach (var property in typeof(MiniPlayerStyle).GetProperties())
        {
            if (property.CanWrite)
                property.SetValue(target, property.GetValue(this));
        }
        target.Layout = layout;
    }

    /// <summary>The same look, whatever the layout.</summary>
    public bool SameLook(MiniPlayerStyle other) => typeof(MiniPlayerStyle).GetProperties()
        .Where(p => p.Name is not (nameof(Layout) or nameof(Revision)))
        .All(p => Equals(p.GetValue(this), p.GetValue(other)));
}

/// <summary>A mini player look saved by the user: everything but the layout and the width.</summary>
public sealed class MiniPlayerPreset
{
    public string Name { get; set; } = "";

    public MiniPlayerStyle Look { get; set; } = new();

    /// <summary>The elements shown (cover, title, buttons, progress); the width stays the user's.</summary>
    public BarLayout Elements { get; set; } = BarLayout.DefaultMiniPlayer();

    public static MiniPlayerPreset From(string name, AppSettings settings) => new()
    {
        Name = name,
        Look = settings.MiniPlayerLook.Clone(),
        Elements = settings.MiniPlayer.Clone(),
    };

    public bool Matches(AppSettings settings) => Look.SameLook(settings.MiniPlayerLook) && Elements.SameElements(settings.MiniPlayer);

    public void ApplyTo(AppSettings settings)
    {
        Look.CopyLookTo(settings.MiniPlayerLook);
        Elements.CopyElementsTo(settings.MiniPlayer);
    }
}

/// <summary>How many verses Minimal Mode shows around the one being sung.</summary>
public enum MinimalLines
{
    /// <summary>Only the current verse.</summary>
    Current,

    /// <summary>The current verse and the next one, like karaoke.</summary>
    Next,

    /// <summary>One before, the current one, one after.</summary>
    Three,

    /// <summary>Two before and two after.</summary>
    Five,
}

/// <summary>What sits behind Minimal Mode's lyrics.</summary>
public enum MinimalBackdrop
{
    /// <summary>Nothing: the lyrics alone on the desktop.</summary>
    Clear,

    /// <summary>A dark, translucent panel.</summary>
    Soft,

    /// <summary>The album's colours (the Aurora background), translucent.</summary>
    Album,
}

public enum MinimalTextColor
{
    White,

    /// <summary>A warm off-white, easier on the eyes over photos.</summary>
    Warm,

    /// <summary>A light tone of the album's main colour.</summary>
    Album,

    /// <summary>Near black, for light wallpapers and windows.</summary>
    Dark,
}

/// <summary>Keeps the lyrics readable over any surface.</summary>
public enum MinimalLegibility
{
    None,

    /// <summary>A soft shadow around the letters.</summary>
    Shadow,

    /// <summary>A thin, tight halo: reads on busy and light surfaces too.</summary>
    Outline,
}

/// <summary>
/// Minimal Mode's settings. Its look (type, highlight, motion...) is its own, not the mini
/// player's, so the two can differ; the position is in physical pixels (0 width: not placed yet).
/// By default it is minimal indeed: the verse being sung between a smaller one before and after
/// it, no background, no buttons, and
/// every click going through to what is underneath (a game keeps all of its input).
/// </summary>
public sealed class MinimalModeSettings
{
    public bool Enabled { get; set; }

    /// <summary>Also keep the taskbar player while the floating lyrics are on (off: the lyrics alone).</summary>
    public bool KeepTaskbar { get; set; }

    public bool Topmost { get; set; } = true;

    /// <summary>The window can be neither moved nor resized.</summary>
    public bool Locked { get; set; }

    /// <summary>Clicks go to whatever is under the lyrics (turned off from the tray or the settings, to move them).</summary>
    public bool ClickThrough { get; set; } = true;

    public LyricsHighlight Highlight { get; set; } = LyricsHighlight.Progressive;

    public LyricMotionMode Motion { get; set; } = LyricMotionMode.Smooth;

    public LyricsTypeface Typeface { get; set; } = LyricsTypeface.Modern;

    public LyricsAlignment Alignment { get; set; } = LyricsAlignment.Center;

    /// <summary>The current verse's size in DIPs.</summary>
    public double FontSize { get; set; } = 30;

    public MinimalLines Lines { get; set; } = MinimalLines.Three;

    /// <summary>Verses get smaller the further they are from the current one (small, medium, current, medium, small).</summary>
    public bool GraduatedSizes { get; set; } = true;

    /// <summary>
    /// The text gets smaller when the window is too small for the verses chosen (never larger than
    /// <see cref="FontSize"/>): resizing the window resizes the lyrics.
    /// </summary>
    public bool AutoFit { get; set; } = true;

    /// <summary>Room between verses, relative to the usual (0.6 to 1.8).</summary>
    public double LineSpacing { get; set; } = 1;

    public MinimalBackdrop Backdrop { get; set; } = MinimalBackdrop.Clear;

    /// <summary>The background's opacity (0 to 1); a clear background has none.</summary>
    public double BackdropOpacity { get; set; } = 0.55;

    public MinimalTextColor TextColor { get; set; } = MinimalTextColor.White;

    public MinimalLegibility Legibility { get; set; } = MinimalLegibility.Outline;

    /// <summary>The song's title and artist above the lyrics.</summary>
    public bool ShowSong { get; set; }

    /// <summary>Previous, play/pause and next under the lyrics.</summary>
    public bool ShowControls { get; set; }

    // The window's own buttons (shown while it catches clicks), each optional; and the small
    // crossed-out pointer shown while clicks pass through. Ctrl + Alt + click, the tray and the
    // settings always work, whatever is hidden.
    public bool ShowMiniPlayerButton { get; set; } = true;
    public bool ShowSettingsButton { get; set; } = true;
    public bool ShowClickThroughButton { get; set; } = true;
    public bool ShowLockButton { get; set; } = true;
    public bool ShowCloseButton { get; set; } = true;
    public bool ShowClickThroughMark { get; set; } = true;

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>Verses shown before and after the current one.</summary>
    public (int Before, int After) Around => Lines switch
    {
        MinimalLines.Current => (0, 0),
        MinimalLines.Next => (0, 1),
        MinimalLines.Five => (2, 2),
        _ => (1, 1),
    };

    public static double ClampFontSize(double size) => Math.Clamp(Math.Round(size), 18, 64);

    /// <summary>Height of one verse's slot for a font size (the line, its padding above and below).</summary>
    public double VerseHeight(double fontSize) => fontSize * (1.14 + (0.4 * ClampSpacing(LineSpacing)));

    /// <summary>
    /// The largest font size at which the verses shown fit a lyrics area of this height (DIPs), and
    /// most of the song's verses its width (<paramref name="widthAt100"/>: a typical verse's width at
    /// 100 px, 0 if unknown). Long verses may still wrap rather than make everything tiny.
    /// </summary>
    public double FitFontSize(double height, double width, double widthAt100)
    {
        var (before, after) = Around;
        var byHeight = height * 0.96 / ((before + after + 1) * VerseHeight(1));
        var byWidth = widthAt100 > 0 ? (width - 12) * 0.98 / widthAt100 * 100 : double.MaxValue;
        var fit = Math.Min(byHeight, Math.Max(byWidth, ClampFontSize(FontSize) * 0.6));
        return Math.Clamp(Math.Floor(fit), 10, ClampFontSize(FontSize));
    }

    /// <summary>A verse's size relative to the current one's, by its distance from it.</summary>
    public double SizeAt(int distance) => !GraduatedSizes ? 1 : Math.Abs(distance) switch
    {
        0 => 1,
        1 => 0.78,
        _ => 0.62,
    };

    public static double ClampSpacing(double spacing) => Math.Clamp(Math.Round(spacing * 10) / 10, 0.6, 1.8);
}
