using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Taskitify.Core.Layout;
using Taskitify.Infrastructure;
using Taskitify.Models;
using Taskitify.ViewModels;
using Taskitify.Views;

namespace Taskitify.Services;

/// <summary>
/// Owns the overlay window: decides when it is visible, keeps it exactly over the weather
/// widget and above the taskbar in z-order, and matches its colour to the taskbar.
///
/// The taskbar is itself a topmost window, so when it is activated it can be raised above us.
/// We re-assert HWND_TOPMOST whenever the foreground window changes (out-of-context WinEvent
/// hook, no injection) and every few seconds while visible.
/// </summary>
public sealed class TaskbarOverlayService : IDisposable
{
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);

    // UI Automation is a cross-process walk of the taskbar tree and dominates our CPU use;
    // the widget rarely moves, so re-detect every 5th tick (15 s) plus on explicit events.
    private const int TicksPerDetection = 5;

    private readonly ISpotifyMonitor _monitor;
    private readonly LyricsSyncService _sync;
    private readonly SettingsService _settings;
    private readonly TaskbarPositionService _position;
    private readonly TaskbarViewModel _vm;
    private readonly TaskbarOverlay _window;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _autoHideTimer;
    private readonly uint _taskbarCreatedMessage;

    private IntPtr _hwnd;
    private HwndSource? _source;
    private ForegroundWatcher? _foreground;
    private TaskbarPlacement? _placement;
    private PixelRect _bounds;
    private bool _refreshing;
    private bool _refreshAgain;
    private bool _fullscreen;
    private bool _shellFullscreen;
    private uint _appBarMessage;
    private readonly DispatcherTimer _fullscreenTimer;
    private bool _taskbarShown = true;
    private bool _isShown;
    private DateTime _lastRefreshAttempt;
    private int _ticks;
    private string? _pendingHideReason;
    private readonly DispatcherTimer _menuEsc = new() { Interval = TimeSpan.FromMilliseconds(60) };

    public TaskbarOverlayService(
        ISpotifyMonitor monitor,
        LyricsSyncService sync,
        SettingsService settings,
        TaskbarPositionService position,
        TaskbarViewModel vm)
    {
        _monitor = monitor;
        _sync = sync;
        _settings = settings;
        _position = position;
        _vm = vm;
        _window = new TaskbarOverlay(vm);
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = RefreshInterval };
        _refreshTimer.Tick += (_, _) => OnRefreshTick();
        _hideTimer = new DispatcherTimer { Interval = HideDelay };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (HideReason() is not null)
                HideNow();
        };
        _autoHideTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _autoHideTimer.Tick += (_, _) => CheckTaskbarShown();
        // While hidden for a fullscreen app, re-check now and then in case no event arrives on exit.
        _fullscreenTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _fullscreenTimer.Tick += (_, _) => UpdateFullscreen("poll");
    }

    /// <summary>The overlay was clicked; the mini player toggles.</summary>
    public event EventHandler? Clicked;

    public event EventHandler<bool>? CalibrationChanged;

    public bool IsCalibrating => _vm.IsCalibrating;

    public bool IsShown => _isShown;

    public IntPtr Handle => _hwnd;

    /// <summary>Current overlay bounds in physical pixels (for anchoring the mini player).</summary>
    public PixelRect Bounds => _bounds;

    public TaskbarPlacement? Placement => _placement;

    public void Initialize()
    {
        _hwnd = WindowInterop.EnsureHandle(_window);
        WindowInterop.MakeToolWindow(_hwnd, noActivate: true);
        WindowInterop.DisableRoundedCorners(_hwnd); // square, like the taskbar strip it covers
        WindowInterop.ExcludeFromPeek(_hwnd);
        _source = HwndSource.FromHwnd(_hwnd);
        _source.AddHook(WndProc);
        RegisterAppBar();

        _window.Clicked += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
        _window.CalibrationBoundsChanged += (_, _) => SaveCalibratedBounds(WindowInterop.GetBounds(_hwnd));
        _window.CalibrationResize += (_, step) => ResizeCalibrated(step);
        _window.ContextMenu = BuildContextMenu();

        _monitor.TrackChanged += OnStateChanged;
        _monitor.PlaybackChanged += OnStateChanged;
        _sync.LyricsChanged += OnStateChanged;
        _settings.Changed += OnSettingsChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _foreground = new ForegroundWatcher(OnForegroundChanged);

        RequestRefresh(); // warm up the placement so the first show is instant
        Evaluate();
    }

    public void SetCalibrating(bool calibrating)
    {
        if (_vm.IsCalibrating == calibrating)
            return;
        _vm.IsCalibrating = calibrating;
        // Dragging works best when the window may activate; restored afterwards.
        SetNoActivate(!calibrating);
        Log.Info($"Calibration {(calibrating ? "on" : "off")}");
        CalibrationChanged?.Invoke(this, calibrating);
        Evaluate();
        RequestRefresh();
    }

    public void Dispose()
    {
        _monitor.TrackChanged -= OnStateChanged;
        _monitor.PlaybackChanged -= OnStateChanged;
        _sync.LyricsChanged -= OnStateChanged;
        _settings.Changed -= OnSettingsChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _foreground?.Dispose();
        WindowInterop.UnregisterAppBar(_hwnd);
        _fullscreenTimer.Stop();
        _refreshTimer.Stop();
        _hideTimer.Stop();
        _autoHideTimer.Stop();
        _source?.RemoveHook(WndProc);
        _window.Close();
    }

    private void SetNoActivate(bool value)
    {
        var ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        ex = value ? ex | NativeMethods.WS_EX_NOACTIVATE : ex & ~NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex);
    }

    private System.Windows.Controls.ContextMenu BuildContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();
        var calibrate = new System.Windows.Controls.MenuItem { Header = "Calibrar posição" };
        calibrate.Click += (_, _) => SetCalibrating(!IsCalibrating);
        var settings = new System.Windows.Controls.MenuItem { Header = "Configurações…" };
        settings.Click += (_, _) => ((App)Application.Current).ShowSettings();
        var exit = new System.Windows.Controls.MenuItem { Header = "Sair" };
        exit.Click += (_, _) => Application.Current.Shutdown();
        // Off: the caption row disappears and the lyric may use both lines.
        var showCaption = new System.Windows.Controls.MenuItem { Header = "Mostrar música e artista", IsCheckable = true };
        showCaption.Click += (_, _) => _settings.Update(s => s.Bar.ShowCaption = showCaption.IsChecked);
        // The overlay never takes focus, so WPF cannot tell when the user clicks elsewhere;
        // a low-level mouse hook (only while the menu is open) closes it on an outside click.
        var outside = new OutsideClickWatcher((x, y) =>
        {
            if (PresentationSource.FromVisual(menu) is HwndSource popup)
            {
                var r = WindowInterop.GetBounds(popup.Handle);
                if (x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom)
                    return;
            }
            menu.Dispatcher.BeginInvoke(() => menu.IsOpen = false);
        });
        menu.Opened += (_, _) =>
        {
            calibrate.Header = IsCalibrating ? "Concluir calibração" : "Calibrar posição";
            showCaption.IsChecked = _settings.Current.Bar.ShowCaption;
            outside.Start();
            NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE); // clear a stale "pressed since" bit
            _menuEsc.Start();
        };
        menu.Closed += (_, _) =>
        {
            outside.Stop();
            _menuEsc.Stop();
        };
        _menuEsc.Tick += (_, _) =>
        {
            // Esc without keyboard focus: poll the key state while the menu is open.
            // 0x8000 = down now, 0x0001 = pressed since the last poll (catches quick taps).
            if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8001) != 0)
                menu.IsOpen = false;
        };
        // Bypasses the cache (e.g. a "not found" saved earlier) for the song playing now.
        var refetch = new System.Windows.Controls.MenuItem { Header = "Buscar letra de novo" };
        refetch.Click += (_, _) => _ = _sync.ForceReloadAsync();
        menu.Items.Add(refetch);
        menu.Items.Add(showCaption);
        menu.Items.Add(calibrate);
        menu.Items.Add(settings);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(exit);
        return menu;
    }

    private void OnStateChanged(object? sender, EventArgs e) => Evaluate();

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        Evaluate();
        RequestRefresh();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        _window.Dispatcher.InvokeAsync(() =>
        {
            Log.Info("Display settings changed");
            _placement = null;
            RequestRefresh();
        });

    private void OnForegroundChanged(IntPtr hwnd)
    {
        UpdateFullscreen("foreground");
        if (_isShown)
            WindowInterop.BringToTopmost(_hwnd);
    }

    /// <summary>
    /// Registered as a space-less app bar so the shell tells us (ABN_FULLSCREENAPP) when a
    /// fullscreen app opens or closes, including when the same window goes fullscreen (F11,
    /// video players), which no foreground change would reveal.
    /// </summary>
    private void RegisterAppBar()
    {
        if (_appBarMessage == 0)
            _appBarMessage = NativeMethods.RegisterWindowMessage("Taskitify.AppBar");
        if (!WindowInterop.RegisterAppBar(_hwnd, _appBarMessage))
            Log.Warn("Could not register for fullscreen notifications");
    }

    private void UpdateFullscreen(string trigger)
    {
        var fullscreen = _placement is { } p && WindowInterop.IsFullscreenOn(p.Monitor, _shellFullscreen);
        _fullscreenTimer.IsEnabled = fullscreen;
        if (fullscreen == _fullscreen)
            return;
        _fullscreen = fullscreen;
        Log.Info($"Fullscreen app on our monitor: {fullscreen} ({trigger})");
        Evaluate();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _appBarMessage && _appBarMessage != 0 && wParam.ToInt32() == NativeMethods.ABN_FULLSCREENAPP)
        {
            _shellFullscreen = lParam != IntPtr.Zero;
            UpdateFullscreen("shell");
        }
        else if (msg == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            // Explorer restarted: the taskbar (and its UIA tree) is rebuilt over the next moments.
            Log.Info("Explorer restarted (TaskbarCreated)");
            _placement = null;
            _shellFullscreen = false;
            RegisterAppBar(); // app bars are forgotten when Explorer restarts
            _window.Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(1500);
                RequestRefresh();
            });
        }
        else if (msg == NativeMethods.WM_MOUSEACTIVATE && !IsCalibrating)
        {
            handled = true;
            return new IntPtr(NativeMethods.MA_NOACTIVATE);
        }
        else if (msg == NativeMethods.WM_DPICHANGED)
        {
            // WPF resizes to its suggestion; put our own physical bounds back afterwards.
            _window.Dispatcher.InvokeAsync(ApplyBounds, DispatcherPriority.Background);
        }
        else if (msg == NativeMethods.WM_DISPLAYCHANGE)
        {
            _placement = null;
            RequestRefresh();
        }
        return IntPtr.Zero;
    }

    /// <summary>Decide visibility from playback, lyrics, settings and screen state.</summary>
    private void Evaluate()
    {
        var hideReason = HideReason();
        if (hideReason is null)
        {
            _hideTimer.Stop();
            ShowNow();
        }
        else if (_isShown && !_hideTimer.IsEnabled)
        {
            // Short grace period: Spotify reports Paused/Changing for an instant when skipping.
            _pendingHideReason = hideReason;
            _hideTimer.Start();
        }
    }

    /// <summary>Why the overlay should be hidden right now, or null if it should show.</summary>
    private string? HideReason()
    {
        if (IsCalibrating)
            return null;
        if (_monitor.Track is null)
            return "no track";
        if (_monitor.Status != PlaybackState.Playing && !_vm.IsCompact)
            return $"playback {_monitor.Status}";
        if (!_vm.IsCompact && _settings.Current.NoLyricsBehavior == NoLyricsBehavior.HideOverlay && _sync.State != LyricsState.Synced)
            return $"no synced lyrics ({_sync.State})";
        if (_fullscreen)
            return "fullscreen app";
        if (!_taskbarShown)
            return "taskbar hidden";
        return null;
    }

    private void ShowNow()
    {
        if (_placement is null)
        {
            // Will call ShowNow again via Evaluate once placed. Throttled: if the taskbar cannot
            // be found (Explorer restarting) we must not spin.
            if (DateTime.UtcNow - _lastRefreshAttempt > TimeSpan.FromSeconds(2))
                RequestRefresh();
            return;
        }

        ComputeBounds();
        if (!_isShown)
        {
            _isShown = true;
            SampleTaskbarColor();
            _window.Show();
            _vm.SetActive(true);
            Log.Info($"Overlay shown at {_bounds}");
            // The cached placement may be old (icons added while hidden); confirm it in the background.
            if (DateTime.UtcNow - _lastRefreshAttempt > RefreshInterval)
                RequestRefresh();
        }
        ApplyBounds();
        _refreshTimer.Start();
        _autoHideTimer.IsEnabled = _placement.AutoHide;
    }

    private void HideNow()
    {
        if (!_isShown)
            return;
        _isShown = false;
        _refreshTimer.Stop();
        _window.Hide();
        _vm.SetActive(false);
        Log.Info($"Overlay hidden ({HideReason() ?? _pendingHideReason})");
        // Keep watching an auto-hide taskbar while playing so we can reappear with it.
        _autoHideTimer.IsEnabled = _placement?.AutoHide == true && _monitor.IsPlaying;
    }

    private void ComputeBounds()
    {
        if (_placement is null)
            return;
        _bounds = TaskbarPositionService.ComputeOverlayRect(_placement, _settings.Current, _vm.IsCompact);
    }

    private void ApplyBounds()
    {
        if (_hwnd == IntPtr.Zero || _bounds.IsEmpty || !_isShown)
            return;
        WindowInterop.SetBounds(_hwnd, _bounds, topmost: true);
    }

    private void CheckTaskbarShown()
    {
        if (_placement is null)
            return;
        var shown = TaskbarPositionService.IsTaskbarShown(_placement);
        if (shown != _taskbarShown)
        {
            _taskbarShown = shown;
            Evaluate();
        }
    }

    /// <summary>Cheap upkeep every tick; full UIA re-detection only every few ticks.</summary>
    private void OnRefreshTick()
    {
        UpdateFullscreen("tick");
        if (++_ticks % TicksPerDetection == 0)
        {
            RequestRefresh();
            return;
        }
        if (!_isShown)
            return;
        WindowInterop.BringToTopmost(_hwnd);
        SampleTaskbarColor();
    }

    /// <summary>Re-detect the widget off the UI thread; coalesces concurrent requests.</summary>
    private async void RequestRefresh()
    {
        if (_refreshing)
        {
            _refreshAgain = true;
            return;
        }
        _refreshing = true;
        _lastRefreshAttempt = DateTime.UtcNow;
        try
        {
            do
            {
                _refreshAgain = false;
                var monitorName = _settings.Current.MonitorDeviceName;
                var placement = await Task.Run(() => _position.GetPlacement(monitorName));
                if (placement is not null)
                {
                    if (_placement is null || _placement.Widget != placement.Widget || _placement.Taskbar != placement.Taskbar)
                        Log.Info($"Taskbar {placement.Taskbar} on {placement.MonitorName}, widget {placement.Widget} (detected: {placement.WidgetDetected}), scale {placement.Scale:0.##}");
                    _placement = placement;
                    _taskbarShown = TaskbarPositionService.IsTaskbarShown(placement);
                }
                else
                {
                    Log.Warn("Taskbar not found");
                }
            }
            while (_refreshAgain);
        }
        catch (Exception ex)
        {
            Log.Error("Placement refresh failed", ex);
        }
        finally
        {
            _refreshing = false;
        }

        if (_isShown)
        {
            ComputeBounds();
            ApplyBounds();
            SampleTaskbarColor();
        }
        UpdateFullscreen("placement");
        Evaluate();
    }

    /// <summary>
    /// Read a few taskbar pixels right next to the overlay (never under it) and use their
    /// average as background, so the overlay blends with Mica/acrylic/solid taskbars.
    /// </summary>
    private void SampleTaskbarColor()
    {
        if (_placement is not { } p || _bounds.IsEmpty)
            return;
        var t = p.Taskbar;
        var gap = (int)Math.Round(4 * p.Scale);
        var xs = new[] { _bounds.Left - gap, _bounds.Right + gap }.Where(x => x > t.Left && x < t.Right).ToArray();
        if (xs.Length == 0)
            return;

        var inset = (int)Math.Round(3 * p.Scale);
        var bodyPoints = xs.SelectMany(x => new[] { (x, t.Top + inset), (x, t.Bottom - inset) });
        var background = WindowInterop.SampleScreen(bodyPoints);
        var topLine = WindowInterop.SampleScreen(xs.Select(x => (x, t.Top)));
        if (background is { } bg)
            _vm.ApplyTaskbarColors(bg, topLine);
    }

    private void SaveCalibratedBounds(PixelRect r)
    {
        if (r.IsEmpty)
            return;
        _bounds = r;
        _settings.Update(s =>
        {
            s.ManualPlacement = true;
            s.ManualX = r.X;
            s.ManualY = r.Y;
            s.ManualWidth = r.Width;
            s.ManualHeight = r.Height;
        });
        Log.Info($"Calibrated bounds saved: {r}");
    }

    private void ResizeCalibrated(int step)
    {
        var r = _bounds;
        var delta = (int)Math.Round(10 * (_placement?.Scale ?? 1)) * step;
        var width = Math.Max(40, r.Width + delta);
        // Grow towards the side away from the screen edge, like the automatic layout.
        var growLeft = _placement is { } p && r.CenterX > p.Taskbar.CenterX;
        var x = growLeft ? r.Right - width : r.X;
        SaveCalibratedBounds(new PixelRect(x, r.Y, width, r.Height));
        ApplyBounds();
    }
}
