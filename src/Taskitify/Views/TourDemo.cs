using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Taskitify.Core.Lyrics;
using Taskitify.Models;
using Taskitify.Services;
using Taskitify.ViewModels;

namespace Taskitify.Views;

/// <summary>A demonstration song for the welcome tour: original title, lines and cover art.</summary>
internal sealed record TourSong(string Title, string Album, string[] Lines, Color Start, Color End, Func<int, BitmapSource> Cover)
{
    public const string Artist = "Demo Artist";
    public const double LineSeconds = 4.2;
    public double Duration => (Lines.Length + 1) * LineSeconds;

    /// <summary>The songs written for the Store campaign, reused here.</summary>
    public static readonly TourSong[] All =
    [
        new("After the Rain", "Evening Studies",
            ["City lights are fading slow", "Somewhere in time", "Every little moment", "Some moments stay with us.", "Until we meet again", "Keep the music playing", "All the way back home"],
            Hex("#142D3C"), Hex("#2B6F80"), TourCovers.Rain),
        new("Midnight Echo", "Night Letters",
            ["Streetlights hum a quiet tune", "When the night falls quiet", "I still carry a little light", "Somewhere inside the sound", "We keep on dancing slow", "Until the morning comes"],
            Hex("#3A1F33"), Hex("#9B4E6E"), TourCovers.Moon),
        new("Golden Hour", "Coastline",
            ["Windows open to the sea", "Let the world slow down", "Golden hour on the avenue", "Every color sounds like you", "Stay a little longer", "Before the light is gone"],
            Hex("#3B2A16"), Hex("#C98B3A"), TourCovers.Sun),
        new("Paper Planes", "Blue Notebook",
            ["Folded letters in the wind", "There's a little light ahead", "Follow where the river bends", "Every road leads somewhere new", "Carry me along with you"],
            Hex("#16324A"), Hex("#4F8DB8"), TourCovers.Plane),
        new("Northern Lines", "Quiet Hours",
            ["Snow is falling on the pines", "Underneath the northern lines", "I could stay here forever", "Wrapped inside a melody", "Hold the night a little longer"],
            Hex("#0E2A2A"), Hex("#2F9B86"), TourCovers.Aurora),
    ];

    internal static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}

/// <summary>Plays a demonstration song on a clock, as if Spotify were playing it. Nothing real is touched.</summary>
internal sealed class TourMonitor : ISpotifyMonitor
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _loop;
    private TourSong _song;
    private ImageSource? _cover;
    private double _start;

    public TourMonitor(TourSong song, double start)
    {
        _song = song;
        _start = start;
        _loop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _loop.Tick += (_, _) =>
        {
            if (Position.TotalSeconds >= _song.Duration - 0.3)
                Seek(0);
        };
        _loop.Start();
    }

    public TourSong Song => _song;

    public void Play(TourSong song, double start)
    {
        _song = song;
        _cover = null;
        _start = start;
        _clock.Restart();
        TrackChanged?.Invoke(this, EventArgs.Empty);
        ThumbnailChanged?.Invoke(this, EventArgs.Empty);
        TimelineChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Seek(double seconds)
    {
        _start = seconds;
        _clock.Restart();
        TimelineChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetPlaying(bool playing)
    {
        if (playing == IsPlaying)
            return;
        _start = Position.TotalSeconds;
        Status = playing ? PlaybackState.Playing : PlaybackState.Paused;
        _clock.Restart();
        PlaybackChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool HasSession => true;
    public string? SessionAppId => "Spotify.exe";
    public IReadOnlyList<string> AllSessionAppIds => ["Spotify.exe"];
    public TrackInfo? Track => new(_song.Title, TourSong.Artist, _song.Album);
    public PlaybackState Status { get; private set; } = PlaybackState.Playing;
    public bool IsPlaying => Status == PlaybackState.Playing;
    public TimeSpan Position => TimeSpan.FromSeconds(_start + (IsPlaying ? _clock.Elapsed.TotalSeconds : 0));
    public TimeSpan? Duration => TimeSpan.FromSeconds(_song.Duration);
    public ImageSource? Thumbnail => _cover ??= _song.Cover(320);
    public bool CanSeek => true;
    public bool CanGoNext => true;
    public bool CanGoPrevious => true;
    public bool CanShuffle => true;
    public bool CanRepeat => true;
    public bool IsShuffleActive => false;
    public RepeatMode RepeatMode => RepeatMode.None;
    public (Color Start, Color End) Palette => (_song.Start, _song.End);
    public event EventHandler? TrackChanged;
    public event EventHandler? PlaybackChanged;
    public event EventHandler? TimelineChanged;
    public event EventHandler? ThumbnailChanged;
    public Task StartAsync() => Task.CompletedTask;
    public Task PlayPauseAsync() { SetPlaying(!IsPlaying); return Task.CompletedTask; }
    public Task NextAsync() => Task.CompletedTask;
    public Task PreviousAsync() { Seek(0); return Task.CompletedTask; }
    public Task SeekAsync(TimeSpan position) { Seek(position.TotalSeconds); return Task.CompletedTask; }
    public Task ToggleShuffleAsync() => Task.CompletedTask;
    public Task CycleRepeatAsync() => Task.CompletedTask;
    public void Dispose() => _loop.Stop();
}

internal sealed class TourLyrics(TourMonitor monitor) : ILyricsService
{
    public Task<LyricsResult> GetLyricsAsync(TrackQuery query, CancellationToken ct) => Task.FromResult(new LyricsResult(LyricsKind.Synced,
        monitor.Song.Lines.Select((text, i) => new LyricsLine(TimeSpan.FromSeconds(i * TourSong.LineSeconds), text)).ToArray(), null, query.Title, query.Artist));

    public Task ForgetAsync(TrackQuery query) => Task.CompletedTask;
}

/// <summary>
/// The real taskbar player and mini player, driven by a demonstration song and in-memory settings,
/// shown far off-screen; the tour displays snapshots of them. The user's settings, playback, lyrics
/// cache and Spotify session are never touched.
/// </summary>
internal sealed class TourStage : IDisposable
{
    private readonly LyricsSyncService _sync;
    private readonly SpotifyLikeService _likes;
    private TaskbarViewModel? _barVm;
    private TaskbarOverlay? _bar;
    private MiniPlayerViewModel? _miniVm;
    private MiniPlayerWindow? _mini;

    public TourStage(AppSettings? start = null)
    {
        Settings = SettingsService.InMemory(new AppSettings
        {
            EnableAnimations = true, SpotifyLyricsEnabled = true, TaskbarLyricFlow = LyricFlowMode.Automatic,
            Background = OverlayBackground.AlbumGradient, PausedBehavior = PausedBehavior.Minimize, LikeButtonAdded = true,
            Bar = new BarLayout { ShowCover = true, ShowText = true, ShowCaption = true, ShowPrevious = true, ShowPlayPause = true, ShowNext = true, ShowLike = true, ShowProgress = true, Width = 320 },
            MinimizedBar = BarLayout.DefaultMinimized(),
            MiniPlayerLook = new MiniPlayerStyle
            {
                Layout = MiniPlayerLayout.Cinema, Background = MiniPlayerBackground.BlurredCover, Highlight = LyricsHighlight.Expressive,
                Motion = LyricMotionMode.Expressive, Typeface = LyricsTypeface.Modern,
            },
        });
        if (start is not null)
        {
            // A copy of the user's own layout and look: changing the demonstration never changes them.
            var s = Settings.Current;
            s.Bar = Copy(start.Bar);
            s.MinimizedBar = Copy(start.MinimizedBar);
            s.PausedBehavior = start.PausedBehavior;
            s.Background = start.Background;
            s.TaskbarLyricFlow = start.TaskbarLyricFlow == LyricFlowMode.Off ? LyricFlowMode.Automatic : start.TaskbarLyricFlow;
            var look = start.MiniPlayerLook;
            s.MiniPlayerLook = new MiniPlayerStyle
            {
                Layout = look.Layout, Background = look.Background == MiniPlayerBackground.Default ? MiniPlayerBackground.BlurredCover : look.Background,
                Highlight = look.Highlight, Motion = look.Motion, Typeface = look.Typeface, Alignment = look.Alignment, Controls = look.Controls,
                LyricsScale = look.LyricsScale, Flow = look.Flow,
            };
        }
        Monitor = new TourMonitor(TourSong.All[0], 3 * TourSong.LineSeconds + 1.2);
        _sync = new LyricsSyncService(Monitor, new TourLyrics(Monitor), Settings);
        _sync.Reload();
        _likes = new SpotifyLikeService(Monitor, _sync, Settings, liked: true);
    }

    private static BarLayout Copy(BarLayout b) => new()
    {
        ShowCover = b.ShowCover, ShowText = b.ShowText, ShowCaption = b.ShowCaption, ShowShuffle = b.ShowShuffle, ShowPrevious = b.ShowPrevious,
        ShowPlayPause = b.ShowPlayPause, ShowNext = b.ShowNext, ShowRepeat = b.ShowRepeat, ShowVolume = b.ShowVolume, ShowLike = b.ShowLike,
        ShowProgress = b.ShowProgress, Width = b.Width,
    };

    /// <summary>Plays or pauses the demonstration; the player takes the paused layout's width, as on the taskbar.</summary>
    public void SetPlaying(bool playing)
    {
        Monitor.SetPlaying(playing);
        if (_bar is not null)
            _bar.Width = Settings.Current.Layout(!playing).Width;
    }

    public SettingsService Settings { get; }

    public TourMonitor Monitor { get; }

    public void Play(TourSong song, int line = 2)
    {
        Monitor.Play(song, line * TourSong.LineSeconds + 1.2);
        _sync.Reload();
    }

    public void Update(Action<AppSettings> change)
    {
        Settings.Update(change);
        if (_bar is not null)
            _bar.Width = Settings.Current.Layout(!Monitor.IsPlaying).Width;
    }

    private RenderTargetBitmap? _barBitmap, _miniBitmap;

    /// <summary>
    /// Wake only the players a step shows. A hidden player stops its own animations and timers
    /// (its lyric ink and scrolling check visibility), so steps without them cost nothing.
    /// </summary>
    public void Use(bool bar, bool mini)
    {
        if (!bar && _bar is { IsVisible: true })
            _bar.Hide();
        if (!mini && _mini is { IsVisible: true })
            _mini.Hide();
        if (bar && _bar is { IsVisible: false })
            _bar.Show();
        if (mini && _mini is { IsVisible: false })
            _mini.ShowOffscreen();
    }

    /// <summary>The taskbar player's content, shown live (GPU) by a VisualBrush; null while it hides.</summary>
    public FrameworkElement? BarVisual()
    {
        Bar(1);
        return !Monitor.IsPlaying && Settings.Current.PausedBehavior == PausedBehavior.Hide ? null : (FrameworkElement?)_bar?.Content;
    }

    /// <summary>The mini player window's content, shown live by a VisualBrush.</summary>
    public FrameworkElement? MiniVisual()
    {
        if (_mini is null)
            Mini(1);
        return _mini?.ContentCard; // the card itself: no shadow margin around it
    }

    /// <summary>The taskbar player as it looks now; null while it hides (paused, "give the space back").</summary>
    public BitmapSource? Bar(double scale)
    {
        if (_bar is null)
        {
            _barVm = new TaskbarViewModel(Monitor, _sync, Settings, new AppVolumeService(), _likes);
            _bar = new TaskbarOverlay(_barVm) { Left = -20000, Top = -20000, Height = 48, ShowActivated = false, Topmost = false, ShowInTaskbar = false };
            _bar.Width = Settings.Current.Layout(!Monitor.IsPlaying).Width;
            _bar.Show();
        }
        if (!Monitor.IsPlaying && Settings.Current.PausedBehavior == PausedBehavior.Hide)
            return null;
        return Snapshot(_bar, scale, ref _barBitmap);
    }

    public BitmapSource Mini(double scale)
    {
        if (_mini is null)
        {
            _miniVm = new MiniPlayerViewModel(Monitor, _sync, Settings, new AppVolumeService(), _likes);
            _mini = new MiniPlayerWindow(_miniVm, () => true, () => true) { ShowActivated = false };
            _mini.ShowOffscreen();
        }
        return Snapshot(_mini, scale, ref _miniBitmap);
    }

    /// <summary>Renders into the same bitmap while the size holds: no new bitmap every frame.</summary>
    private static BitmapSource Snapshot(Window window, double scale, ref RenderTargetBitmap? bitmap)
    {
        var w = Math.Max(1, (int)Math.Round(window.ActualWidth * scale));
        var h = Math.Max(1, (int)Math.Round(window.ActualHeight * scale));
        if (bitmap is null || bitmap.PixelWidth != w || bitmap.PixelHeight != h)
            bitmap = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        else
            bitmap.Clear();
        bitmap.Render(window);
        return bitmap;
    }

    public void Dispose()
    {
        _bar?.Close();
        _mini?.Close();
        _barVm?.Dispose();
        _miniVm?.Dispose();
        _likes.Dispose();
        _sync.Dispose();
        Monitor.Dispose();
    }
}

/// <summary>Original cover art for the demonstration songs (the Store campaign's).</summary>
internal static class TourCovers
{
    private static readonly Dictionary<(string, int), BitmapSource> Cache = [];

    private static BitmapSource Paint(string key, int size, Action<DrawingContext> draw)
    {
        if (Cache.TryGetValue((key, size), out var cached))
            return cached;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 300.0, size / 300.0));
            draw(dc);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return Cache[(key, size)] = bitmap;
    }

    private static SolidColorBrush B(string hex) => new(TourSong.Hex(hex));

    private static void Fill(DrawingContext dc, string color, string path) => dc.DrawGeometry(B(color), null, Geometry.Parse(path));

    public static BitmapSource Rain(int size) => Paint("rain", size, dc =>
    {
        dc.DrawRectangle(new LinearGradientBrush(TourSong.Hex("#189BB3"), TourSong.Hex("#1F7F95"), 60), null, new Rect(0, 0, 300, 300));
        Fill(dc, "#E2C46F", "M0,0 L132,0 L104,96 L0,118 Z");
        Fill(dc, "#142D3C", "M170,0 L300,0 L300,140 L226,118 Z");
        Fill(dc, "#356B79", "M0,300 L0,196 L120,170 L176,300 Z");
        Fill(dc, "#F1F2ED", "M214,300 L246,210 L300,198 L300,300 Z");
        Fill(dc, "#AF9B67", "M118,168 L196,150 L176,226 Z");
        var dark = new Pen(B("#122733"), 15) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var cream = new Pen(B("#F1F2ED"), 6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(dark, new Point(64, 34), new Point(98, 262));
        dc.DrawLine(dark, new Point(150, 26), new Point(132, 250));
        dc.DrawLine(dark, new Point(212, 60), new Point(248, 238));
        dc.DrawLine(dark, new Point(98, 262), new Point(176, 226));
        dc.DrawLine(cream, new Point(24, 128), new Point(276, 72));
        dc.DrawLine(cream, new Point(40, 230), new Point(262, 146));
        dc.DrawLine(cream, new Point(186, 40), new Point(270, 270));
    });

    public static BitmapSource Moon(int size) => Paint("moon", size, dc =>
    {
        dc.DrawRectangle(new LinearGradientBrush(TourSong.Hex("#2A1430"), TourSong.Hex("#C0607E"), 90), null, new Rect(0, 0, 300, 300));
        dc.DrawEllipse(B("#F6D6C8"), null, new Point(150, 118), 62, 62);
        dc.DrawEllipse(new LinearGradientBrush(TourSong.Hex("#3A1B38"), TourSong.Hex("#5E2A4C"), 90), null, new Point(178, 100), 56, 56);
        for (int i = 0; i < 5; i++)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(60 + i * 30), 0x24, 0x0E, 0x22)), null, new Rect(0, 210 + i * 18, 300, 18));
        foreach (var (x, y) in new[] { (40, 40), (250, 60), (70, 170), (230, 180), (110, 30) })
            dc.DrawEllipse(B("#FBE7DC"), null, new Point(x, y), 2.2, 2.2);
    });

    public static BitmapSource Sun(int size) => Paint("sun", size, dc =>
    {
        dc.DrawRectangle(new LinearGradientBrush(TourSong.Hex("#F2C46D"), TourSong.Hex("#C4603A"), 90), null, new Rect(0, 0, 300, 300));
        dc.DrawEllipse(B("#FFF1CF"), null, new Point(150, 170), 70, 70);
        for (int i = 0; i < 6; i++)
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb((byte)(0x2C + i * 6), (byte)(0x4A + i * 8), (byte)(0x6B + i * 6))), null, new Rect(0, 182 + i * 20, 300, 12 + i * 2));
    });

    public static BitmapSource Plane(int size) => Paint("plane", size, dc =>
    {
        dc.DrawRectangle(new LinearGradientBrush(TourSong.Hex("#BFD9EA"), TourSong.Hex("#4F8DB8"), 90), null, new Rect(0, 0, 300, 300));
        dc.DrawGeometry(B("#F7F4EC"), null, Geometry.Parse("M60,190 L240,90 L150,215 L130,170 Z"));
        dc.DrawGeometry(B("#D8D2C3"), null, Geometry.Parse("M130,170 L240,90 L150,215 Z"));
        dc.DrawGeometry(null, new Pen(B("#F7F4EC"), 3) { DashStyle = new DashStyle([3, 3], 0) }, Geometry.Parse("M60,190 C30,230 80,270 20,290"));
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), null, new Point(80, 70), 46, 18);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), null, new Point(220, 230), 60, 20);
    });

    public static BitmapSource Aurora(int size) => Paint("aurora", size, dc =>
    {
        dc.DrawRectangle(new LinearGradientBrush(TourSong.Hex("#08161E"), TourSong.Hex("#123B3F"), 90), null, new Rect(0, 0, 300, 300));
        dc.DrawGeometry(new LinearGradientBrush(Color.FromArgb(200, 0x5C, 0xF2, 0xB0), Color.FromArgb(0, 0x5C, 0xF2, 0xB0), 90), null, Geometry.Parse("M0,120 C80,40 160,160 300,60 L300,170 C180,220 90,120 0,200 Z"));
        dc.DrawGeometry(new LinearGradientBrush(Color.FromArgb(150, 0x9A, 0x7B, 0xF0), Color.FromArgb(0, 0x9A, 0x7B, 0xF0), 90), null, Geometry.Parse("M0,80 C100,20 200,110 300,30 L300,100 C200,160 110,60 0,140 Z"));
        dc.DrawGeometry(B("#06110F"), null, Geometry.Parse("M0,300 L0,240 L30,250 L50,200 L70,250 L95,235 L120,180 L150,245 L180,225 L205,190 L230,240 L260,215 L300,250 L300,300 Z"));
    });
}
