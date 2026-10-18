using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Taskitify.Core.Lyrics;
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
    private TrayIconService? _tray;
    private SettingsWindow? _settingsWindow;
    private SettingsViewModel? _settingsVm;
    private DebugWindow? _debugWindow;

    public bool IsShuttingDown { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
        LocalizationService.SetLanguage(_settings.Current.Language);
        Log.WriteToFile = _settings.Current.DebugMode;
        Log.Info($"Taskitify starting (Windows {Environment.OSVersion.Version}, background={background})");

        _theme = new ThemeService();

        // LRCLIB asks clients to identify themselves with a name and a link.
        _http = LrclibClient.CreateHttpClient($"Taskitify/{typeof(App).Assembly.GetName().Version?.ToString(3)} (+https://github.com/CarlosVinniFernandes/Taskitify)");
        _cache = new LyricsCache(AppPaths.LyricsCacheDirectory);
        _spotifyHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(10) };
        _spotifyLyrics = new SpotifyLyricsSession(_settings, Dispatcher);
        var spotifyOnly = e.Args.Contains("--spotify-only", StringComparer.OrdinalIgnoreCase);
        var spotifyProvider = new SpotifyLyricsProvider(_spotifyHttp, _spotifyLyrics);
        // Spotify is the primary source; LRCLIB only fills in songs Spotify has no synced lyrics
        // for. The diagnostic mode skips local files and the disk cache, which may hold LRCLIB lyrics.
        var lyrics = spotifyOnly
            ? new LyricsService([spotifyProvider], cache: null)
            : new LyricsService(
                [new LocalLyricsProvider(AppPaths.UserLyricsDirectory),
                 new FallbackLyricsProvider(spotifyProvider, new LrclibProvider(new LrclibClient(_http)))], _cache);
        if (spotifyOnly) Log.Info("Spotify-only test mode: LRCLIB, local files and disk cache disabled");

        _monitor = new SpotifyMonitor(Dispatcher);
        _sync = new LyricsSyncService(_monitor, lyrics, _settings);
        _spotifyLyrics.StatusChanged += (_, _) =>
        {
            if (_spotifyLyrics.StatusKey == "SpotifyLyricsReady" && _sync.State != Models.LyricsState.Synced)
                _ = _sync.ForceReloadAsync();
        };
        // Restore an enabled web session even when the current song is already cached.
        _ = _spotifyLyrics.GetCredentialsAsync(CancellationToken.None);

        _taskbarVm = new TaskbarViewModel(_monitor, _sync, _settings, new AppVolumeService());
        _overlay = new TaskbarOverlayService(_monitor, _sync, _settings, new TaskbarPositionService(), _taskbarVm);
        _overlay.Initialize();
        _overlay.Clicked += (_, _) => ToggleMiniPlayer();
        _overlay.CalibrationChanged += (_, on) => _settingsVm?.SyncCalibrating(on);

        _miniPlayerVm = new MiniPlayerViewModel(_monitor, _sync);
        _miniPlayer = new MiniPlayerWindow(_miniPlayerVm, () => _settings.Current.EnableAnimations,
            () => _settings.Current.Background == Models.OverlayBackground.AlbumGradient);

        _tray = new TrayIconService(ShowSettings, ShowMiniPlayerFromTray, ShowDebug, Shutdown);
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

        if (_settings.IsFirstRun)
            ShowSettings();
        else if (!background && _tray.Visible)
            _tray.ShowBalloon("Taskitify", LocalizationService.Get("BackgroundNotice"));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsShuttingDown = true;
        _settings?.Flush();
        _tray?.Dispose();
        _overlay?.Dispose();
        _miniPlayerVm?.Dispose();
        _taskbarVm?.Dispose();
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

    public void ShowSettings()
    {
        if (_settings is null || _cache is null || _sync is null || _overlay is null)
            return;
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsVm = new SettingsViewModel(_settings, _cache, _sync, _overlay.IsCalibrating, _overlay.SetCalibrating);
        _settingsWindow = new SettingsWindow(_settingsVm, _spotifyLyrics!);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _settingsVm = null;
            _settings.Flush();
        };
        WindowInterop.SetDarkTitleBar(_settingsWindow, !_theme!.AppsUseLightTheme);
        _settingsWindow.Show();
        _settingsWindow.Activate();
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

    private void OpenMiniPlayer(Core.Layout.PixelRect anchor)
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
        _miniPlayer.ShowAt(anchor, monitor.WorkArea, scale);
    }

    private void ApplySettings()
    {
        if (_settings is null || _tray is null)
            return;
        Log.WriteToFile = _settings.Current.DebugMode;
        _tray.Visible = _settings.Current.ShowTrayIcon;
        _tray.DebugEnabled = _settings.Current.DebugMode;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A background widget must not die on a transient error; log and keep running.
        Log.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
    }
}
