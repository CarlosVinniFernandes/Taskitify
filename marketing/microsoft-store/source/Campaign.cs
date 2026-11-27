using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Taskitify.Core.Lyrics;
using Taskitify.Models;
using Taskitify.Services;
using Taskitify.ViewModels;
using Taskitify.Views;

/// <summary>A demonstration song: original title, lines and cover art.</summary>
internal sealed record Song(string Key, string Title, string Artist, string Album, (double At, string Text)[] Lines, double Position,
    double Duration, Color Start, Color End, Func<int, BitmapSource> Cover);

// Campaign components: the app's own views and Lyrics Studio renderer, with original demonstration
// songs (titles, lines and covers written for the campaign).
//   dotnet run --project marketing/microsoft-store/source -c Release -- <repo root> --campaign
internal static partial class Program
{
    static Song[] Songs() =>
    [
        new("rain", "After the Rain", "Demo Artist", "Evening Studies",
            [(0, "City lights are fading slow"), (7, "Somewhere in time"), (13, "Every little moment"), (19, "Some moments stay with us."), (26, "Until we meet again"), (32, "Keep the music playing"), (38, "All the way back home")],
            22.4, 161, Hex("#142D3C"), Hex("#2B6F80"), Cover),
        new("echo", "Midnight Echo", "Demo Artist", "Night Letters",
            [(0, "Streetlights hum a quiet tune"), (6, "When the night falls quiet"), (12, "I still carry a little light"), (19, "Somewhere inside the sound"), (25, "We keep on dancing slow"), (31, "Until the morning comes")],
            15.1, 204, Hex("#3A1F33"), Hex("#9B4E6E"), MoonCover),
        new("golden", "Golden Hour", "Demo Artist", "Coastline",
            [(0, "Windows open to the sea"), (6, "Let the world slow down"), (12, "Golden hour on the avenue"), (18, "Every color sounds like you"), (24, "Stay a little longer"), (30, "Before the light is gone")],
            8.6, 187, Hex("#3B2A16"), Hex("#C98B3A"), SunCover),
        new("planes", "Paper Planes", "Demo Artist", "Blue Notebook",
            [(0, "Folded letters in the wind"), (6, "There's a little light ahead"), (12, "Follow where the river bends"), (18, "Every road leads somewhere new"), (24, "Carry me along with you")],
            8.9, 176, Hex("#16324A"), Hex("#4F8DB8"), PlaneCover),
        new("north", "Northern Lines", "Demo Artist", "Quiet Hours",
            [(0, "Snow is falling on the pines"), (6, "Underneath the northern lines"), (12, "I could stay here forever"), (18, "Wrapped inside a melody"), (24, "Hold the night a little longer")],
            14.2, 199, Hex("#0E2A2A"), Hex("#2F9B86"), AuroraCover),
    ];

    static string _comp = "";

    static void Campaign(string root)
    {
        _out = Path.Combine(root, "marketing", "microsoft-store");
        _fonts = Path.Combine(root, "src", "Taskitify", "Resources", "Fonts");
        _comp = Path.Combine(_out, "raw-captures", "components");
        Directory.CreateDirectory(_comp);
        var data = Path.Combine(Path.GetTempPath(), "taskitify-store-shot");
        Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable("TASKITIFY_DATA_DIR", data);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Taskitify;component/Resources/Styles.xaml") });
        resources["BoolToVisibility"] = new BooleanToVisibilityConverter();
        resources["NullToCollapsed"] = new Taskitify.Infrastructure.NullToCollapsedConverter();
        app.Resources = resources;
        using var theme = new ThemeService();
        LocalizationService.SetLanguage("en");

        var songs = Songs().ToDictionary(s => s.Key);
        foreach (var song in songs.Values)
            Save(song.Cover(640), Path.Combine(_comp, $"cover-{song.Key}.png"));

        var full = new BarLayout { ShowCover = true, ShowText = true, ShowCaption = true, ShowPrevious = true, ShowPlayPause = true, ShowNext = true, ShowLike = true, ShowProgress = true, Width = 300 };
        var lyricsOnly = new BarLayout { ShowCover = true, ShowText = true, ShowCaption = true, ShowProgress = true, Width = 300 };
        var compact = new BarLayout { ShowCover = true, ShowText = true, ShowCaption = true, ShowPlayPause = true, Width = 190 };
        var minimal = new BarLayout { ShowCover = true, ShowText = true, Width = 170 };

        // Taskbar players at 1x, for the real desktop captures (02).
        foreach (var key in new[] { "rain", "echo", "planes", "golden", "north" })
            Widget(songs[key], full, 1, $"bar-{key}-1x");
        // The four configurations at the strip scale (03).
        const double strip = 1.875;
        Widget(songs["rain"], Copy(full, 360), strip, "bar-full");
        Widget(songs["rain"], lyricsOnly, strip, "bar-lyrics");
        Widget(songs["rain"], compact, strip, "bar-compact", paused: true);
        Widget(songs["rain"], minimal, strip, "bar-minimal", paused: true);
        // The large taskbar player for 01 and the hero art.
        Widget(songs["rain"], Copy(full, 640), 4.0, "bar-rain-4x");
        Widget(songs["rain"], Copy(full, 420), 3.0, "bar-rain-3x");

        // Mini players (01, 04, hero).
        Mini(songs["rain"], MiniPlayerLayout.Cinema, MiniPlayerBackground.BlurredCover, LyricsTypeface.Modern, 1930, "mini-rain-cinema");
        Mini(songs["echo"], MiniPlayerLayout.Vertical, MiniPlayerBackground.Velvet, LyricsTypeface.Serif, 1000, "mini-echo-vertical");
        Mini(songs["north"], MiniPlayerLayout.Vertical, MiniPlayerBackground.Aurora, LyricsTypeface.Geometric, 1000, "mini-north-vertical");
        Mini(songs["golden"], MiniPlayerLayout.Cinema, MiniPlayerBackground.Atmosphere, LyricsTypeface.Modern, 1600, "mini-golden-cinema");

        // Lyrics Studio cards, exported by the app's own renderer (05).
        foreach (var (style, song, name) in new[] { (LyricCardStyle.Paint98, "rain", "card-paint"), (LyricCardStyle.LyricReceipt, "rain", "card-receipt"),
            (LyricCardStyle.CassetteTape, "echo", "card-cassette"), (LyricCardStyle.GameBoy, "planes", "card-gameboy"), (LyricCardStyle.MagicCard, "golden", "card-magic"),
            (LyricCardStyle.NintendoDs, "north", "card-ds") })
            Card(songs[song], style, name);
        Console.WriteLine($"Components exported to {_comp}");
    }

    static BarLayout Copy(BarLayout b, int width) => new()
    {
        ShowCover = b.ShowCover, ShowText = b.ShowText, ShowCaption = b.ShowCaption, ShowShuffle = b.ShowShuffle, ShowPrevious = b.ShowPrevious,
        ShowPlayPause = b.ShowPlayPause, ShowNext = b.ShowNext, ShowRepeat = b.ShowRepeat, ShowVolume = b.ShowVolume, ShowLike = b.ShowLike,
        ShowProgress = b.ShowProgress, Width = width,
    };

    static SettingsService Settings(BarLayout bar, MiniPlayerStyle? look = null)
    {
        var settings = new SettingsService();
        var s = settings.Current;
        s.Language = "en"; s.EnableAnimations = false; s.SpotifyLyricsEnabled = true;
        s.Background = OverlayBackground.AlbumGradient; s.TaskbarLyricFlow = LyricFlowMode.Automatic;
        s.PausedBehavior = PausedBehavior.Minimize;
        s.Bar = bar; s.MinimizedBar = bar; s.MiniPlayer = BarLayout.DefaultMiniPlayer();
        if (look is not null) s.MiniPlayerLook = look;
        return settings;
    }

    static void Widget(Song song, BarLayout bar, double scale, string name, bool paused = false)
    {
        var monitor = new SongMonitor(song) { Status = paused ? PlaybackState.Paused : PlaybackState.Playing };
        var settings = Settings(bar);
        using var sync = new LyricsSyncService(monitor, new SongLyrics(song), settings);
        sync.Reload();
        using var vm = new TaskbarViewModel(monitor, sync, settings, new AppVolumeService(), OfflineLikes(monitor, settings));
        var bitmap = RenderShown(new TaskbarOverlay(vm), bar.Width, 48, scale, Brushes.Transparent);
        Save(bitmap, Path.Combine(_comp, name + ".png"));
    }

    static void Mini(Song song, MiniPlayerLayout layout, MiniPlayerBackground background, LyricsTypeface typeface, double width, string name)
    {
        var monitor = new SongMonitor(song);
        var settings = Settings(BarLayout.DefaultNormal(), new MiniPlayerStyle
        {
            Layout = layout, Background = background, Highlight = LyricsHighlight.Expressive, Motion = LyricMotionMode.Expressive,
            Typeface = typeface, Alignment = LyricsAlignment.Center, Controls = ControlsLook.Classic,
        });
        using var sync = new LyricsSyncService(monitor, new SongLyrics(song), settings);
        sync.Reload();
        using var vm = new MiniPlayerViewModel(monitor, sync, settings, new AppVolumeService(), OfflineLikes(monitor, settings));
        var mini = RenderMini(vm, width);
        Save(mini.Bitmap, Path.Combine(_comp, name + ".png"));
        File.WriteAllText(Path.Combine(_comp, name + ".json"), $"{{\"x\":{mini.Card.X:0},\"y\":{mini.Card.Y:0},\"w\":{mini.Card.Width:0},\"h\":{mini.Card.Height:0},\"scale\":{mini.Scale:0.###}}}");
    }

    static void Card(Song song, LyricCardStyle style, string name)
    {
        var lines = song.Lines.Skip(2).Take(2).Select(l => new LyricCardLine(l.Text, TimeSpan.FromSeconds(l.At))).ToArray();
        var content = new LyricCardContent(song.Title, song.Artist, song.Album, lines, song.Cover(640), song.End, song.Start, song.End, TimeSpan.FromSeconds(song.Duration));
        File.WriteAllBytes(Path.Combine(_comp, name + ".png"), LyricCardRenderer.EncodePng(LyricCardRenderer.Render(content, new LyricCardOptions { Style = style, Format = LyricCardFormat.Story })));
    }

    // ---- Original covers ------------------------------------------------------------------------

    static BitmapSource Paint(int size, Action<DrawingContext> draw)
    {
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen()) { dc.PushTransform(new ScaleTransform(size / 300.0, size / 300.0)); draw(dc); dc.Pop(); }
        return Render(v, size, size);
    }

    static BitmapSource MoonCover(int size) => Paint(size, dc =>
    {
        dc.DrawRectangle(new LinearGradientBrush(Hex("#2A1430"), Hex("#C0607E"), 90), null, new Rect(0, 0, 300, 300));
        dc.DrawEllipse(B("#F6D6C8"), null, new Point(150, 118), 62, 62);
        dc.DrawEllipse(new LinearGradientBrush(Hex("#3A1B38"), Hex("#5E2A4C"), 90), null, new Point(178, 100), 56, 56);
        for (int i = 0; i < 5; i++)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(60 + i * 30), 0x24, 0x0E, 0x22)), null, new Rect(0, 210 + i * 18, 300, 18));
        foreach (var (x, y) in new[] { (40, 40), (250, 60), (70, 170), (230, 180), (110, 30) })
            dc.DrawEllipse(B("#FBE7DC"), null, new Point(x, y), 2.2, 2.2);
    });

    static BitmapSource SunCover(int size) => Paint(size, dc =>
    {
        dc.DrawRectangle(new LinearGradientBrush(Hex("#F2C46D"), Hex("#C4603A"), 90), null, new Rect(0, 0, 300, 300));
        dc.DrawEllipse(B("#FFF1CF"), null, new Point(150, 170), 70, 70);
        for (int i = 0; i < 6; i++)
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb((byte)(0x2C + i * 6), (byte)(0x4A + i * 8), (byte)(0x6B + i * 6))), null, new Rect(0, 182 + i * 20, 300, 12 + i * 2));
    });

    static BitmapSource PlaneCover(int size) => Paint(size, dc =>
    {
        dc.DrawRectangle(new LinearGradientBrush(Hex("#BFD9EA"), Hex("#4F8DB8"), 90), null, new Rect(0, 0, 300, 300));
        dc.DrawGeometry(B("#F7F4EC"), null, Geometry.Parse("M60,190 L240,90 L150,215 L130,170 Z"));
        dc.DrawGeometry(B("#D8D2C3"), null, Geometry.Parse("M130,170 L240,90 L150,215 Z"));
        dc.DrawGeometry(null, new Pen(B("#F7F4EC"), 3) { DashStyle = new DashStyle([3, 3], 0) }, Geometry.Parse("M60,190 C30,230 80,270 20,290"));
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), null, new Point(80, 70), 46, 18);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), null, new Point(220, 230), 60, 20);
    });

    static BitmapSource AuroraCover(int size) => Paint(size, dc =>
    {
        dc.DrawRectangle(new LinearGradientBrush(Hex("#08161E"), Hex("#123B3F"), 90), null, new Rect(0, 0, 300, 300));
        dc.DrawGeometry(new LinearGradientBrush(Color.FromArgb(200, 0x5C, 0xF2, 0xB0), Color.FromArgb(0, 0x5C, 0xF2, 0xB0), 90), null, Geometry.Parse("M0,120 C80,40 160,160 300,60 L300,170 C180,220 90,120 0,200 Z"));
        dc.DrawGeometry(new LinearGradientBrush(Color.FromArgb(150, 0x9A, 0x7B, 0xF0), Color.FromArgb(0, 0x9A, 0x7B, 0xF0), 90), null, Geometry.Parse("M0,80 C100,20 200,110 300,30 L300,100 C200,160 110,60 0,140 Z"));
        dc.DrawGeometry(B("#06110F"), null, Geometry.Parse("M0,300 L0,240 L30,250 L50,200 L70,250 L95,235 L120,180 L150,245 L180,225 L205,190 L230,240 L260,215 L300,250 L300,300 Z"));
    });
}

internal sealed class SongLyrics(Song song) : ILyricsService
{
    public Task<LyricsResult> GetLyricsAsync(TrackQuery query, CancellationToken ct) => Task.FromResult(new LyricsResult(LyricsKind.Synced,
        song.Lines.Select(l => new LyricsLine(TimeSpan.FromSeconds(l.At), l.Text)).ToArray(), null, query.Title, query.Artist));
    public Task ForgetAsync(TrackQuery query) => Task.CompletedTask;
}

internal sealed class SongMonitor(Song song) : ISpotifyMonitor
{
    private ImageSource? _cover;
    public bool HasSession => true;
    public string? SessionAppId => "Spotify.exe";
    public IReadOnlyList<string> AllSessionAppIds => ["Spotify.exe"];
    public TrackInfo? Track => new(song.Title, song.Artist, song.Album);
    public PlaybackState Status { get; set; } = PlaybackState.Playing;
    public TimeSpan Position => TimeSpan.FromSeconds(song.Position);
    public TimeSpan? Duration => TimeSpan.FromSeconds(song.Duration);
    public ImageSource? Thumbnail => _cover ??= song.Cover(640);
    public bool CanSeek => true; public bool CanGoNext => true; public bool CanGoPrevious => true;
    public bool CanShuffle => true; public bool CanRepeat => true; public bool IsShuffleActive => false;
    public RepeatMode RepeatMode => RepeatMode.None;
    public (Color Start, Color End) Palette => (song.Start, song.End);
    public event EventHandler? TrackChanged;
    public event EventHandler? PlaybackChanged;
    public event EventHandler? TimelineChanged;
    public event EventHandler? ThumbnailChanged;
    public Task PlayPauseAsync() => Task.CompletedTask; public Task NextAsync() => Task.CompletedTask;
    public Task PreviousAsync() => Task.CompletedTask; public Task SeekAsync(TimeSpan position) => Task.CompletedTask;
    public Task ToggleShuffleAsync() => Task.CompletedTask; public Task CycleRepeatAsync() => Task.CompletedTask;
    public Task StartAsync() => Task.CompletedTask;
    public void Dispose() { }
}
