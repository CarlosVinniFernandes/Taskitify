using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Taskitify.Core.Lyrics;
using Taskitify.Core.Net;
using Taskitify.Infrastructure;
using Taskitify.Services;
using Taskitify.ViewModels;
using Taskitify.Views;

namespace Taskitify;

/// <summary>Composition root: builds the services, wires them and owns their lifetime.</summary>
public partial class App : Application
{
    private const string MutexName = @"Local\Taskitify.SingleInstance";
    private const string ActivateEventName = @"Local\Taskitify.Activate";

    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _activateWait;

    private SettingsService? _settings;
    private ThemeService? _theme;
    private HttpClient? _http;
    private HttpClient? _spotifyHttp;
    private SpotifyLyricsSession? _spotifyLyrics;
    private LyricsCache? _cache;
    private SpotifyMonitor? _monitor;
    private LyricsSyncService? _sync;
    private TaskbarViewModel? _taskbarVm;
    private TaskbarOverlayService? _overlay;
    private MiniPlayerViewModel? _miniPlayerVm;
    private MiniPlayerWindow? _miniPlayer;
    private MinimalLyricsViewModel? _minimalVm;
    private MinimalLyricsWindow? _minimal;
    // Minimal Mode is off but its look is being changed in the settings: shown until they close.
    private bool _minimalPreview;
    // An app chosen in the settings is in front (Minimal Mode shows); the shortcut hid it for now.
    private bool _minimalAuto;
    private bool _minimalSuppressed;
    private ForegroundWatcher? _autoWatch;
    private readonly Dictionary<uint, string> _processNames = [];
    private GlobalHotkey? _hotkey;
    private (bool, int, int)? _hotkeyApplied;
    private bool _hotkeyTaken;
    private bool _hotkeyPaused;
    private TrayIconService? _tray;
    private SettingsWindow? _settingsWindow;
    private SettingsViewModel? _settingsVm;
    private DebugWindow? _debugWindow;
    private LyricCardWindow? _lyricCardWindow;
    private SpotifyLikeService? _likes;
    private SpotifyLyricsProvider? _spotifyProvider;

    public bool IsShuttingDown { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // The overlay is a small strip that changes a few times a second: rendering it in
        // software keeps the Direct3D device and the GPU driver's heaps (~13 MB, measured with the
        // overlay alone) out of the process, at no extra CPU, scrolling text included. The mini
        // player switches to the GPU while it is open (see there).
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        _mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            // Already running: ask that instance to open its settings and leave.
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ActivateEventName);
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _activateWait = ThreadPool.RegisterWaitForSingleObject(_activateEvent,
            (_, _) => Dispatcher.InvokeAsync(ShowSettings), null, Timeout.Infinite, executeOnlyOnce: false);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        SessionEnding += (_, _) => IsShuttingDown = true;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        var background = e.Args.Contains(StartupService.BackgroundArgument, StringComparer.OrdinalIgnoreCase)
            || StartupService.IsStartupActivation();
        await StartupService.InitializeAsync();

        AppPaths.MigrateFromOldName();
        StartupService.MigrateFromOldName();

        _settings = new SettingsService();
        _settings.Load();
        if (_settings.IsFirstRun)
        {
            // The welcome comes in Windows' display language when the app has it. Saving the
            // settings now also means it is shown only once.
            var windows = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            _settings.Update(s => s.Language = LocalizationService.Languages.Any(l => l.Code == windows) ? windows : s.Language);
        }
        LocalizationService.SetLanguage(_settings.Current.Language);
        Log.WriteToFile = _settings.Current.DebugMode;
        Log.Info($"Taskitify starting (Windows {Environment.OSVersion.Version}, background={background})");

        // A new release opened from its own folder takes over the desktop shortcut and the
        // "start with Windows" entry of the previous one. Off the UI thread: the desktop may be
        // a OneDrive folder.
        var desktopShortcut = _settings.Current.DesktopShortcut;
        _ = Task.Run(() =>
        {
            StartupService.MoveEntryToThisCopy();
            DesktopShortcutService.Apply(desktopShortcut);
        });

        _theme = new ThemeService();

        // LRCLIB asks clients to identify themselves with a name and a link.
        _http = LrclibClient.CreateHttpClient($"Taskitify/{typeof(App).Assembly.GetName().Version?.ToString(3)} (+https://github.com/CarlosVinniFernandes/Taskitify)");
        _cache = new LyricsCache(AppPaths.LyricsCacheDirectory);
        // Addresses tried like a browser does: some networks drop IPv6 to Spotify's image CDN.
        _spotifyHttp = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, ConnectCallback = StaggeredConnect.ConnectAsync })
        { Timeout = TimeSpan.FromSeconds(10) };
        _spotifyLyrics = new SpotifyLyricsSession(_settings, Dispatcher);
        var spotifyOnly = e.Args.Contains("--spotify-only", StringComparer.OrdinalIgnoreCase);
        var spotifyProvider = new SpotifyLyricsProvider(_spotifyHttp, _spotifyLyrics, Log.Debug);
        // Spotify is the primary source; LRCLIB only fills in songs Spotify has no synced lyrics
        // for. The diagnostic mode skips local files and the disk cache, which may hold LRCLIB lyrics.
        var lyrics = spotifyOnly
            ? new LyricsService([spotifyProvider], cache: null)
            : new LyricsService(
                [new LocalLyricsProvider(AppPaths.UserLyricsDirectory),
                 new FallbackLyricsProvider(spotifyProvider, new LrclibProvider(new LrclibClient(_http)))], _cache);
        if (spotifyOnly) Log.Info("Spotify-only test mode: LRCLIB, local files and disk cache disabled");

        _monitor = new SpotifyMonitor(Dispatcher)
        {
            // Spotify's desktop app sometimes publishes a song to Windows without its cover: with
            // Spotify lyrics connected, the one Spotify's search shows.
            CoverFallback = (track, duration) => _settings.Current.SpotifyLyricsEnabled
                ? spotifyProvider.FindCoverAsync(new TrackQuery(track.Title, track.Artist, track.Album, duration), CancellationToken.None)
                : Task.FromResult<byte[]?>(null),
        };
        _sync = new LyricsSyncService(_monitor, lyrics, _settings);
        var spotifyStatus = _spotifyLyrics.StatusKey;
        _spotifyLyrics.StatusChanged += (_, _) =>
        {
            var was = spotifyStatus;
            spotifyStatus = _spotifyLyrics.StatusKey;
            // After signing in (not after a routine token renewal, which also ends in "ready"), look
            // the song up again; a lookup in progress gets the token anyway. "Disconnected" is also
            // the state before the first token, and connecting goes through "login".
            if (spotifyStatus == "SpotifyLyricsReady"
                && was is "SpotifyLyricsLogin" or "SpotifyLyricsDenied" or "SpotifyLyricsBrowserError"
                && _sync.State is not (Models.LyricsState.Synced or Models.LyricsState.Loading))
                _ = _sync.ForceReloadAsync();
        };
        // The web player opens when a lookup first needs it, not at startup: a cached song
        // needs no token, and the settings window checks the session when it opens.
        _monitor.TimelineChanged += (_, _) =>
        {
            if (_monitor.Status == Models.PlaybackState.Playing && _monitor.Duration is { } duration
                && duration - _monitor.Position < TimeSpan.FromSeconds(30))
                _spotifyLyrics.PrepareForNextSong();
        };

        var library = new SpotifyLibraryClient(_spotifyHttp, Log.Debug);
        _spotifyLyrics.LibraryQuerySeen = library.Learn;
        _likes = new SpotifyLikeService(_monitor, _sync, _settings, _spotifyLyrics, spotifyProvider, library, Dispatcher);
        _spotifyProvider = spotifyProvider;

        _taskbarVm = new TaskbarViewModel(_monitor, _sync, _settings, new AppVolumeService(), _likes);
        _overlay = new TaskbarOverlayService(_monitor, _sync, _settings, new TaskbarPositionService(), _taskbarVm);
        _overlay.Initialize();
        _overlay.Clicked += (_, _) => ToggleMiniPlayer();
        _overlay.ShareRequested += (_, _) => ShowLyricCard();
        _overlay.CalibrationChanged += (_, on) => _settingsVm?.SyncCalibrating(on);

        _miniPlayerVm = new MiniPlayerViewModel(_monitor, _sync, _settings, new AppVolumeService(), _likes);
        _miniPlayerVm.ShareRequested += (_, _) => ShowLyricCard();
        _miniPlayer = new MiniPlayerWindow(_miniPlayerVm, () => _settings.Current.EnableAnimations,
            () => _settings.Current.Background == Models.OverlayBackground.AlbumGradient);
        // The popup's hover buttons.
        _miniPlayer.LyricsSizeRequested += (_, step) => _settings.Update(s =>
            s.MiniPlayerLook.LyricsScale = Math.Clamp(Math.Round((s.MiniPlayerLook.LyricsScale + (step * 0.1)) * 10) / 10, 0.7, 2.2));
        _miniPlayer.FloatRequested += (_, _) => _settings.Update(s => (s.Minimal.Enabled, s.Minimal.NotThatMinimal) = (true, true));
        _miniPlayer.MinimalRequested += (_, _) =>
        {
            _miniPlayer.HidePopup(force: true);
            _settings.Update(s => (s.Minimal.Enabled, s.Minimal.NotThatMinimal) = (true, false));
        };

        _minimalVm = new MinimalLyricsViewModel(_monitor, _sync, _settings);
        // The floating lyrics' first button: the whole player instead (Not that minimal).
        _minimalVm.MiniPlayerRequested += (_, _) => _settings.Update(s => (s.Minimal.Enabled, s.Minimal.NotThatMinimal) = (true, true));
        _minimal = new MinimalLyricsWindow(_minimalVm, _settings, _monitor);
        _hotkey = new GlobalHotkey(OnMinimalHotkey);
        _overlay.MinimalActive = () => MinimalShown;

        _tray = new TrayIconService(ShowSettings, ShowMiniPlayerFromTray, ShowLyricCard, ShowDebug, Shutdown, ShowTour)
        {
            CanMakeLyricCard = () => _monitor?.Track is not null,
            Minimal = () => (_settings.Current.Minimal.Enabled, _settings.Current.Minimal.ClickThrough),
            ToggleMinimal = () => _settings.Update(s => s.Minimal.Enabled = !s.Minimal.Enabled),
            ToggleMinimalClicks = () => _settings.Update(s => s.Minimal.ClickThrough = !s.Minimal.ClickThrough),
        };
        ApplySettings();
        _settings.Changed += (_, _) => ApplySettings();

        _monitor.TrackChanged += (_, _) =>
        {
            if (_monitor.Track is null)
                _miniPlayer?.HidePopup();
        };

        try
        {
            await _monitor.StartAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Could not access Windows media sessions", ex);
            MessageBox.Show(LocalizationService.Get("MediaError") + "\n\n" + ex.Message,
                "Taskitify", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        _settings.Current.FirstUseDay ??= DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (_monitor is not null)
            _monitor.PlaybackChanged += (_, _) => RecordListening();
        // The tour opens once: on a new installation, and once for users updating from a version without it.
        if (e.Args.Contains("--tour", StringComparer.OrdinalIgnoreCase) || _settings.Current.TourVersion < TourWindow.Version)
            ShowTour();
        else if (!background && _tray.Visible)
            _tray.ShowBalloon("Taskitify", LocalizationService.Get("BackgroundNotice"));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsShuttingDown = true;
        _settings?.Flush();
        _tray?.Dispose();
        _hotkey?.Dispose();
        _autoWatch?.Dispose();
        _minimal?.HideLyrics();
        _settings?.Flush();
        _minimalVm?.Dispose();
        _overlay?.Dispose();
        _miniPlayerVm?.Dispose();
        _taskbarVm?.Dispose();
        _likes?.Dispose();
        _sync?.Dispose();
        _monitor?.Dispose();
        _theme?.Dispose();
        _http?.Dispose();
        _spotifyLyrics?.Dispose();
        _spotifyHttp?.Dispose();
        _activateWait?.Unregister(null);
        _activateEvent?.Dispose();
        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
        Log.Info("Exit");
        base.OnExit(e);
    }

    public void ShowSettings() => ShowSettings(null);

    public void ShowSettings(SettingsPage? page)
    {
        if (_settings is null || _cache is null || _sync is null || _overlay is null || _monitor is null || _theme is null)
            return;
        if (_settingsWindow is { IsLoaded: true })
        {
            if (page is { } p)
                _settingsWindow.ShowPage(p);
            BringToFront(_settingsWindow);
            return;
        }
        _spotifyLyrics?.CheckSession();
        _settingsVm = new SettingsViewModel(_settings, _cache, _sync, _overlay.IsCalibrating, _overlay.SetCalibrating)
        {
            MiniLook = new MiniPlayerLookViewModel(_settings, _monitor, _sync, _theme),
            MinimalLook = new MinimalLookViewModel(_settings, _monitor, _theme),
        };
        // Customizing the mini player shows it, live, beside the settings.
        _settingsVm.MiniLook!.PreviewRequested += (_, _) => PreviewMiniPlayer();
        _settingsVm.MinimalLook!.PreviewRequested += (_, _) =>
        {
            _minimalPreview = true;
            UpdateMinimal();
        };
        _settingsVm.MinimalLook.ResetPlacementRequested += (_, _) => _minimal?.ResetPlacement();
        _settingsVm.MinimalLook.PlaceRequested += (_, spot) =>
        {
            if (_miniPlayer is { IsPinned: true })
                _miniPlayer.PlaceAt(spot);
            else
                _minimal?.PlaceAt(spot);
        };
        _settingsVm.MinimalLook.HotkeyTaken = () => _hotkeyTaken;
        _settingsVm.MinimalLook.CapturingChanged += (_, _) =>
        {
            // The shortcut being typed must reach the settings, not toggle the lyrics.
            _hotkeyPaused = _settingsVm?.MinimalLook?.IsCapturing == true;
            ApplyHotkey();
        };
        _settingsWindow = new SettingsWindow(_settingsVm, _spotifyLyrics!);
        _settingsVm.SetCurrentTrack(_monitor.Track);
        EventHandler followTrack = (_, _) => _settingsVm?.SetCurrentTrack(_monitor.Track);
        _monitor.TrackChanged += followTrack;
        _settingsWindow.Closed += (_, _) => _monitor.TrackChanged -= followTrack;
        if (page is { } first)
            _settingsWindow.ShowPage(first);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _settingsVm = null;
            _miniPlayer?.EndPreview();
            _minimalPreview = false;
            UpdateMinimal();
            _settings.Flush();
        };
        WindowInterop.SetDarkTitleBar(_settingsWindow, !_theme!.AppsUseLightTheme);
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>
    /// An open window asked for again (tray, second launch, a button): restored if minimized and
    /// brought to the front with the focus, even when another app has it.
    /// </summary>
    private static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
        // Activate alone may only flash the taskbar button when another app has the focus.
        var hwnd = WindowInterop.EnsureHandle(window);
        if (NativeMethods.GetForegroundWindow() != hwnd)
            FocusHelper.ForceForeground(hwnd);
        window.Focus();
    }

    private TourWindow? _tour;

    /// <summary>The welcome tour (for now opened from the tray menu or with --tour).</summary>
    public void ShowTour()
    {
        if (_tour is not null)
        {
            _tour.Activate();
            return;
        }
        _miniPlayer?.HidePopup();
        _tour = new TourWindow(page => { ShowSettings(page); return true; }, _settings!);
        _tour.Closed += (_, _) =>
        {
            _tour = null;
            if (_settings.Current.TourVersion < TourWindow.Version)
            {
                _settings.Update(s => s.TourVersion = TourWindow.Version);
                _settings.Flush(); // seen, even if the app is closed right away
            }
        };
        WindowInterop.SetDarkTitleBar(_tour, true);
        _tour.Show();
        _tour.Activate();
    }

    /// <summary>Counts the days music played (local only), then perhaps invites a rating once.</summary>
    private void RecordListening()
    {
        if (_monitor?.Status != Models.PlaybackState.Playing || _settings is null)
            return;
        var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (_settings.Current.LastListeningDay == today)
            return;
        _settings.Update(s => { s.LastListeningDay = today; s.ListeningDays++; });
        InviteRatingOnce();
    }

    /// <summary>
    /// After two weeks and seven days with music, one quiet notification offers the Store's review page.
    /// Only for the Store package, never during the first run or the tour, never again once shown.
    /// </summary>
    private void InviteRatingOnce()
    {
        var s = _settings!.Current;
        if (s.RatingInviteShown || s.ListeningDays < 7 || _tour is not null || _tray?.Visible != true
            || !DateTime.TryParse(s.FirstUseDay, CultureInfo.InvariantCulture, DateTimeStyles.None, out var first) || (DateTime.Today - first).TotalDays < 14)
            return;
        string family;
        try { family = global::Windows.ApplicationModel.Package.Current.Id.FamilyName; }
        catch (InvalidOperationException) { return; } // not installed from the Store package
        _settings.Update(x => x.RatingInviteShown = true);
        _tray.ShowBalloon(LocalizationService.Get("RatingTitle"), LocalizationService.Get("RatingText"), () =>
        {
            try { Process.Start(new ProcessStartInfo($"ms-windows-store://review/?PFN={family}") { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception ex) { Log.Error("Could not open the Store review page", ex); }
        });
    }

    /// <summary>First run: LRCLIB alone or Spotify + LRCLIB, and what the Spotify connection does.</summary>
    private void ShowWelcome()
    {
        var welcome = new WelcomeWindow(_spotifyLyrics!);
        WindowInterop.SetDarkTitleBar(welcome, !_theme!.AppsUseLightTheme);
        welcome.Show();
        welcome.Activate();
    }

    /// <summary>The lyric card maker for the song playing (one window; reopening brings it back with the new song).</summary>
    private void ShowLyricCard()
    {
        if (_monitor?.Track is not { } track || _sync is null || _theme is null)
            return;
        _miniPlayer?.HidePopup();
        var lines = new List<LyricCardLine>();
        var current = 0;
        if (_sync.Timeline is { } timeline)
        {
            for (var i = 0; i < timeline.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(timeline.Lines[i].Text))
                    continue;
                if (i <= _sync.CurrentIndex)
                    current = lines.Count;
                lines.Add(new LyricCardLine(timeline.Lines[i].Text.Trim(), timeline.Lines[i].Time));
            }
        }
        else if (_sync.Result?.PlainText is { } plain)
        {
            lines.AddRange(plain.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Select(l => new LyricCardLine(l, null)));
        }

        BitmapSource? cover = null;
        if (_monitor.CoverData is { } data)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = new System.IO.MemoryStream(data);
                image.EndInit();
                image.Freeze();
                cover = image;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                cover = _monitor.Thumbnail as BitmapSource;
            }
        }
        var query = new TrackQuery(track.Title, track.Artist, track.Album, _monitor.Duration);
        var provider = _spotifyProvider;
        var source = new LyricCardSource(track.Title, track.Artist, string.IsNullOrWhiteSpace(track.Album) ? null : track.Album, lines, current, cover,
            _monitor.Palette.Start, _monitor.Palette.End, _monitor.Duration, LyricsCredit.For(_sync.Result),
            provider is null ? null : () => provider.FindLargeCoverAsync(query, CancellationToken.None),
            provider?.KnownTrackId(query));

        _lyricCardWindow?.Close();
        _lyricCardWindow = new LyricCardWindow(new LyricCardViewModel(source, _settings!));
        _lyricCardWindow.Closed += (_, _) => _lyricCardWindow = null;
        WindowInterop.SetDarkTitleBar(_lyricCardWindow, !_theme.AppsUseLightTheme);
        _lyricCardWindow.Show();
        _lyricCardWindow.Activate();
    }

    private void ShowDebug()
    {
        if (_monitor is null || _sync is null || _overlay is null)
            return;
        if (_debugWindow is { IsLoaded: true })
        {
            _debugWindow.Activate();
            return;
        }
        _debugWindow = new DebugWindow(_monitor, _sync, _overlay);
        _debugWindow.Closed += (_, _) => _debugWindow = null;
        WindowInterop.SetDarkTitleBar(_debugWindow, !_theme!.AppsUseLightTheme);
        _debugWindow.Show();
    }

    private void ToggleMiniPlayer()
    {
        if (_miniPlayer is null || _overlay is null)
            return;
        if (_miniPlayer.IsVisible)
        {
            _miniPlayer.HidePopup();
            return;
        }
        // The click that deactivated (and so closed) the popup must not immediately re-open it.
        if (DateTime.UtcNow - _miniPlayer.LastHidden < TimeSpan.FromMilliseconds(300))
            return;
        OpenMiniPlayer(_overlay.Bounds);
    }

    private void ShowMiniPlayerFromTray()
    {
        if (_overlay is null)
            return;
        if (_overlay.IsShown)
        {
            OpenMiniPlayer(_overlay.Bounds);
            return;
        }
        // Overlay hidden (paused): anchor to where it would be, or to the taskbar's right edge.
        var p = _overlay.Placement;
        var anchor = p is null ? default : TaskbarPositionService.ComputeOverlayRect(p, _settings!.Current);
        OpenMiniPlayer(anchor);
    }

    /// <summary>Show the mini player without taking focus after an appearance change.</summary>
    private void PreviewMiniPlayer()
    {
        if (_miniPlayer is null || _overlay is null)
            return;
        // After the change is applied and the player has its new size.
        Dispatcher.InvokeAsync(() =>
        {
            if (_settingsWindow is not { IsVisible: true })
                return;
            var anchor = _overlay.IsShown ? _overlay.Bounds
                : _overlay.Placement is { } p ? TaskbarPositionService.ComputeOverlayRect(p, _settings!.Current) : default;
            OpenMiniPlayer(anchor, preview: true);
        }, DispatcherPriority.Loaded);
    }

    private void OpenMiniPlayer(Core.Layout.PixelRect anchor, bool preview = false)
    {
        if (_miniPlayer is null || _overlay is null)
            return;
        var monitor = anchor.IsEmpty ? null : WindowInterop.MonitorForRect(anchor);
        monitor ??= WindowInterop.MonitorForWindow(_overlay.Handle);
        if (monitor is null)
            return;
        if (anchor.IsEmpty)
            anchor = new Core.Layout.PixelRect(monitor.WorkArea.Right - 200, monitor.WorkArea.Bottom, 200, 0);
        var scale = _overlay.Placement?.Scale ?? WindowInterop.GetScale(_overlay.Handle);
        _miniPlayer.ShowAt(anchor, monitor.WorkArea, scale, preview);
    }

    private void ApplySettings()
    {
        if (_settings is null || _tray is null)
            return;
        Log.WriteToFile = _settings.Current.DebugMode;
        _tray.Visible = _settings.Current.ShowTrayIcon;
        _tray.DebugEnabled = _settings.Current.DebugMode;
        ApplyHotkey();
        UpdateAutoWatch();
        UpdateMinimal();
    }

    /// <summary>Minimal Mode shows: switched on, or an app chosen for it is in front (unless the shortcut hid it).</summary>
    private bool MinimalShown => _settings?.Current.Minimal.Enabled == true || (_minimalAuto && !_minimalSuppressed);

    /// <summary>The shortcut, from anywhere (games too): hides what shows, shows what is hidden.</summary>
    private void OnMinimalHotkey()
    {
        if (_settings is null)
            return;
        if (MinimalShown)
        {
            if (_settings.Current.Minimal.Enabled)
                _settings.Update(s => s.Minimal.Enabled = false);
            if (_minimalAuto)
                _minimalSuppressed = true;
        }
        else if (_minimalAuto)
            _minimalSuppressed = false;
        else
            _settings.Update(s => s.Minimal.Enabled = true);
        UpdateMinimal();
        _overlay?.Reevaluate();
    }

    private void ApplyHotkey()
    {
        if (_hotkey is null || _settings is null)
            return;
        var m = _settings.Current.Minimal;
        var wanted = (m.HotkeyEnabled && !_hotkeyPaused, m.HotkeyModifiers, m.HotkeyKey);
        if (_hotkeyApplied == wanted)
            return;
        _hotkeyApplied = wanted;
        if (wanted.Item1)
        {
            _hotkeyTaken = !_hotkey.Register(m.HotkeyModifiers, m.HotkeyKey);
            if (_hotkeyTaken)
                Log.Warn("Minimal Mode shortcut is taken by another app");
        }
        else
        {
            _hotkey.Unregister();
            _hotkeyTaken = false;
        }
    }

    /// <summary>The foreground is watched (an event, no polling) only while some app is chosen.</summary>
    private void UpdateAutoWatch()
    {
        if (_settings is null)
            return;
        if (_settings.Current.Minimal.AutoApps.Count > 0)
        {
            _autoWatch ??= new ForegroundWatcher(OnForeground);
            OnForeground(NativeMethods.GetForegroundWindow());
        }
        else if (_autoWatch is not null)
        {
            _autoWatch.Dispose();
            _autoWatch = null;
            SetMinimalAuto(false);
        }
    }

    private void OnForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || _settings is null)
            return;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        // Taskitify itself and the taskbar never change it: clicking the lyrics' tray icon mid-game must not.
        if (pid == Environment.ProcessId || WindowInterop.GetClassName(hwnd) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return;
        if (!_processNames.TryGetValue(pid, out var name))
        {
            try
            {
                using var process = Process.GetProcessById((int)pid);
                name = process.ProcessName;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                name = "";
            }
            if (_processNames.Count > 256)
                _processNames.Clear();
            _processNames[pid] = name;
        }
        SetMinimalAuto(_settings.Current.Minimal.AutoApps.Contains(name, StringComparer.OrdinalIgnoreCase));
    }

    private void SetMinimalAuto(bool on)
    {
        if (on == _minimalAuto)
            return;
        _minimalAuto = on;
        if (!on)
            _minimalSuppressed = false;
        Log.Info($"Minimal Mode {(on ? "brought up" : "put away")} by the app in front");
        UpdateMinimal();
        _overlay?.Reevaluate();
    }

    /// <summary>Minimal Mode's window follows its switch (and the settings' live preview).</summary>
    private void UpdateMinimal()
    {
        // Settings saved while closing (the windows' last places) must not show them again.
        if (_minimal is null || _settings is null || IsShuttingDown)
            return;
        var on = MinimalShown;
        // Not that minimal: the mini player floats instead of the lyrics alone.
        if (on && _settings.Current.Minimal.NotThatMinimal && _miniPlayer is not null)
        {
            _minimal.HideLyrics();
            var m = _settings.Current.Minimal;
            _miniPlayer.ShowPinned(new Core.Layout.PixelRect(m.PlayerX, m.PlayerY, m.PlayerWidth, m.PlayerWidth > 0 ? 1 : 0),
                new PinnedOptions(m.ClickThrough, m.Locked, m.Topmost, m.ShowClickThroughMark, m.AutoFit, m.ControlsOnHover, m.PlayerButtonsOnHover,
                    m.PlayerWindowWidth, m.PlayerLyricsHeight, m.PlayerZoom,
                    on => _settings.Update(s => s.Minimal.ClickThrough = on),
                    (r, width, lyrics, zoom) => _settings.Update(s => (s.Minimal.PlayerX, s.Minimal.PlayerY, s.Minimal.PlayerWidth, s.Minimal.PlayerWindowWidth, s.Minimal.PlayerLyricsHeight, s.Minimal.PlayerZoom)
                        = (r.X, r.Y, r.Width, width, lyrics, zoom)),
                    locked => _settings.Update(s => s.Minimal.Locked = locked),
                    () => _settings.Update(s => s.Minimal.Enabled = false),
                    () => _settings.Update(s => (s.Minimal.Enabled, s.Minimal.NotThatMinimal) = (true, false)),
                    new PlayerTools(m.PlayerShowSwitchButton, m.PlayerShowSizeButtons, m.PlayerShowFillButton, m.PlayerShowFullScreenButton, m.PlayerShowSettingsButton,
                        m.PlayerShowClickThroughButton, m.PlayerShowLockButton, m.PlayerShowCloseButton, Models.PlayerToolIds.Normalize(m.PlayerToolOrder))));
            return;
        }
        _miniPlayer?.Unpin();
        if (on || _minimalPreview)
            _minimal.ShowLyrics(preview: !on);
        else
            _minimal.HideLyrics();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A background widget must not die on a transient error; log and keep running.
        Log.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
    }
}
