using System.ComponentModel;
using System.Windows;
using Taskitify.Services;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Taskitify.Core.Layout;
using Taskitify.Core.Lyrics;
using Taskitify.Models;
using Taskitify.Infrastructure;
using Taskitify.ViewModels;

namespace Taskitify.Views;

/// <summary>
/// Popover above the overlay. One instance, shown and hidden (never re-created). Closes on
/// Esc, on losing activation (click outside) or when the overlay is clicked again.
/// </summary>
public partial class MiniPlayerWindow : Window
{
    private readonly MiniPlayerViewModel _vm;
    private readonly Func<bool> _animationsEnabled;
    private readonly Func<bool> _useGradient;
    private IntPtr _hwnd;
    private readonly OutsideClickWatcher _outsideClicks;
    private IntPtr _previousForeground;
    private PixelRect _anchor;
    // The user scrolled the lyrics: they stay put until "sync", a line click, a new song or reopening.
    private bool _browsing;
    private bool _lyricsUpdateQueued;
    private bool _animateLyrics;
    // When the running scroll ends. A finished animation stays attached to the transform
    // (FillBehavior.Stop keeps its clock), so HasAnimatedProperties cannot tell it is over.
    private DateTime _scrollEndsAt;
    private bool _inkUpdateQueued;
    private readonly LyricInkDriver _lyricInk;
    private TextBlock? _activeText;

    public MiniPlayerWindow(MiniPlayerViewModel vm, Func<bool> animationsEnabled, Func<bool> useGradient)
    {
        InitializeComponent();
        _vm = vm;
        _animationsEnabled = animationsEnabled;
        _useGradient = useGradient;
        DataContext = vm;
        _lyricInk = new LyricInkDriver(TimeSpan.FromMilliseconds(1000d / 30), SampleLyricInk);
        vm.LyricPositionChanged += (_, _) => QueueLyricInk();
        LyricsViewport.SizeChanged += (_, _) => QueueLyrics(animate: false);

        _outsideClicks = new OutsideClickWatcher(OnOutsideClick);
        Closed += (_, _) => _outsideClicks.Dispose();

        vm.PropertyChanged += OnViewModelChanged;
        vm.CurrentLineChanged += (_, _) => QueueLyrics(vm.ScrollMilliseconds > 0);
        vm.FollowCheck += (_, _) => KeepCentred();
        vm.Lines.CollectionChanged += (_, change) =>
        {
            if (change.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                _activeText = null;
                _lyricInk.Invalidate();
                SetBrowsing(false);
            }
            QueueLyrics(animate: false);
        };
        LyricsViewport.PreviewMouseWheel += OnLyricsWheel;
        // The wheel over the button (or the open slider) changes the volume without opening anything.
        MouseWheelEventHandler wheel = (_, e) =>
        {
            _vm.Volume.Nudge(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        };
        VolumeButton.PreviewMouseWheel += wheel;
        VolumeSlider.PreviewMouseWheel += wheel;
        VolumeButton.MouseEnter += (_, _) => _vm.Volume.Refresh();
        VolumePopup.CustomPopupPlacementCallback = (popup, target, _) =>
        [
            new CustomPopupPlacement(new Point(target.Width - popup.Width, -popup.Height - 4), PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new Point(target.Width - popup.Width, target.Height + 4), PopupPrimaryAxis.Horizontal),
        ];
        VolumePopup.Opened += (_, _) => WindowInterop.RaisePopup(VolumePopup.Child);
        VolumePopup.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _vm.Volume.IsOpen = false;
                VolumeButton.Focus();
                e.Handled = true;
            }
        };

        _vm.Like.JustLiked += (_, _) => LikeIcon.Pulse();
        ContentCard.ContextMenu = BuildMenu();
        ContentCard.MouseLeftButtonDown += OnPinnedDrag;
        // Ctrl + Alt + click on the floating player: clicks pass through from now on.
        ContentCard.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (IsPinned && !_presenting && NativeMethods.GetAsyncKeyState(0x11) < 0 && NativeMethods.GetAsyncKeyState(0x12) < 0)
            {
                e.Handled = true;
                _pinned?.SetClickThrough(true);
            }
        };
        // The pointer's own state: IsMouseOver can still be true while MouseLeave is raised.
        MouseEnter += (_, _) => { _pointerOver = true; UpdateHoverChrome(); UpdatePinnedToolbar(); };
        MouseLeave += (_, _) => { _pointerOver = false; UpdateHoverChrome(); UpdatePinnedToolbar(); };
        MouseMove += (_, e) => OnPresentPointerMoved(e.GetPosition(this));
        IsVisibleChanged += (_, _) => { if (!IsVisible) _pointerOver = false; };
        PinnedSwitch.Click += (_, _) =>
        {
            if (_pinned is { } p)
                p.SwitchMode();
            else
                MinimalRequested?.Invoke(this, EventArgs.Empty);
        };
        PinnedSettings.Click += (_, _) => (Application.Current as App)?.ShowSettings(IsPinned ? SettingsPage.NotMinimal : SettingsPage.MiniPlayer);
        PopupFloat.Click += (_, _) => FloatRequested?.Invoke(this, EventArgs.Empty);
        LyricsSmaller.Click += (_, _) => LyricsSizeRequested?.Invoke(this, -1);
        LyricsBigger.Click += (_, _) => LyricsSizeRequested?.Invoke(this, 1);
        PinnedMaximize.Click += (_, _) => RequestPresentation(fullScreen: false);
        PinnedFullScreen.Click += (_, _) => RequestPresentation(fullScreen: true);
        CaptionRestore.Click += (_, _) => ExitPresentation();
        CaptionClose.Click += (_, _) => _pinned?.TurnOff();
        CaptionMinimize.Click += (_, _) => WindowState = WindowState.Minimized;
        // Back from the taskbar: full screen again, in front.
        StateChanged += (_, _) =>
        {
            if (_presenting && WindowState == WindowState.Normal)
                Dispatcher.BeginInvoke(PlacePresentation);
            PresentationMinimizedChanged?.Invoke(this, EventArgs.Empty);
        };
        PopupKeepOpen.Click += (_, _) =>
        {
            _keepOpen = !_keepOpen;
            UpdatePinnedToolbar();
        };
        PinnedClickThrough.Click += (_, _) => _pinned?.SetClickThrough(true);
        PinnedLock.Click += (_, _) => _pinned?.SetLocked(!_pinned.Locked);
        PinnedClose.Click += (_, _) => _pinned?.TurnOff();
        PinnedGrip.MouseLeftButtonDown += OnGripDown;
        PinnedGrip.MouseMove += OnGripMove;
        PinnedGrip.MouseLeftButtonUp += OnGripUp;
        PinnedGrip.LostMouseCapture += (_, _) =>
        {
            if (!_gripping)
                return;
            _gripping = false;
            CompositionTarget.Rendering -= ApplyGrip;
            SavePinned();
        };
        CoverPart.Cursor = Cursors.Hand;
        CoverPart.ToolTip = LocalizationService.Get("OpenInSpotify");
        CoverPart.MouseLeftButtonUp += (_, _) => OpenSpotify();
        Progress.Seek += _vm.SeekTo;
        Progress.Preview += _vm.PreviewPosition;

        SourceInitialized += (_, _) =>
        {
            _hwnd = WindowInterop.EnsureHandle(this);
            WindowInterop.MakeToolWindow(_hwnd, noActivate: false);
        };

        // The app renders in software to keep the GPU driver's memory out of the process, but
        // this transparent popup redraws most of itself on every lyric scroll, which is cheaper on
        // the GPU. So the process uses the GPU while it is open and software again once it is
        // hidden. The driver loaded by then stays (~12 MB after a full collection, measured)
        // until the app restarts: the saving only lasts until the mini player is first opened.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                GpuRendering.Acquire();
                QueueLyrics(animate: false);
            }
            else
            {
                _lyricInk.Invalidate();
                _activeText = null;
                LyricsOffset.BeginAnimation(TranslateTransform.YProperty, null);
                GpuRendering.Release();
            }
        };
    }

    /// <summary>
    /// Right-click on the player: its mode, its background, sharing the lyrics and its settings,
    /// without going through the settings window.
    /// </summary>
    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { Placement = PlacementMode.Top, PlacementTarget = ContentCard };
        ContentCard.ContextMenuOpening += (_, e) =>
        {
            var point = e.CursorLeft < 0
                ? new Point(ContentCard.ActualWidth / 2, ContentCard.ActualHeight / 2)
                : Mouse.GetPosition(ContentCard);
            menu.PlacementRectangle = new Rect(point, new Size(1, 1));
        };
        var vertical = new MenuItem { IsCheckable = true };
        vertical.Click += (_, _) => _vm.SetLayout(MiniPlayerLayout.Vertical);
        var wide = new MenuItem { IsCheckable = true };
        wide.Click += (_, _) => _vm.SetLayout(MiniPlayerLayout.Cinema);
        var background = new MenuItem();
        background.SubmenuOpened += (_, _) => WindowInterop.RaisePopup(background.Items.OfType<MenuItem>().FirstOrDefault());
        foreach (var style in Enum.GetValues<MiniPlayerBackground>())
        {
            var item = new MenuItem { Tag = style, IsCheckable = true };
            item.Click += (_, _) => _vm.SetBackground(style);
            background.Items.Add(item);
        }
        var share = new MenuItem { Icon = MenuGlyph("\uE72D") };
        share.Click += (_, _) => _vm.ShareCommand.Execute(null);
        var spotify = new MenuItem { Icon = MenuGlyph("\uE8A7") };
        spotify.Click += (_, _) => OpenSpotify();
        var settings = new MenuItem { Icon = MenuGlyph("\uE713") };
        settings.Click += (_, _) => (Application.Current as App)?.ShowSettings(SettingsPage.MiniPlayer);
        menu.Items.Add(vertical);
        menu.Items.Add(wide);
        menu.Items.Add(background);
        menu.Items.Add(new Separator());
        menu.Items.Add(share);
        menu.Items.Add(spotify);
        menu.Items.Add(new Separator());
        menu.Items.Add(settings);
        menu.Opened += (_, _) =>
        {
            _vm.Volume.IsOpen = false;
            vertical.Header = LocalizationService.Get("ModeVertical");
            wide.Header = LocalizationService.Get("ModeWide");
            vertical.IsChecked = !_vm.IsCinema;
            wide.IsChecked = _vm.IsCinema;
            background.Header = LocalizationService.Get("LookBackground");
            foreach (var item in background.Items.OfType<MenuItem>())
            {
                var style = (MiniPlayerBackground)item.Tag;
                item.Header = LocalizationService.Get("LookBackground" + style);
                item.IsChecked = _vm.Look.Background == style;
            }
            share.Header = LocalizationService.Get("LyricCardOpen");
            spotify.Header = LocalizationService.Get("OpenInSpotify");
            settings.Header = LocalizationService.Get("MiniPlayerSettings");
            WindowInterop.RaisePopup(menu);
        };
        return menu;
    }

    private static TextBlock MenuGlyph(string glyph) => new()
    {
        Text = glyph,
        FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
        FontSize = 14,
    };

    /// <summary>Brings Spotify up (its own protocol: the running app comes to the front).</summary>
    private static void OpenSpotify()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("spotify:") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"Could not open Spotify: {ex.Message}");
        }
    }

    /// <summary>When the popup last hid; lets the overlay click that caused it not re-open it.</summary>
    public DateTime LastHidden { get; private set; }

    /// <summary>
    /// Shown as a live preview while its appearance is being customized in Settings: it stays
    /// open next to the settings, without taking focus, until the preview ends.
    /// </summary>
    public bool IsPreview { get; private set; }

    /// <summary>The settings closed: a preview hides; a player the user opened stays.</summary>
    public void EndPreview()
    {
        if (!IsPreview)
            return;
        IsPreview = false;
        HidePopup();
    }

    private void OnOutsideClick(int x, int y)
    {
        if (VolumePopup.IsOpen && WindowInterop.PopupContains(VolumePopup.Child, x, y))
            return;
        if (ContentCard.ContextMenu is { IsOpen: true } menu
            && (WindowInterop.PopupContains(menu, x, y)
                || menu.Items.OfType<MenuItem>().Any(item => item.IsSubmenuOpen
                    && WindowInterop.PopupContains(item.Items.OfType<MenuItem>().FirstOrDefault(), x, y))))
            return;
        // Clicks on the overlay toggle the player themselves.
        if (!Contains(WindowInterop.GetBounds(_hwnd), x, y) && !Contains(_anchor, x, y))
        {
            if (!_keepOpen)
                Dispatcher.BeginInvoke(HidePopup);
            else if (VolumePopup.IsOpen)
                Dispatcher.BeginInvoke(() => _vm.Volume.IsOpen = false);
        }
        else if (VolumePopup.IsOpen && !ContainsVolumeButton(x, y))
            Dispatcher.BeginInvoke(() => _vm.Volume.IsOpen = false);
    }

    /// <summary>Shown far off-screen for the welcome tour, which displays snapshots of it: no position, no hooks.</summary>
    internal void ShowOffscreen()
    {
        _vm.IsActive = true;
        ApplyLayout();
        ApplyControlsStyle();
        UpdatePlayGlyph();
        UpdateProgressValue();
        ApplyBackground();
        Topmost = false;
        Left = -20000;
        Top = -20000;
        Opacity = 1;
        Show();
        UpdateLayout();
        ScrollToCurrent(animate: false);
        QueueLyrics(animate: false);
    }

    /// <summary>"Not that minimal": the player floats, pinned (it does not close on a click outside or Esc).</summary>
    public bool IsPinned { get; private set; }

    private PinnedOptions? _pinned;
    private ModifierClickWatcher? _unpinClicks;
    private ForegroundWatcher? _pinnedForeground;
    private bool _gripping;
    private NativeMethods.POINT _gripStart;
    private (double Width, double Lyrics, double Zoom, double WindowWidth, double WindowHeight) _gripFrom;

    /// <summary>The popup's hover buttons: float the whole player, or switch to Minimal Mode.</summary>
    public event EventHandler? FloatRequested;
    public event EventHandler? MinimalRequested;

    /// <summary>The hover buttons' A- (-1) and A+ (+1): the lyrics' size, one step.</summary>
    public event EventHandler<int>? LyricsSizeRequested;

    // Floating and filling its screen: the place and shape to go back to.
    private (PixelRect Bounds, double Width, double Lyrics)? _beforeFill;

    private bool _pointerOver;

    // The popup kept open with its pin: clicks elsewhere and losing the focus no longer close it;
    // Esc, a click on the bar's lyric or unpinning it still do.
    private bool _keepOpen;
    private bool _gripShape;
    private (double Width, double Lyrics, double Zoom)? _gripPending;

    // Floating, the whole player scales with its grip (cover, text, buttons and lyrics together).
    // Its shape (width and lyrics height) is kept unscaled, the window is that shape times the zoom.
    private double _zoom = 1;
    private double _baseWidth = 384;
    private const double ShadowRoom = 24; // Chrome's 12 DIP margin on both sides, never scaled
    private const double MinZoom = 0.7, MaxZoom = 2.5;

    /// <summary>The window's width for a shape width at the current zoom.</summary>
    private void SetBaseWidth(double width)
    {
        _baseWidth = width;
        Width = ((width - ShadowRoom) * _zoom) + ShadowRoom;
    }

    private void SetZoom(double zoom)
    {
        _zoom = zoom;
        Chrome.LayoutTransform = Math.Abs(zoom - 1) < 0.001 ? Transform.Identity : new ScaleTransform(zoom, zoom);
        SetBaseWidth(_baseWidth);
    }

    /// <summary>
    /// Shows the player floating at a saved place (or bottom right of the main screen), until
    /// <see cref="Unpin"/>, with Minimal Mode's possibilities: clicks through (Ctrl + Alt + click
    /// both ways), never taking the focus, above games, locked, resized with its grip.
    /// </summary>
    public void ShowPinned(PixelRect saved, PinnedOptions options)
    {
        _pinned = options;
        if (!IsPinned && IsVisible)
            HidePopup(force: true);
        IsPinned = true;
        IsPreview = false;
        _zoom = 1; // an earlier version scaled the whole player; it now adapts instead
        if (IsVisible)
        {
            ApplyPinned();
            return;
        }
        _vm.IsActive = true;
        ApplyLayout();
        ApplyControlsStyle();
        UpdatePlayGlyph();
        UpdateProgressValue();
        OnViewModelChanged(this, new PropertyChangedEventArgs(nameof(MiniPlayerViewModel.StatusMessage)));
        ApplyBackground();
        BeginAnimation(OpacityProperty, null);
        ChromeOffset.BeginAnimation(TranslateTransform.YProperty, null);
        Opacity = 1;
        Show();
        UpdateLayout();
        _hwnd = WindowInterop.EnsureHandle(this);
        var bounds = WindowInterop.GetBounds(_hwnd);
        var areas = TaskbarPositionService.GetMonitors().OrderByDescending(m => m.IsPrimary).Select(m => m.WorkArea).ToList();
        var place = saved.IsEmpty || areas.Count == 0
            ? areas.Count > 0 ? FloatingPlacement.Snap(bounds, areas[0], FloatingSpot.BottomRight, 24) : bounds
            : FloatingPlacement.Resolve(new PixelRect(saved.X, saved.Y, bounds.Width, bounds.Height), areas, (bounds.Width, bounds.Height));
        WindowInterop.SetPosition(_hwnd, place.X, place.Y);
        ApplyPinned();
        SetBrowsing(false);
        ScrollToCurrent(animate: false);
        QueueLyrics(animate: false);
    }

    /// <summary>Back to a regular popup, hidden.</summary>
    public void Unpin()
    {
        if (!IsPinned)
            return;
        _presentEsc?.Stop();
        if (_presenting)
        {
            if (WindowState != WindowState.Normal)
                WindowState = WindowState.Normal;
            SetAppWindow(false);
        }
        _presenting = false;
        StopPresentIdle();
        _vm.WantsLargeCover = false;
        _beforeFill = null;
        IsPinned = false;
        _pinned = null;
        _unpinClicks?.Stop();
        _pinnedForeground?.Dispose();
        _pinnedForeground = null;
        PinnedMark.Visibility = PinnedGrip.Visibility = Visibility.Collapsed;
        SetExStyle(transparent: false, noActivate: false);
        _vm.FitScale = 1;
        _zoom = 1;
        Chrome.LayoutTransform = Transform.Identity;
        Topmost = true;
        BeginAnimation(ToolbarShownProperty, null);
        SetValue(ToolbarShownProperty, 0.0);
        UpdatePinnedToolbar();
        foreach (UIElement part in new UIElement[] { ActionsPart, ProgressPart, ControlsPart })
            LyricMotionText.Tween(part, OpacityProperty, 1, 0, expressive: false);
        HidePopup(force: true);
        ApplyLayout();
    }

    /// <summary>The options as they are now (called on every settings change while pinned).</summary>
    private void ApplyPinned()
    {
        if (_pinned is not { } o)
            return;
        // Maximized or full screen, a settings change must not turn it back into a floating window
        // (on top of everything, never focused, maybe click-through): it stays an app until it leaves.
        if (_presenting)
        {
            Topmost = false;
            PinnedMark.Visibility = PinnedGrip.Visibility = Visibility.Collapsed;
            _unpinClicks?.Stop();
            ApplyLayout();
            UpdateFit();
            UpdateHoverChrome();
            UpdatePinnedToolbar();
            return;
        }
        Topmost = o.Topmost;
        if (!o.Topmost)
            NativeMethods.SetWindowPos(_hwnd, new IntPtr(-2), 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        // Games keep the focus: the player never takes it while floating.
        SetExStyle(o.ClickThrough, noActivate: true);
        PinnedMark.Visibility = o.ClickThrough && o.ShowMark ? Visibility.Visible : Visibility.Collapsed;
        PinnedGrip.Visibility = o.ClickThrough || o.Locked || _presenting ? Visibility.Collapsed : Visibility.Visible;
        _unpinClicks ??= new ModifierClickWatcher(
            (x, y) => IsVisible && IsPinned && Contains(WindowInterop.GetBounds(_hwnd), x, y),
            () => Dispatcher.BeginInvoke(() => _pinned?.SetClickThrough(false)));
        if (o.ClickThrough)
            _unpinClicks.Start();
        else
            _unpinClicks.Stop();
        if (o.Topmost && _pinnedForeground is null)
            _pinnedForeground = new ForegroundWatcher(h =>
            {
                if (h != _hwnd && IsPinned && !_presenting && _pinned?.Topmost == true && !IsOwnWindow(h))
                    WindowInterop.BringToTopmost(_hwnd);
            });
        else if (!o.Topmost && _pinnedForeground is not null)
        {
            _pinnedForeground.Dispose();
            _pinnedForeground = null;
        }
        ApplyLayout();
        UpdateFit();
        UpdateHoverChrome();
        UpdatePinnedToolbar();
    }

    /// <summary>Floating with controls on hover: only the cover, the song and the lyrics until the pointer comes.</summary>
    private void UpdateHoverChrome()
    {
        var hide = IsPinned && _pinned is { ControlsOnHover: true } && !_pointerOver && !_gripping;
        var target = hide ? 0.0 : 1.0;
        var duration = _animationsEnabled() && SystemParameters.ClientAreaAnimation ? 160 : 0;
        foreach (UIElement part in new UIElement[] { ActionsPart, ProgressPart, ControlsPart })
            LyricMotionText.Tween(part, OpacityProperty, target, duration, expressive: false);
        // The grip goes with the window's own buttons.
        var grip = IsPinned && _pinned is { PlayerButtonsOnHover: true } && !_pointerOver && !_gripping ? 0.0 : _pointerOver || _gripping ? 1.0 : 0.6;
        LyricMotionText.Tween(PinnedGrip, OpacityProperty, grip, duration, expressive: false);
    }

    /// <summary>Floating, the player resizes further than as a popup.</summary>
    private static (double Min, double Max) PinnedWidthRange(bool cinema) => cinema ? (400, 4000) : (280, 4000);

    // Maximized or full screen: the area it covers (in pixels) and the room around the content.
    private bool _presenting;
    // Presenting: over the whole monitor (taskbar included) rather than its work area.
    private bool _fullScreen;
    private PixelRect _presentMonitor;
    private Thickness _presentPadding;
    private DispatcherTimer? _presentEsc;

    /// <summary>
    /// Floating: maximized (the monitor's work area, the taskbar still there) or full screen (the
    /// whole monitor at its own resolution), laid out like a listening room: the cover and the song
    /// on the left, large lyrics on the right, over a backdrop made from the cover. The same button
    /// again: back where it was; the other one: switch between the two. Full screen also leaves with Esc.
    /// </summary>
    private void TogglePresentation(bool fullScreen)
    {
        if (!IsPinned || _hwnd == IntPtr.Zero)
            return;
        if (_beforeFill is not null && _fullScreen == fullScreen)
        {
            ExitPresentation();
            return;
        }
        var bounds = _beforeFill?.Bounds ?? WindowInterop.GetBounds(_hwnd);
        if (WindowInterop.MonitorForRect(bounds) is not { } monitor)
            return;
        _beforeFill ??= (bounds, _baseWidth, _lyricsBase);
        _fullScreen = fullScreen;
        _presentMonitor = fullScreen ? monitor.Bounds : monitor.WorkArea;
        _presenting = true;
        SetAppWindow(true);
        // An app like any other while it fills the screen: not above the rest, in Alt+Tab, with the focus.
        Topmost = false;
        NativeMethods.SetWindowPos(_hwnd, new IntPtr(-2), 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        _vm.WantsLargeCover = true;
        ApplyLayout();
        ApplyBackground();
        UpdateLayout();
        PlacePresentation();
        // The player never takes the focus, so Esc is watched rather than received.
        _presentEsc ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _presentEsc.Tick -= OnPresentEsc;
        if (fullScreen)
        {
            _presentEsc.Tick += OnPresentEsc;
            NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE);
            _presentEsc.Start();
        }
        else
        {
            _presentEsc.Stop();
        }
        _presentIdle = true;
        WakePresentation();
        UpdatePinnedToolbar();
        ScrollToCurrent(animate: false);
        QueueLyrics(animate: false);
    }

    /// <summary>Over its area, in front and with the focus, as an app window (Alt+Tab leaves and comes back).</summary>
    private void PlacePresentation()
    {
        if (!_presenting || _hwnd == IntPtr.Zero)
            return;
        var margin = (int)Math.Round(ShadowRoom / 2 * WindowInterop.GetScale(_hwnd));
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, _presentMonitor.X - margin, _presentMonitor.Y - margin, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        Activate();
    }

    /// <summary>
    /// Full screen, the player is an app window like any other: a taskbar button to come back to
    /// after minimizing, and the focus. Floating again, it goes back to never taking the focus.
    /// </summary>
    private void SetAppWindow(bool app)
    {
        if (_hwnd == IntPtr.Zero)
            return;
        var ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        ex = app
            ? (ex | NativeMethods.WS_EX_APPWINDOW) & ~NativeMethods.WS_EX_TOOLWINDOW & ~NativeMethods.WS_EX_NOACTIVATE
            : (ex | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE) & ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex);
        ShowInTaskbar = app;
        CaptionButtons.Visibility = app ? Visibility.Visible : Visibility.Collapsed;
        if (!app && _pinned is { } o)
            SetExStyle(o.ClickThrough, noActivate: true);
    }

    /// <summary>
    /// Full screen: the hover tools look like the window buttons beside them (flat, the same
    /// height, a hairline between the two groups), one bar instead of a pill next to a title bar.
    /// Elsewhere: the floating pill.
    /// </summary>
    private void ApplyToolbarLook()
    {
        var tool = (Style)(_presenting ? CaptionButtons.Resources["Caption"] : PinnedToolbar.Resources["PinnedTool"]);
        foreach (var button in ((Panel)PinnedToolbar.Child).Children.OfType<Button>())
        {
            if (button.Style != tool)
                button.Style = tool;
            if (_presenting)
                button.Width = 40;
            else
                button.ClearValue(WidthProperty);
        }
        if (_presenting)
        {
            PinnedToolbar.Margin = new Thickness(0, 0, 46 * 3, 0);
            PinnedToolbar.Padding = new Thickness(0);
            PinnedToolbar.CornerRadius = new CornerRadius(0);
            PinnedToolbar.Background = Brushes.Transparent;
            PinnedToolbar.BorderThickness = new Thickness(0, 0, 1, 0);
            PinnedToolbar.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            PinnedToolbar.Effect = null;
        }
        else if (PinnedToolbar.Effect is null)
        {
            PinnedToolbar.Margin = new Thickness(0, 5, 6, 0);
            PinnedToolbar.Padding = new Thickness(2);
            PinnedToolbar.CornerRadius = new CornerRadius(9);
            PinnedToolbar.SetResourceReference(Border.BackgroundProperty, "PopupBackgroundBrush");
            PinnedToolbar.BorderThickness = new Thickness(1);
            PinnedToolbar.SetResourceReference(Border.BorderBrushProperty, "PopupBorderBrush");
            PinnedToolbar.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Direction = 270, Opacity = 0.3, Color = Colors.Black };
        }
    }

    private void OnPresentEsc(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            return;
        if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8001) != 0)
            ExitPresentation();
    }

    private void ExitPresentation()
    {
        _presentEsc?.Stop();
        if (_beforeFill is not { } before)
            return;
        _beforeFill = null;
        _presenting = false;
        StopPresentIdle();
        _vm.WantsLargeCover = false;
        if (WindowState != WindowState.Normal)
            WindowState = WindowState.Normal;
        SetAppWindow(false);
        _vm.PresentationHeight = 0;
        ApplyLayout();
        SetBaseWidth(before.Width);
        ResizeLyrics(before.Lyrics);
        UpdateFit();
        AdaptToSize();
        ApplyBackground();
        UpdateLayout();
        if (_hwnd != IntPtr.Zero)
            WindowInterop.SetPosition(_hwnd, before.Bounds.X, before.Bounds.Y);
        // Floating again: all of its options (on top, click-through, lock, mark) as they are now.
        ApplyPinned();
        if (_pinned is { Topmost: false })
            NativeMethods.SetWindowPos(_hwnd, new IntPtr(-2), 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        PresentationMinimizedChanged?.Invoke(this, EventArgs.Empty);
        ScrollToCurrent(animate: false);
        QueueLyrics(animate: false);
    }

    /// <summary>Maximized or full screen and minimized: out of sight, so the taskbar player is the way back.</summary>
    public bool IsPresentationMinimized => _presenting && WindowState == WindowState.Minimized;

    /// <summary>The maximized or full screen player was minimized, restored or left.</summary>
    public event EventHandler? PresentationMinimizedChanged;

    /// <summary>The popup's maximize or full screen button: the app floats the player, then calls <see cref="Present"/>.</summary>
    public event EventHandler<bool>? PresentRequested;

    /// <summary>Floating: maximized (false) or full screen (true), as its buttons do.</summary>
    public void Present(bool fullScreen)
    {
        if (IsPinned && !(_presenting && _fullScreen == fullScreen))
            TogglePresentation(fullScreen);
    }

    private void RequestPresentation(bool fullScreen)
    {
        if (IsPinned)
            TogglePresentation(fullScreen);
        else
            PresentRequested?.Invoke(this, fullScreen);
    }

    /// <summary>
    /// Maximized or full screen, then minimized, and the taskbar player clicked: the player comes
    /// back as the regular mini player over the bar (the app turns floating off first).
    /// </summary>
    public void LeavePresentation()
    {
        if (!_presenting)
            return;
        if (WindowState != WindowState.Normal)
            WindowState = WindowState.Normal;
        ExitPresentation();
    }

    /// <summary>The hover buttons' choices (which, in which order): the popup follows them too.</summary>
    public PlayerTools? Tools { get; set; }

    /// <summary>The full screen arrangement, after ApplyLayout has placed the usual one.</summary>
    private void ApplyPresentation()
    {
        var scale = WindowInterop.GetScale(_hwnd);
        double width = _presentMonitor.Width / scale, height = _presentMonitor.Height / scale;
        _vm.PresentationHeight = height;
        ShadowLayer.Visibility = Visibility.Collapsed;
        ContentCard.CornerRadius = new CornerRadius(0);
        ContentCard.BorderThickness = new Thickness(0);
        var padX = Math.Round(width * 0.06);
        var padY = Math.Round(height * 0.08);
        _presentPadding = new Thickness(padX, padY, padX, padY);

        // Left: the song, centred as a block; right: the lyrics, the full height.
        LayoutRoot.RowDefinitions.Clear();
        LayoutRoot.ColumnDefinitions.Clear();
        LayoutRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 4; i++)
            LayoutRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        LayoutRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        LayoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44, GridUnitType.Star) });
        LayoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56, GridUnitType.Star) });
        Place(HeaderPart, 1, 0);
        Place(ProgressPart, 2, 0);
        Place(ControlsPart, 3, 0);
        Place(ConfirmPart, 4, 0);
        Place(LyricsPart, 0, 1);
        Grid.SetRowSpan(LyricsPart, 6);

        var inner = width - (2 * padX);
        var cover = Math.Round(Math.Min(height * 0.42, inner * 0.44 * 0.78));
        CoverPart.Width = CoverPart.Height = cover;
        CoverPart.Margin = new Thickness(0, 0, 0, Math.Round(height * 0.03));
        CoverImage.CornerRadius = CoverPlaceholder.CornerRadius = new CornerRadius(Math.Round(cover * 0.025));
        foreach (FrameworkElement part in new FrameworkElement[] { HeaderPart, ProgressPart, ControlsPart })
        {
            part.Width = cover;
            part.HorizontalAlignment = HorizontalAlignment.Center;
        }
        ProgressPart.Margin = new Thickness(0, Math.Round(height * 0.02), 0, 0);
        ArtistText.FontSize = Math.Round(height * 0.02);
        TitleText.TextWrapping = TextWrapping.Wrap;
        TitleText.MaxHeight = double.PositiveInfinity;
        PlayPauseButton.Margin = new Thickness(18, 0, 18, 0);

        _lyricsMargin = new Thickness(Math.Round(width * 0.03), 0, 0, 0);
        _lyricsBase = height - (2 * padY);
        PinnedGrip.Visibility = Visibility.Collapsed;
        SetBaseWidth(width + ShadowRoom);
        ApplyToolbarRoom();
    }

    /// <summary>What full screen changed, back to the usual (nothing to do when it did not).</summary>
    private void ResetPresentation()
    {
        _vm.PresentationHeight = 0;
        ShadowLayer.Visibility = Visibility.Visible;
        ContentCard.CornerRadius = new CornerRadius(12);
        ContentCard.BorderThickness = new Thickness(1);
        CoverImage.CornerRadius = CoverPlaceholder.CornerRadius = new CornerRadius(8);
        foreach (FrameworkElement part in new FrameworkElement[] { HeaderPart, ProgressPart, ControlsPart })
        {
            part.ClearValue(WidthProperty);
            part.ClearValue(HorizontalAlignmentProperty);
        }
        ProgressPart.Margin = new Thickness(0, 2, 0, 0);
        ArtistText.FontSize = 13.5;
        Grid.SetRowSpan(LyricsPart, 1);
    }

    /// <summary>
    /// Floating, sized with the grip: the cover follows the room (the lyrics' height in the wide
    /// layout, the width in the vertical one) and the transport buttons spread or close up with
    /// the width. The popup keeps its usual layout.
    /// </summary>
    private void AdaptToSize()
    {
        if (_presenting)
            return;
        var content = _baseWidth - ShadowRoom - 32; // card padding
        if (!IsPinned)
        {
            PlayPauseButton.Margin = new Thickness(8, 0, 8, 0);
            TitleText.TextWrapping = TextWrapping.NoWrap;
            TitleText.MaxHeight = double.PositiveInfinity;
            return;
        }
        if (_vm.IsCinema)
        {
            if (_vm.ShowCover)
            {
                // The song's column is the cover plus its 28 DIPs (196 for the usual 168): it widens
                // and narrows with it, and the lyrics keep at least two thirds of the width.
                var cover = Math.Clamp(_lyricsBase * 168 / 268, 72, 560);
                cover = Math.Round(Math.Min(cover, Math.Max(72, (content * 0.34) - 28)));
                // The song's column (cover, title, actions) never taller than the lyrics beside it.
                cover = Math.Max(72, Math.Min(cover, _lyricsBase - 100));
                CoverPart.Width = CoverPart.Height = cover;
                if (LayoutRoot.ColumnDefinitions.Count == 2)
                    LayoutRoot.ColumnDefinitions[0].Width = new GridLength(cover + 28);
            }
        }
        else
        {
            CoverPart.Width = CoverPart.Height = Math.Round(Math.Clamp(content * 64 / 320, 48, 120));
        }
        // A narrow song column: the title takes two lines rather than being cut short.
        var narrow = _vm.IsCinema && LayoutRoot.ColumnDefinitions.Count == 2 && LayoutRoot.ColumnDefinitions[0].Width.Value < 170;
        TitleText.TextWrapping = narrow ? TextWrapping.Wrap : TextWrapping.NoWrap;
        TitleText.MaxHeight = narrow ? Math.Ceiling(TitleText.FontSize * 1.34 * 2) : double.PositiveInfinity;
        var gap = Math.Clamp((content - 300) / 14, 2, 28);
        PlayPauseButton.Margin = new Thickness(gap, 0, gap, 0);
    }

    private void SetExStyle(bool transparent, bool noActivate)
    {
        if (_hwnd == IntPtr.Zero)
            return;
        var ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        var wanted = transparent ? ex | NativeMethods.WS_EX_TRANSPARENT : ex & ~NativeMethods.WS_EX_TRANSPARENT;
        wanted = noActivate ? wanted | NativeMethods.WS_EX_NOACTIVATE : wanted & ~NativeMethods.WS_EX_NOACTIVATE;
        if (wanted != ex)
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, wanted);
    }

    /// <summary>Lyrics shorter than usual (resized with the grip): their type shrinks with them, if chosen.</summary>
    private void UpdateFit()
    {
        var usual = _vm.IsCinema ? (_vm.ShowCover ? 268 : 200) : 176;
        _vm.FitScale = IsPinned && _pinned is { AutoFit: true } && Verses == 0 ? _lyricsBase / usual : 1;
    }

    /// <summary>One click from Settings: the floating player moved to a spot of its screen.</summary>
    public void PlaceAt(FloatingSpot spot)
    {
        if (!IsPinned || !IsVisible)
            return;
        var bounds = WindowInterop.GetBounds(_hwnd);
        if (WindowInterop.MonitorForRect(bounds) is not { } monitor)
            return;
        // The shadow margin (12 DIPs) is part of the window: the card itself keeps the distance.
        var place = FloatingPlacement.Snap(bounds, monitor.WorkArea, spot, (int)Math.Round(20 * WindowInterop.GetScale(_hwnd)));
        WindowInterop.SetPosition(_hwnd, place.X, place.Y);
        SavePinned();
    }

    private void SavePinned()
    {
        // Full screen is not a size to come back to: the place and size before it stay saved.
        if (_presenting)
            return;
        _pinned?.Saved(WindowInterop.GetBounds(_hwnd), (int)Math.Round(_baseWidth), (int)Math.Round(_lyricsBase), Math.Round(_zoom, 3));
    }

    private void OnGripDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsPinned || _pinned is { Locked: true } or { ClickThrough: true })
            return;
        NativeMethods.GetCursorPos(out _gripStart);
        _gripFrom = (_baseWidth, _lyricsBase, _zoom, ActualWidth, ActualHeight);
        // Shift + drag: the old way, more width and more verses at the same size.
        _gripShape = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        _gripping = PinnedGrip.CaptureMouse();
        if (_gripping)
            CompositionTarget.Rendering += ApplyGrip;
        e.Handled = true;
    }

    private void OnGripMove(object sender, MouseEventArgs e)
    {
        if (!_gripping)
            return;
        NativeMethods.GetCursorPos(out var now);
        var scale = WindowInterop.GetScale(_hwnd);
        var dx = (now.X - _gripStart.X) / scale;
        var dy = (now.Y - _gripStart.Y) / scale;
        // A real resize: the corner follows the pointer and the layout adapts (shorter progress
        // bar, closer buttons, a smaller cover); text and buttons keep their size.
        // Up to the whole screen it is on (the shadow margin may go past its edges).
        var (min, max) = PinnedWidthRange(_vm.IsCinema);
        var maxLyrics = 900.0;
        var bounds = WindowInterop.GetBounds(_hwnd);
        if (WindowInterop.MonitorForRect(bounds) is { } monitor)
        {
            max = Math.Max(min, ((monitor.WorkArea.Right - bounds.Left) / scale) + (ShadowRoom / 2));
            maxLyrics = Math.Max(70, _gripFrom.Lyrics + ((monitor.WorkArea.Bottom - bounds.Bottom) / scale) + (ShadowRoom / 2));
        }
        _beforeFill = null;
        // A set number of verses fixes the height: the grip then changes the width only.
        var lyrics = Verses > 0 ? _gripFrom.Lyrics : Math.Clamp(_gripFrom.Lyrics + dy, 70, maxLyrics);
        _gripPending = (Math.Clamp(_gripFrom.Width + dx, min, max), lyrics, 1);
    }

    /// <summary>Once per frame while the grip moves: one layout per frame, however fast the mouse.</summary>
    private void ApplyGrip(object? sender, EventArgs e)
    {
        if (_gripPending is not { } next)
            return;
        _gripPending = null;
        if (Math.Abs(next.Zoom - _zoom) > 0.0005)
            SetZoom(next.Zoom);
        if (Math.Abs(next.Width - _baseWidth) > 0.25)
            SetBaseWidth(next.Width);
        if (Math.Abs(next.Lyrics - _lyricsBase) > 0.25)
        {
            ResizeLyrics(next.Lyrics);
            UpdateFit();
        }
        AdaptToSize();
    }

    private void OnGripUp(object sender, MouseButtonEventArgs e)
    {
        if (!_gripping)
            return;
        ApplyGrip(null, EventArgs.Empty);
        _gripping = false;
        CompositionTarget.Rendering -= ApplyGrip;
        PinnedGrip.ReleaseMouseCapture();
        SavePinned();
    }

    /// <summary>Pinned: dragged by any part that is not a control (buttons, progress, lyrics).</summary>
    private void OnPinnedDrag(object sender, MouseButtonEventArgs e)
    {
        if (!IsPinned || _presenting || _pinned is { Locked: true } || e.OriginalSource is not DependencyObject source)
            return;
        for (var node = source; node is not null && node != ContentCard; node = node is Visual ? VisualTreeHelper.GetParent(node) : null)
        {
            if (node is System.Windows.Controls.Primitives.ButtonBase or SeekBar or Slider or LyricMotionText || node == CoverPart)
                return;
        }
        try { DragMove(); }
        catch (InvalidOperationException) { return; }
        SavePinned();
    }

    public void ShowAt(PixelRect anchor, PixelRect workArea, double scale, bool preview = false)
    {
        // Floating (not that minimal): it is already where the user put it.
        if (IsPinned)
        {
            Activate();
            return;
        }
        // Opened by the user (a click): a regular popup again, even if it was a preview.
        var wasVisible = IsVisible;
        IsPreview = preview && (IsPreview || !wasVisible);
        _vm.IsActive = true;
        ApplyLayout();
        ApplyControlsStyle();
        UpdatePlayGlyph();
        UpdateProgressValue();
        OnViewModelChanged(this, new PropertyChangedEventArgs(nameof(MiniPlayerViewModel.StatusMessage)));
        ApplyBackground();

        // Remember who had focus so closing with Esc gives it back.
        if (!wasVisible)
        {
            _previousForeground = NativeMethods.GetForegroundWindow();
            // A clock from the last opening can override the base opacity while the window
            // is shown at its previous position. Clear both clocks before making it visible.
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            ChromeOffset.BeginAnimation(TranslateTransform.YProperty, null);
        }
        Show();
        UpdateLayout();
        _hwnd = WindowInterop.EnsureHandle(this);

        // Physical size from DIPs at the taskbar monitor's scale.
        var width = (int)Math.Round(ActualWidth * scale);
        var height = (int)Math.Round(ActualHeight * scale);
        var shadow = (int)Math.Round(12 * scale);
        var gap = (int)Math.Round(8 * scale);

        var x = (int)Math.Round(anchor.CenterX - (width / 2.0));
        x = Math.Clamp(x, workArea.Left - shadow, Math.Max(workArea.Left - shadow, workArea.Right + shadow - width));
        int y;
        if (anchor.Top >= workArea.Bottom - 2 || anchor.Top > workArea.Top + (workArea.Height / 2))
            y = Math.Min(anchor.Top, workArea.Bottom) - height + shadow - gap; // taskbar at the bottom: open upwards
        else
            y = Math.Max(anchor.Bottom, workArea.Top) - shadow + gap;          // top taskbar: open downwards

        // Position only: WPF keeps owning the size (SizeToContent, DPI changes between monitors).
        WindowInterop.SetPosition(_hwnd, x, y);
        _anchor = anchor;
        SetBrowsing(false);
        ScrollToCurrent(animate: false);
        QueueLyrics(animate: false);
        _outsideClicks.Start();
        if (IsPreview)
        {
            // Beside the settings, which keep the focus.
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            return;
        }
        if (!IsActive && !Activate())
            FocusHelper.ForceForeground(_hwnd);
        if (wasVisible)
            return;

        Opacity = 1;
        if (_animationsEnabled() && SystemParameters.ClientAreaAnimation)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop });
            ChromeOffset.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        }
    }

    public void HidePopup() => HidePopup(force: false);

    /// <param name="force">Also when pinned (floating): only turning that mode off hides it.</param>
    public void HidePopup(bool force)
    {
        if (IsPinned && !force)
            return;
        IsPreview = false;
        if (!IsVisible)
            return;
        LastHidden = DateTime.UtcNow;
        _vm.IsActive = false;
        _keepOpen = false;
        _outsideClicks.Stop();
        var restoreFocus = NativeMethods.GetForegroundWindow() == _hwnd;
        Hide();
        if (restoreFocus && _previousForeground != IntPtr.Zero && _previousForeground != _hwnd)
            NativeMethods.SetForegroundWindow(_previousForeground);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        // A preview stays while the settings are used; a menu of its own does not close it either.
        if (IsPreview || IsPinned || _keepOpen || ContentCard.ContextMenu is { IsOpen: true } || VolumePopup.Child is { IsKeyboardFocusWithin: true })
            return;
        HidePopup();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Escape when _vm.Volume.IsOpen:
                _vm.Volume.IsOpen = false;
                VolumeButton.Focus();
                e.Handled = true;
                break;
            case Key.Escape:
                HidePopup();
                break;
            case Key.Space:
                _vm.PlayPauseCommand.Execute(null);
                break;
            // Arrows step through the song, like most players.
            case Key.Left when !Progress.IsDragging:
                _vm.SeekBy(-5);
                break;
            case Key.Right when !Progress.IsDragging:
                _vm.SeekBy(5);
                break;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Alt+F4 must not destroy the single instance.
        if (Application.Current is App { IsShuttingDown: false })
        {
            e.Cancel = true;
            HidePopup();
        }
        base.OnClosing(e);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MiniPlayerViewModel.IsPlaying):
                UpdatePlayGlyph();
                break;
            case nameof(MiniPlayerViewModel.PositionSeconds):
            case nameof(MiniPlayerViewModel.DurationSeconds):
                UpdateProgressValue();
                break;
            case nameof(MiniPlayerViewModel.Palette):
                ApplyBackground();
                break;
            case nameof(MiniPlayerViewModel.Look):
                if (!IsVisible)
                    break;
                ApplyLayout();
                ApplyControlsStyle();
                UpdatePlayGlyph();
                ApplyBackground();
                _lyricInk.Invalidate();
                QueueLyrics(animate: false);
                break;
            case nameof(MiniPlayerViewModel.LyricsFontSize):
                if (IsVisible)
                    ApplyVerses();
                break;
            case nameof(MiniPlayerViewModel.WindowWidth):
                // Not a XAML binding: the window writes its HWND's size to Width, which drops one.
                if (!(IsPinned && _pinned is { Width: > 0 }))
                    SetBaseWidth(_vm.WindowWidth);
                break;
            case nameof(MiniPlayerViewModel.StatusMessage):
                StatusText.Visibility = string.IsNullOrEmpty(_vm.StatusMessage) ? Visibility.Collapsed : Visibility.Visible;
                LyricsList.Visibility = string.IsNullOrEmpty(_vm.StatusMessage) ? Visibility.Visible : Visibility.Collapsed;
                QueueLyrics(animate: false);
                break;
        }
    }

    /// <summary>
    /// Album gradient mode: a diagonal gradient (accent top-left, dark bottom-right) with
    /// white text regardless of the app theme. Otherwise the regular theme brushes.
    /// </summary>
    private void ApplyBackground()
    {
        var keys = new[] { "TextPrimaryBrush", "TextSecondaryBrush", "TextTertiaryBrush", "TrackBrush", "SubtleFillBrush", "SubtleFillHoverBrush", "WindowBackgroundBrush" };
        var look = _vm.Look.Background;
        // Full screen: always a backdrop from the cover (the player's own, if it is one).
        if (_presenting && !MiniPlayerBackdrop.IsAtmospheric(look))
            look = MiniPlayerBackground.Aurora;
        var useGradient = _presenting || look switch
        {
            MiniPlayerBackground.Theme => false,
            MiniPlayerBackground.AlbumGradient => true,
            MiniPlayerBackground.Default => _useGradient(),
            _ => true,
        };
        if (!useGradient)
        {
            foreach (var key in keys)
                Resources.Remove(key);
            ContentCard.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "PopupBackgroundBrush");
            return;
        }

        var (start, end) = _vm.Palette;
        if (MiniPlayerBackdrop.IsAtmospheric(look))
        {
            // A small image made once per cover and style, stretched: it is all soft gradients.
            var (w, h) = _presenting ? (192, 108) : _vm.IsCinema ? (192, 112) : (128, 176);
            var image = MiniPlayerBackdrop.Render(_vm.Cover as BitmapSource, look, w, h, BackdropTuning.From(_vm.Look));
            var brush = new ImageBrush(image) { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
            brush.Freeze();
            ContentCard.Background = brush;
        }
        else
        {
            var dim = MiniPlayerStyle.ClampDim(_vm.Look.BackgroundDim);
            var gradient = new LinearGradientBrush(MiniPlayerBackdrop.Dimmed(end, dim), MiniPlayerBackdrop.Dimmed(start, dim), new Point(0, 0), new Point(1, 1));
            gradient.Freeze();
            ContentCard.Background = gradient;
        }
        Resources["TextPrimaryBrush"] = Frozen(Colors.White);
        Resources["TextSecondaryBrush"] = Frozen(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));
        Resources["TextTertiaryBrush"] = Frozen(Color.FromArgb(0x73, 0xFF, 0xFF, 0xFF));
        Resources["TrackBrush"] = Frozen(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF));
        Resources["SubtleFillBrush"] = Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        Resources["SubtleFillHoverBrush"] = Frozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
        Resources["WindowBackgroundBrush"] = Frozen(start);
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static bool Contains(PixelRect r, int x, int y) => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;

    private bool ContainsVolumeButton(int x, int y)
    {
        if (!VolumeButton.IsVisible)
            return false;
        var topLeft = VolumeButton.PointToScreen(new Point());
        var bottomRight = VolumeButton.PointToScreen(new Point(VolumeButton.ActualWidth, VolumeButton.ActualHeight));
        return x >= topLeft.X && x < bottomRight.X && y >= topLeft.Y && y < bottomRight.Y;
    }

    private static readonly Geometry SolidPlay = Geometry.Parse("M7,4.2 L19.6,12 L7,19.8 Z");
    private static readonly Geometry SolidPause = Geometry.Parse("M6.2,4.5 L10.2,4.5 L10.2,19.5 L6.2,19.5 Z M13.8,4.5 L17.8,4.5 L17.8,19.5 L13.8,19.5 Z");
    private static readonly Geometry SolidPrevious = Geometry.Parse("M5,5 L7.6,5 L7.6,19 L5,19 Z M19,5 L19,19 L8.6,12 Z");
    private static readonly Geometry SolidNext = Geometry.Parse("M16.4,5 L19,5 L19,19 L16.4,19 Z M5,5 L15.4,12 L5,19 Z");

    private void UpdatePlayGlyph()
    {
        if (_vm.Look.Controls == ControlsLook.Solid)
        {
            PlayPauseButton.Content = Solid(_vm.IsPlaying ? SolidPause : SolidPlay, 26);
            return;
        }
        if (PlayPauseButton.Content != PlayPauseGlyph)
            PlayPauseButton.Content = PlayPauseGlyph;
        PlayPauseGlyph.Text = _vm.IsPlaying ? "" : "";
    }

    /// <summary>A solid vector icon in the text colour (it follows the background's).</summary>
    private static FrameworkElement Solid(Geometry shape, double size)
    {
        var path = new System.Windows.Shapes.Path { Data = shape, Stretch = Stretch.Uniform };
        path.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "TextPrimaryBrush");
        return new Viewbox { Width = size, Height = size, Child = new Grid { Width = 24, Height = 24, Children = { path } } };
    }

    /// <summary>Outline icons with play in a circle, or solid ones with a large bare play.</summary>
    private void ApplyControlsStyle()
    {
        var solid = _vm.Look.Controls == ControlsLook.Solid;
        PlayPauseButton.Style = (Style)FindResource(solid ? "SolidPlayButton" : "PlayButton");
        PreviousButton.Content = solid ? Solid(SolidPrevious, 18) : "";
        NextButton.Content = solid ? Solid(SolidNext, 18) : "";
    }

    /// <summary>
    /// Vertical: song, lyrics, progress, controls, one under the other. Cinema wide: a large
    /// cover with the song on the left, the lyrics on the right, progress and controls across.
    /// Same parts, other places.
    /// </summary>
    private void ApplyLayout()
    {
        ResetPresentation();
        var cinema = _vm.IsCinema;
        LayoutRoot.RowDefinitions.Clear();
        LayoutRoot.ColumnDefinitions.Clear();
        HeaderPart.RowDefinitions.Clear();
        HeaderPart.ColumnDefinitions.Clear();
        var rows = cinema ? 4 : 5;
        for (var i = 0; i < rows; i++)
            LayoutRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (cinema)
        {
            LayoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(196) });
            LayoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Place(HeaderPart, 0, 0);
            Place(LyricsPart, 0, 1);
            Place(ProgressPart, 1, 0, span: 2);
            Place(ControlsPart, 2, 0, span: 2);
            Place(ConfirmPart, 3, 0, span: 2);
            // The song: cover on top, title, artist, then the actions.
            for (var i = 0; i < 3; i++)
                HeaderPart.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(CoverPart, 0, 0);
            Place(TitlePart, 1, 0);
            Place(ActionsPart, 2, 0);
            CoverPart.Width = CoverPart.Height = 168;
            CoverPart.Margin = new Thickness(0, 0, 0, 12);
            CoverPart.HorizontalAlignment = HorizontalAlignment.Left;
            TitlePart.Margin = new Thickness(0, 0, 12, 0);
            ActionsPart.Margin = new Thickness(-8, 6, 0, 0);
            ActionsPart.HorizontalAlignment = HorizontalAlignment.Left;
            LyricsPart.Margin = new Thickness(18, 0, 0, 8);
            SetLyricsHeight(IsPinned && _pinned is { LyricsHeight: > 0 } c ? c.LyricsHeight : _vm.ShowCover ? 268 : 200);
            HeaderPart.VerticalAlignment = VerticalAlignment.Top;
        }
        else
        {
            LayoutRoot.ColumnDefinitions.Add(new ColumnDefinition());
            Place(HeaderPart, 0, 0);
            Place(LyricsPart, 1, 0);
            Place(ProgressPart, 2, 0);
            Place(ControlsPart, 3, 0);
            Place(ConfirmPart, 4, 0);
            HeaderPart.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            HeaderPart.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            HeaderPart.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Place(CoverPart, 0, 0);
            Place(TitlePart, 0, 1);
            Place(ActionsPart, 0, 2);
            CoverPart.Width = CoverPart.Height = 64;
            CoverPart.Margin = new Thickness(0, 0, 14, 0);
            TitlePart.Margin = new Thickness(0);
            ActionsPart.Margin = new Thickness(8, 0, -6, 0);
            ActionsPart.HorizontalAlignment = HorizontalAlignment.Right;
            LyricsPart.Margin = new Thickness(0, 12, 0, 6);
            SetLyricsHeight(IsPinned && _pinned is { LyricsHeight: > 0 } v ? v.LyricsHeight : 176);
        }
        if (!IsPinned)
            _zoom = 1;
        if (!_gripping)
            SetZoom(_zoom);
        SetBaseWidth(IsPinned && _pinned is { Width: > 0 } w ? Math.Clamp(w.Width, PinnedWidthRange(cinema).Min, PinnedWidthRange(cinema).Max) : _vm.WindowWidth);
        AdaptToSize();
        if (_presenting && IsPinned)
            ApplyPresentation();
    }

    private static void Place(UIElement element, int row, int column, int span = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, span);
        Grid.SetRowSpan(element, 1);
    }

    // The lyrics' height as laid out (or set with the grip); while the floating toolbar shows, it
    // takes ToolbarRoom of it (eased), so the window keeps its size and the verses get the room back.
    private double _lyricsBase = 176;
    private const double ToolbarRoom = 30;

    private Thickness _lyricsMargin;

    /// <summary>From the layout: the lyrics' height and margin as laid out.</summary>
    private void SetLyricsHeight(double height)
    {
        _lyricsMargin = LyricsPart.Margin;
        _lyricsBase = Verses > 0 ? VersesHeight(Verses) : height;
        ApplyToolbarRoom();
    }

    /// <summary>Floating with a set number of verses (0: free, as the grip left it; maximized and full screen use the screen).</summary>
    private int Verses => IsPinned && !_presenting && _pinned is { Verses: > 0 } o ? o.Verses : 0;

    /// <summary>Room for <paramref name="verses"/> lines of the current type, plus a little for the fades at the edges.</summary>
    private double VersesHeight(int verses)
    {
        var line = _vm.MotionLineHeight;
        var padding = _vm.LyricsLinePadding.Top + _vm.LyricsLinePadding.Bottom;
        return Math.Round(Math.Max(60, (verses * (line + padding)) + (line * (verses == 1 ? 0.35 : 0.6))));
    }

    /// <summary>The type size changed (A-, A+, a look): a set number of verses keeps fitting.</summary>
    private void ApplyVerses()
    {
        if (Verses == 0)
            return;
        var height = VersesHeight(Verses);
        if (Math.Abs(height - _lyricsBase) < 0.5)
            return;
        ResizeLyrics(height);
        AdaptToSize();
        QueueLyrics(animate: false);
    }

    /// <summary>From the grip: only the height changes.</summary>
    private void ResizeLyrics(double height)
    {
        _lyricsBase = height;
        ApplyToolbarRoom();
    }

    private static readonly DependencyProperty ToolbarShownProperty = DependencyProperty.Register(
        "ToolbarShown", typeof(double), typeof(MiniPlayerWindow), new PropertyMetadata(0.0, (d, _) => ((MiniPlayerWindow)d).ApplyToolbarRoom()));

    private double ToolbarShown => (double)GetValue(ToolbarShownProperty);

    private void ApplyToolbarRoom()
    {
        // The buttons float over the player: the lyrics keep all of their room.
        var shown = ToolbarShown;
        var room = 0.0;
        var height = Math.Max(60, _lyricsBase - room);
        LyricsViewport.Height = height;
        LyricsFade.EndPoint = new Point(0, height);
        // Wide: the toolbar sits over the lyrics column, which gives it the room; vertical: over the
        // song's row, so everything moves down by what the lyrics give. Either way the size holds.
        if (_presenting)
        {
            ContentCard.Padding = _presentPadding;
            LyricsPart.Margin = _lyricsMargin;
        }
        else if (_vm.IsCinema)
        {
            ContentCard.Padding = new Thickness(16, 16, 16, 10);
            LyricsPart.Margin = new Thickness(_lyricsMargin.Left, _lyricsMargin.Top + room, _lyricsMargin.Right, _lyricsMargin.Bottom);
        }
        else
        {
            ContentCard.Padding = new Thickness(16, 16 + room, 16, 10);
            LyricsPart.Margin = _lyricsMargin;
        }
        PinnedToolbar.Opacity = shown;
        PinnedToolbar.IsHitTestVisible = shown > 0.5;
        CaptionButtons.Opacity = _presenting ? shown : 1;
        CaptionButtons.IsHitTestVisible = !_presenting || shown > 0.5;
    }

    // Maximized or full screen: the pointer has rested (buttons and cursor hidden) since this long.
    private static readonly TimeSpan PresentIdleAfter = TimeSpan.FromSeconds(2.5);
    private bool _presentIdle;
    private Point _presentPointer = new(double.NaN, double.NaN);
    private DispatcherTimer? _presentIdleTimer;

    /// <summary>A real pointer move (not a layout change under a still pointer): buttons and cursor back, for a while.</summary>
    private void OnPresentPointerMoved(Point at)
    {
        if (!_presenting)
            return;
        if (Math.Abs(at.X - _presentPointer.X) < 2 && Math.Abs(at.Y - _presentPointer.Y) < 2)
            return;
        _presentPointer = at;
        WakePresentation();
    }

    private void WakePresentation()
    {
        _presentIdleTimer ??= CreatePresentIdleTimer();
        _presentIdleTimer.Stop();
        _presentIdleTimer.Start();
        if (!_presentIdle)
            return;
        _presentIdle = false;
        ClearValue(CursorProperty);
        UpdatePinnedToolbar();
    }

    private DispatcherTimer CreatePresentIdleTimer()
    {
        var timer = new DispatcherTimer { Interval = PresentIdleAfter };
        timer.Tick += (_, _) =>
        {
            // Over the buttons the pointer may rest: they stay.
            if (!_presenting || PinnedToolbar.IsMouseOver || CaptionButtons.IsMouseOver)
                return;
            timer.Stop();
            _presentIdle = true;
            Cursor = Cursors.None;
            UpdatePinnedToolbar();
        };
        return timer;
    }

    private void StopPresentIdle()
    {
        _presentIdleTimer?.Stop();
        _presentIdle = false;
        _presentPointer = new Point(double.NaN, double.NaN);
        ClearValue(CursorProperty);
    }

    /// <summary>A window of this app (the settings): the floating player does not jump over it.</summary>
    private static bool IsOwnWindow(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>The hover buttons in the given order (the popup's own two first); moved only when it changed.</summary>
    private void OrderToolbar(IEnumerable<string> order)
    {
        var panel = (Panel)PinnedToolbar.Child;
        var wanted = new List<UIElement> { PopupKeepOpen, PopupFloat };
        foreach (var id in PlayerToolIds.Normalize(order))
        {
            wanted.AddRange(id switch
            {
                PlayerToolIds.Settings => [PinnedSettings],
                PlayerToolIds.Size => [LyricsSmaller, LyricsBigger],
                PlayerToolIds.Maximize => [PinnedMaximize],
                PlayerToolIds.FullScreen => [PinnedFullScreen],
                PlayerToolIds.Switch => [PinnedSwitch],
                PlayerToolIds.ClickThrough => [PinnedClickThrough],
                PlayerToolIds.Lock => [PinnedLock],
                PlayerToolIds.Close => [PinnedClose],
                _ => Array.Empty<UIElement>(),
            });
        }
        if (panel.Children.Cast<UIElement>().SequenceEqual(wanted))
            return;
        panel.Children.Clear();
        foreach (var element in wanted)
            panel.Children.Add(element);
    }

    /// <summary>The floating toolbar: shown, or (buttons on hover) only while the pointer is over the player.</summary>
    private void UpdatePinnedToolbar()
    {
        var o = _pinned;
        // Popup: on hover only; floating: unless clicks go through, on hover or always (its setting).
        var available = IsPinned ? o is { ClickThrough: false } : !IsPreview;
        PinnedToolbar.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        var popupOnly = IsPinned ? Visibility.Collapsed : Visibility.Visible;
        PopupFloat.Visibility = PopupKeepOpen.Visibility = popupOnly;
        PopupKeepOpen.Content = _keepOpen ? "\uE840" : "\uE718";
        PopupKeepOpen.ToolTip = LocalizationService.Get(_keepOpen ? "KeepOpenOff" : "KeepOpen");
        if (_keepOpen)
            PopupKeepOpen.SetResourceReference(ForegroundProperty, "AccentBrush");
        else
            PopupKeepOpen.ClearValue(ForegroundProperty);
        // The floating player's buttons the user chose to show, in the user's order (the popup keeps all of its own).
        // The popup has the same choices; click-through, lock and close only mean something floating.
        var tools = (IsPinned ? o?.Tools : null) ?? Tools ?? PlayerTools.Default;
        static Visibility Vis(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
        PinnedSwitch.Visibility = Vis(tools.Switch);
        LyricsSmaller.Visibility = LyricsBigger.Visibility = Vis(tools.Size);
        PinnedSettings.Visibility = Vis(tools.Settings);
        PinnedMaximize.Visibility = Vis(tools.Maximize);
        PinnedFullScreen.Visibility = Vis(tools.FullScreen);
        PinnedClickThrough.Visibility = Vis(IsPinned && tools.ClickThrough);
        PinnedLock.Visibility = Vis(IsPinned && tools.Lock);
        PinnedClose.Visibility = Vis(IsPinned && tools.Close);
        // Maximized or full screen, the window buttons beside these restore and close.
        if (_presenting)
            PinnedClose.Visibility = PinnedLock.Visibility = PinnedClickThrough.Visibility = Visibility.Collapsed;
        OrderToolbar(tools.Order);
        ApplyToolbarLook();
        var maximized = _presenting && !_fullScreen;
        var full = _presenting && _fullScreen;
        PinnedMaximize.Content = maximized ? "\uE923" : "\uE922";
        PinnedMaximize.ToolTip = LocalizationService.Get(maximized ? "RestoreSize" : "MaximizeWindow");
        PinnedFullScreen.Content = full ? "\uE73F" : "\uE740";
        PinnedFullScreen.ToolTip = LocalizationService.Get(full ? "ExitFullScreen" : "FullScreen");
        System.Windows.Automation.AutomationProperties.SetName(PinnedMaximize, (string)PinnedMaximize.ToolTip);
        System.Windows.Automation.AutomationProperties.SetName(PinnedFullScreen, (string)PinnedFullScreen.ToolTip);
        System.Windows.Automation.AutomationProperties.SetName(PopupKeepOpen, (string)PopupKeepOpen.ToolTip);
        PinnedSettings.ToolTip = LocalizationService.Get(IsPinned ? "FloatingPlayerSettings" : "MiniPlayerSettings");
        System.Windows.Automation.AutomationProperties.SetName(PinnedSettings, (string)PinnedSettings.ToolTip);
        // Maximized and full screen: like a video player, the buttons come with the pointer and leave when it rests.
        var hover = _presenting ? !_presentIdle : _pointerOver || _gripping || (IsPinned && o is { PlayerButtonsOnHover: false });
        var target = available && hover ? 1.0 : 0.0;
        if (IsPinned && o is { } p)
        {
            PinnedLock.Content = p.Locked ? "\uE72E" : "\uE785";
            PinnedLock.ToolTip = LocalizationService.Get(p.Locked ? "MinimalUnlock" : "MinimalLock");
        }
        var ms = _animationsEnabled() && SystemParameters.ClientAreaAnimation && IsVisible ? (target > ToolbarShown ? 180 : 260) : 0;
        if (ms == 0)
        {
            BeginAnimation(ToolbarShownProperty, null);
            SetValue(ToolbarShownProperty, target);
            return;
        }
        BeginAnimation(ToolbarShownProperty, new DoubleAnimation(ToolbarShown, target, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = ToolbarEase,
            FillBehavior = FillBehavior.HoldEnd,
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private static readonly IEasingFunction ToolbarEase = CreateToolbarEase();

    private static IEasingFunction CreateToolbarEase()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        ease.Freeze();
        return ease;
    }

    private void UpdateProgressValue()
    {
        Progress.Maximum = _vm.DurationSeconds;
        Progress.Value = _vm.PositionSeconds;
    }

    private void OnLyricLineClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LyricLineViewModel line })
        {
            // Jumping to a line follows the song again from there.
            SetBrowsing(false);
            _vm.SeekToLineCommand.Execute(line);
            QueueLyrics(animate: false);
        }
    }

    /// <summary>The wheel scrolls the lyrics freely, like Spotify's lyrics view; they stop following the song.</summary>
    private void OnLyricsWheel(object sender, MouseWheelEventArgs e)
    {
        if (_vm.Lines.Count == 0 || LyricsList.Visibility != Visibility.Visible)
            return;
        e.Handled = true;
        // The animated value, kept when the animation is removed.
        var y = LyricsOffset.Y;
        LyricsOffset.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        // Between the first line's top and the last line's bottom at the middle of the viewport.
        var middle = LyricsViewport.ActualHeight / 2;
        LyricsOffset.Y = Math.Clamp(y + (e.Delta / 2.0), middle - LyricsList.ActualHeight, middle);
        SetBrowsing(true);
    }

    private void OnResync(object sender, RoutedEventArgs e)
    {
        SetBrowsing(false);
        ScrollToCurrent(animate: true);
    }

    private void SetBrowsing(bool browsing)
    {
        _browsing = browsing;
        ResyncButton.Visibility = browsing ? Visibility.Visible : Visibility.Collapsed;
        if (browsing)
            _lyricInk.Invalidate();
    }

    private void QueueLyrics(bool animate)
    {
        if (!IsVisible)
            return;
        _animateLyrics = animate;
        if (_lyricsUpdateQueued)
            return;
        _lyricsUpdateQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _lyricsUpdateQueued = false;
            ScrollToCurrent(_animateLyrics);
        });
    }

    private void QueueLyricInk()
    {
        if (!IsVisible || _vm.FlowMode == LyricFlowMode.Off || _inkUpdateQueued)
            return;
        _inkUpdateQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _inkUpdateQueued = false;
            _lyricInk.Refresh();
        });
    }

    private (TextBlock?, LyricFill, bool) SampleLyricInk()
    {
        if (!IsVisible || _browsing || _vm.FlowMode == LyricFlowMode.Off
            || _activeText?.DataContext is not LyricLineViewModel line || line.Index != _vm.CurrentIndex)
            return (null, LyricFill.Full, false);
        return (_activeText, _vm.CurrentFill, _vm.IsPlaying);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _lyricInk.Invalidate();
        QueueLyrics(animate: false);
    }

    /// <summary>
    /// The list drifted from the current line (an interrupted animation, a layout change after
    /// the last scroll): slide back to it. Not while the user browses or a scroll is running.
    /// </summary>
    private void KeepCentred()
    {
        if (!IsVisible || _browsing || _lyricsUpdateQueued || _vm.Lines.Count == 0 || DateTime.UtcNow < _scrollEndsAt)
            return;
        if (!LyricsList.IsArrangeValid)
            LyricsList.UpdateLayout();
        if (CentreTarget() is { } target && Math.Abs(LyricsOffset.Y - target) > 2)
        {
            Log.Debug($"Mini player lyrics off the current line ({LyricsOffset.Y:0} vs {target:0}, line {_vm.CurrentIndex}): recentred");
            ScrollToCurrent(animate: Math.Abs(LyricsOffset.Y - target) < LyricsViewport.ActualHeight);
        }
    }

    /// <summary>Where the list must sit for the current line to be in the middle; null before layout.</summary>
    private double? CentreTarget()
    {
        var index = _vm.CurrentIndex;
        if (LyricsList.ItemContainerGenerator.ContainerFromIndex(Math.Max(0, index)) is not FrameworkElement container || !container.IsArrangeValid)
            return null;
        var viewport = LyricsViewport.ActualHeight;
        var top = container.TranslatePoint(new Point(0, 0), LyricsList).Y;
        return index < 0
            ? (viewport / 2) - (container.ActualHeight / 2) + container.ActualHeight
            : (viewport / 2) - top - (container.ActualHeight / 2);
    }

    private FrameworkElement? _fittedLine;

    /// <summary>
    /// A long verse never shows cut: the fades at the edges narrow to leave the current line whole,
    /// and a line still taller than the lyrics' room is set smaller (and wider: it rewraps into
    /// fewer lines) until it fits. The others keep their size.
    /// </summary>
    private void FitCurrentLine(FrameworkElement? line, double viewport)
    {
        if (_fittedLine is { } previous && !ReferenceEquals(previous, line))
        {
            previous.LayoutTransform = Transform.Identity;
            _fittedLine = null;
            LyricsList.UpdateLayout();
        }
        if (line is not null && viewport > 0)
        {
            var room = viewport * 0.94;
            var scale = line.LayoutTransform is ScaleTransform s ? s.ScaleX : 1;
            if (line.ActualHeight * scale > room + 0.5 || scale < 1)
            {
                // Measured untransformed: try the size that fits, then check again (rewrapped lines are shorter).
                line.LayoutTransform = Transform.Identity;
                LyricsList.UpdateLayout();
                var natural = line.ActualHeight;
                if (natural > room)
                {
                    var fit = Math.Max(0.5, room / natural);
                    for (var i = 0; i < 3; i++)
                    {
                        line.LayoutTransform = new ScaleTransform(fit, fit);
                        LyricsList.UpdateLayout();
                        var height = line.ActualHeight * fit;
                        if (height <= room + 0.5)
                        {
                            // Rewrapped into fewer lines: grow back as much as still fits.
                            var grown = Math.Min(1, fit * Math.Sqrt(room / Math.Max(1, height)));
                            if (grown > fit + 0.01)
                            {
                                line.LayoutTransform = new ScaleTransform(grown, grown);
                                LyricsList.UpdateLayout();
                                if (line.ActualHeight * grown > room + 0.5)
                                {
                                    line.LayoutTransform = new ScaleTransform(fit, fit);
                                    LyricsList.UpdateLayout();
                                }
                            }
                            break;
                        }
                        fit = Math.Max(0.5, fit * room / height);
                    }
                    _fittedLine = line;
                }
            }
        }

        // The fades take at most what the current line leaves free.
        var lineHeight = line is null ? 0 : line.ActualHeight * (line.LayoutTransform is ScaleTransform t ? t.ScaleX : 1);
        var edge = viewport > 0 ? Math.Clamp((viewport - lineHeight) / 2 / viewport, 0.02, 0.16) : 0.16;
        if (LyricsFade.GradientStops.Count == 4 && Math.Abs(LyricsFade.GradientStops[1].Offset - edge) > 0.002)
        {
            if (LyricsFade.IsFrozen)
                return;
            LyricsFade.GradientStops[1].Offset = edge;
            LyricsFade.GradientStops[2].Offset = 1 - edge;
        }
    }

    /// <summary>Translate the list so the current line is vertically centred in the viewport.</summary>
    private void ScrollToCurrent(bool animate)
    {
        if (!IsVisible || _vm.Lines.Count == 0 || _browsing)
            return;

        var viewport = LyricsViewport.ActualHeight;
        double target;
        var index = _vm.CurrentIndex;
        // The lines' layout must be current, or the target is computed from stale positions.
        if (!LyricsList.IsArrangeValid)
            LyricsList.UpdateLayout();
        var container = LyricsList.ItemContainerGenerator.ContainerFromIndex(Math.Max(0, index)) as FrameworkElement;
        if (container is null)
            return;
        FitCurrentLine(index >= 0 ? container : null, viewport);

        var top = container.TranslatePoint(new Point(0, 0), LyricsList).Y;
        target = index < 0
            ? (viewport / 2) - (container.ActualHeight / 2) + container.ActualHeight // first line just below centre
            : (viewport / 2) - top - (container.ActualHeight / 2);

        var duration = animate && _animationsEnabled() && SystemParameters.ClientAreaAnimation
            ? (_vm.ScrollMilliseconds > 0 ? _vm.ScrollMilliseconds : 240)
            : 0;
        _scrollEndsAt = DateTime.UtcNow + TimeSpan.FromMilliseconds(duration + 80);
        LyricMotionText.Tween(LyricsOffset, TranslateTransform.YProperty, target, duration, _vm.ExpressiveMotion);
        if (_vm.FlowMode != LyricFlowMode.Off && index >= 0 && container is ContentPresenter presenter
            && VisualTreeHelper.GetChildrenCount(presenter) > 0)
            _activeText = VisualTreeHelper.GetChild(presenter, 0) as TextBlock;
        else
            _activeText = null;
        _lyricInk.Refresh();
    }
}

/// <summary>How the floating player behaves (Minimal Mode's settings) and where its changes go.</summary>
public sealed record PinnedOptions(bool ClickThrough, bool Locked, bool Topmost, bool ShowMark, bool AutoFit, bool ControlsOnHover, bool PlayerButtonsOnHover,
    int Width, int LyricsHeight, double Zoom, Action<bool> SetClickThrough, Action<PixelRect, int, int, double> Saved, Action<bool> SetLocked, Action TurnOff, Action SwitchMode, PlayerTools? Tools = null, int Verses = 0);

/// <summary>Which of the floating player's own buttons are shown.</summary>
public sealed record PlayerTools(bool Switch, bool Size, bool Maximize, bool FullScreen, bool Settings, bool ClickThrough, bool Lock, bool Close, IReadOnlyList<string> Order)
{
    public static readonly PlayerTools Default = new(true, true, true, true, true, true, true, true, PlayerToolIds.DefaultOrder);
}
