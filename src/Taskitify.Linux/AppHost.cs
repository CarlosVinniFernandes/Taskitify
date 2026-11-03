using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using Taskitify.Core.Lyrics;
using Taskitify.Core.Media;
using Taskitify.Platform.Linux;
using Taskitify.Platform.Linux.Mpris;
using Taskitify.Services;

namespace Taskitify.Linux;

/// <summary>
/// Owns the player, the lyrics session and the settings, and turns their (background-thread) events
/// into one <see cref="Changed"/> notification on the UI thread for the windows to redraw from.
/// </summary>
internal sealed class AppHost : IDisposable
{
    private readonly HttpClient _lyricsHttp;
    private readonly HttpClient _coverHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly CoverLoader _covers;
    private readonly LineFile _lineFile;
    private readonly AutostartEntry _autostart;
    private readonly LocalizationCatalog _catalog = new();
    private CancellationTokenSource? _coverCts;
    private string? _coverUrl;
    private int _changePosted;
    private readonly bool _topBarExtension;

    public AppHost(Xdg xdg)
    {
        Xdg = xdg;
        _topBarExtension = GnomeExtension.IsEnabled();
        Settings = LinuxSettings.Load(xdg.SettingsFile);
        _covers = new CoverLoader(_coverHttp);
        _lineFile = new LineFile(xdg.LineFile);
        _autostart = new AutostartEntry(xdg.AutostartFile);

        var version = typeof(AppHost).Assembly.GetName().Version?.ToString(3);
        _lyricsHttp = LrclibClient.CreateHttpClient($"Taskitify/{version} (+https://github.com/CarlosVinniFernandes/Taskitify)");
        var lyrics = new LyricsService(
            [new LocalLyricsProvider(xdg.UserLyricsDirectory), new LrclibProvider(new LrclibClient(_lyricsHttp))],
            new LyricsCache(xdg.LyricsCacheDirectory));

        Media = new MprisMediaSource();
        Session = new LyricsSession(Media, lyrics) { OffsetMs = Settings.SyncOffsetMs };
        Media.TrackChanged += (_, _) => OnTrackChanged();
        Media.PlaybackChanged += (_, _) => PostChanged();
        Media.TimelineChanged += (_, _) => PostChanged();
        Session.LyricsChanged += (_, _) => PostChanged();
        Session.CurrentLineChanged += (_, _) => PostChanged();
    }

    public Xdg Xdg { get; }

    public LinuxSettings Settings { get; }

    public MprisMediaSource Media { get; }

    public LyricsSession Session { get; }

    public Cover? Cover { get; private set; }

    public IReadOnlyList<LanguageOption> Languages => _catalog.Languages;

    /// <summary>Raised on the UI thread whenever anything the windows show may have changed.</summary>
    public event Action? Changed;

    public string Language => _catalog.Normalize(Settings.Language ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    public string T(string key) => _catalog.Get(Language, key);

    public string Tf(string key, params object[] args) => _catalog.Format(Language, key, args);

    public string L(string key) => LinuxStrings.Get(Language, key);

    /// <summary>The floating bar is shown unless the top-bar extension is on (or the user chose otherwise).</summary>
    public bool FloatingBar => Settings.FloatingBar ?? !_topBarExtension;

    public bool PlayerVisible =>
        Media.HasSession && (Media.Status == MediaPlaybackStatus.Playing || !Settings.HideWhenPaused);

    /// <summary>What the lyrics bar says right now; empty means there is nothing to show.</summary>
    public string BarText()
    {
        if (!Media.HasSession || Media.Track is not { } track)
            return "";

        var title = string.IsNullOrWhiteSpace(track.Artist) ? track.Title : $"{track.Title} — {track.Artist}";
        switch (Session.State)
        {
            case LyricsSessionState.Synced:
                if (Session.CurrentLine is { } line)
                    return string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text;
                return Settings.ShowTrackWhenNoLyrics ? title : "";
            case LyricsSessionState.Loading:
                return title;
            default:
                return Settings.ShowTrackWhenNoLyrics ? title : "";
        }
    }

    public async Task StartAsync()
    {
        try
        {
            await Media.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not connect to the D-Bus session bus: {ex.Message}");
        }
        await Session.InitializeAsync().ConfigureAwait(false);
        OnTrackChanged();
    }

    public void SaveSettings()
    {
        Session.OffsetMs = Settings.SyncOffsetMs;
        try
        {
            Settings.Save(Xdg.SettingsFile);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Could not save settings: {ex.Message}");
        }
        PostChanged();
    }

    public void ApplyAutostart()
    {
        try
        {
            _autostart.Set(Settings.StartAtLogin, Environment.ProcessPath ?? "taskitify");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not update autostart: {ex.Message}");
        }
    }

    public bool AutostartEnabled => _autostart.IsEnabled;

    public void OpenLyricsFolder()
    {
        Directory.CreateDirectory(Xdg.UserLyricsDirectory);
        try
        {
            Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { Xdg.UserLyricsDirectory }, UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine($"xdg-open failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _coverCts?.Cancel();
        Session.Dispose();
        Media.Dispose();
        _lineFile.Clear();
        _lyricsHttp.Dispose();
        _coverHttp.Dispose();
    }

    private void OnTrackChanged()
    {
        PostChanged();
        var url = Media.Track?.ArtUrl;
        if (url == _coverUrl)
            return;
        _coverUrl = url;
        _coverCts?.Cancel();
        var cts = _coverCts = new CancellationTokenSource();
        _ = LoadCoverAsync(url, cts.Token);
    }

    private async Task LoadCoverAsync(string? url, CancellationToken ct)
    {
        var cover = await _covers.LoadAsync(url, ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested)
            return;
        Cover = cover;
        PostChanged();
    }

    private void PostChanged()
    {
        // Coalesce bursts (a track change raises several events).
        if (Interlocked.Exchange(ref _changePosted, 1) == 1)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            Volatile.Write(ref _changePosted, 0);
            PublishLine();
            Changed?.Invoke();
        });
    }

    private void PublishLine()
    {
        if (!Media.HasSession)
        {
            _lineFile.Clear();
            return;
        }
        var track = Media.Track;
        _lineFile.Write(BarText(), track?.Title, track?.Artist, Media.Status == MediaPlaybackStatus.Playing);
    }
}
