using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Taskitify.Core.Imaging;
using Taskitify.Core.Layout;
using Taskitify.Core.Lyrics;
using Taskitify.Infrastructure;
using Taskitify.Models;
using Taskitify.Services;
using Taskitify.ViewModels;

namespace Taskitify.Views;

/// <summary>The colours and effects Minimal Mode draws with (the window, its picker pictures, the tour).</summary>
internal static class MinimalInk
{
    /// <summary>The lyrics' colour for the chosen option and the song's palette.</summary>
    public static Color Text(MinimalTextColor choice, (Color Start, Color End) palette)
    {
        switch (choice)
        {
            case MinimalTextColor.Warm:
                return Color.FromRgb(0xF6, 0xEC, 0xD9);
            case MinimalTextColor.Dark:
                return Color.FromRgb(0x16, 0x18, 0x1D);
            case MinimalTextColor.Album:
                var (h, s, l) = AlbumPalette.ToHsl(new Rgb(palette.Start.R, palette.Start.G, palette.Start.B));
                var light = AlbumPalette.FromHsl(h, Math.Min(s, 0.75), Math.Max(l, 0.8));
                return Color.FromRgb(light.R, light.G, light.B);
            default:
                return Colors.White;
        }
    }

    public static bool IsDark(Color c) => (0.2126 * c.R) + (0.7152 * c.G) + (0.0722 * c.B) < 128;

    /// <summary>A shadow or a tight halo in the opposite tone of the text; null for none.</summary>
    public static Effect? Legibility(MinimalLegibility kind, Color text)
    {
        if (kind == MinimalLegibility.None)
            return null;
        var halo = IsDark(text) ? Colors.White : Colors.Black;
        var effect = kind == MinimalLegibility.Outline
            ? new DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.95, Color = halo, RenderingBias = RenderingBias.Quality }
            : new DropShadowEffect { BlurRadius = 12, ShadowDepth = 1.5, Direction = 270, Opacity = 0.7, Color = halo };
        effect.Freeze();
        return effect;
    }

    /// <summary>The background behind the lyrics (null for a clear one); dark text gets a light panel.</summary>
    public static Brush? Backdrop(MinimalBackdrop kind, double opacity, BitmapSource? cover, (Color Start, Color End) palette, Color text)
    {
        Brush brush;
        switch (kind)
        {
            case MinimalBackdrop.Clear:
                return null;
            case MinimalBackdrop.Album:
                var image = new ImageBrush(MiniPlayerBackdrop.Render(cover, MiniPlayerBackground.Aurora, 160, 64)) { Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                brush = image;
                break;
            default:
                brush = new SolidColorBrush(IsDark(text) ? Color.FromRgb(0xF4, 0xF2, 0xEE) : Color.FromRgb(0x0C, 0x10, 0x16));
                break;
        }
        brush.Opacity = Math.Clamp(opacity, 0, 1);
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Minimal Mode: the lyrics floating over the desktop. One instance, shown and hidden; it can be
/// dragged and resized anywhere (unless locked), stay above other windows and let clicks through.
/// </summary>
public partial class MinimalLyricsWindow : Window
{
    // Generous edges: easy to grab, and the corner grip is larger still.
    private const int ResizeBorder = 10;
    private const int GripSize = 24;
    private readonly MinimalLyricsViewModel _vm;
    private readonly SettingsService _settings;
    private readonly ISpotifyMonitor _monitor;
    private readonly LyricInkDriver _lyricInk;
    private IntPtr _hwnd;
    private bool _lyricsQueued;
    private bool _animateLyrics;
    private bool _inkQueued;
    private TextBlock? _activeText;
    private ForegroundWatcher? _foreground;
    private DispatcherTimer? _topmostAgain;
    // What the window's height was fitted to; the user's own resizing is kept until one of these changes.
    private (MinimalLines, double, double, bool, bool)? _fittedTo;

    public MinimalLyricsWindow(MinimalLyricsViewModel vm, SettingsService settings, ISpotifyMonitor monitor)
    {
        InitializeComponent();
        _vm = vm;
        _settings = settings;
        _monitor = monitor;
        DataContext = vm;
        _lyricInk = new LyricInkDriver(TimeSpan.FromMilliseconds(1000d / 30), SampleLyricInk);

        vm.CurrentLineChanged += (_, _) => QueueLyrics(vm.ScrollMilliseconds > 0);
        vm.LyricPositionChanged += (_, _) => QueueLyricInk();
        vm.LookChanged += (_, _) => ApplyLook();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MinimalLyricsViewModel.StatusMessage) or "")
                UpdateStatus();
        };
        vm.Lines.CollectionChanged += (_, change) =>
        {
            if (change.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                _activeText = null;
                _lyricInk.Invalidate();
            }
            QueueLyrics(animate: false);
        };
        LyricsViewport.SizeChanged += (_, _) =>
        {
            var height = Math.Max(1, LyricsViewport.ActualHeight);
            LyricsFade.EndPoint = new Point(0, height);
            // A short fade at each edge: about a third of a verse, whatever the window's height.
            var edge = Math.Clamp(_vm.LyricsFontSize * .45 / height, 0.02, 0.2);
            LyricsFade.GradientStops[1].Offset = edge;
            LyricsFade.GradientStops[2].Offset = 1 - edge;
            QueueLyrics(animate: false);
        };

        Root.MouseLeftButtonDown += (_, e) =>
        {
            if (_settings.Current.Minimal.Locked || e.OriginalSource is DependencyObject source && IsInButton(source))
                return;
            try { DragMove(); }
            catch (InvalidOperationException) { }
        };
        Root.MouseEnter += (_, _) => Toolbar.Opacity = ResizeGrip.Opacity = 1;
        Root.MouseLeave += (_, _) => Toolbar.Opacity = ResizeGrip.Opacity = 0.6;

        SourceInitialized += (_, _) =>
        {
            _hwnd = WindowInterop.EnsureHandle(this);
            // Never takes the focus from the app being used, and stays out of Alt+Tab.
            WindowInterop.MakeToolWindow(_hwnd, noActivate: true);
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                GpuRendering.Acquire();
                _monitor.ThumbnailChanged += OnCoverChanged;
                UpdateTopmostWatch();
            }
            else
            {
                _monitor.ThumbnailChanged -= OnCoverChanged;
                UpdateTopmostWatch();
                _lyricInk.Invalidate();
                _activeText = null;
                LyricsOffset.BeginAnimation(TranslateTransform.YProperty, null);
                GpuRendering.Release();
            }
        };
    }

    /// <summary>Shown only while its look is being changed in the settings (Minimal Mode itself is off).</summary>
    public bool IsPreview { get; private set; }

    /// <summary>Shows the lyrics where they were left (or somewhere visible, if that place is gone).</summary>
    public void ShowLyrics(bool preview)
    {
        IsPreview = preview;
        if (IsVisible)
            return;
        _vm.IsActive = true;
        _hwnd = WindowInterop.EnsureHandle(this);
        ApplyLook();
        Place();
        Show();
        // Showing a window that never had a position lets Windows pick one: put it back.
        Place();
        UpdateLayout();
        QueueLyrics(animate: false);
    }

    public void HideLyrics()
    {
        IsPreview = false;
        if (!IsVisible)
            return;
        SaveBounds();
        Hide();
        _vm.IsActive = false;
    }

    /// <summary>Back to the default place and size (the settings' and the tray's "reset").</summary>
    public void ResetPlacement()
    {
        _settings.Update(s => s.Minimal.Width = s.Minimal.Height = s.Minimal.X = s.Minimal.Y = 0);
        _fittedTo = null;
        if (IsVisible)
            Place();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Alt+F4 turns Minimal Mode off; the single instance stays.
        if (Application.Current is App { IsShuttingDown: false })
        {
            e.Cancel = true;
            _settings.Update(s => s.Minimal.Enabled = false);
        }
        base.OnClosing(e);
    }

    private static bool IsInButton(DependencyObject source)
    {
        for (var node = source; node is not null; node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : null)
        {
            if (node is System.Windows.Controls.Primitives.ButtonBase)
                return true;
        }
        return false;
    }

    private void OnCoverChanged(object? sender, EventArgs e) => ApplyLook();

    /// <summary>Colours, background, legibility, the window's behaviour and its fitted height.</summary>
    private void ApplyLook()
    {
        if (_hwnd == IntPtr.Zero)
            return;
        var s = _settings.Current.Minimal;
        var ink = MinimalInk.Text(s.TextColor, _monitor.Palette);
        Resources["TextPrimaryBrush"] = Frozen(ink);
        Resources["TextSecondaryBrush"] = Frozen(Color.FromArgb(0xC0, ink.R, ink.G, ink.B));
        var fill = MinimalInk.IsDark(ink) ? Colors.Black : Colors.White;
        Resources["SubtleFillBrush"] = Frozen(Color.FromArgb(0x18, fill.R, fill.G, fill.B));
        Resources["SubtleFillHoverBrush"] = Frozen(Color.FromArgb(0x2A, fill.R, fill.G, fill.B));
        Backdrop.Background = MinimalInk.Backdrop(s.Backdrop, s.BackdropOpacity, _monitor.Thumbnail as BitmapSource, _monitor.Palette, ink);
        var effect = MinimalInk.Legibility(s.Legibility, ink);
        LyricsLayer.Effect = effect;
        StatusText.Effect = effect;
        SongText.Effect = effect;
        Toolbar.Effect = effect;
        ControlsRow.Effect = effect;

        Topmost = s.Topmost;
        UpdateTopmostWatch();
        Cursor = s.Locked ? null : Cursors.SizeAll;
        ResizeGrip.Visibility = s.Locked || s.ClickThrough ? Visibility.Collapsed : Visibility.Visible;
        // Clicks through: nothing to click, so the buttons go too (the tray and the settings turn it off).
        Toolbar.Visibility = s.ClickThrough ? Visibility.Collapsed : Visibility.Visible;
        if (_hwnd != IntPtr.Zero)
        {
            var ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
            var wanted = s.ClickThrough ? ex | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED : ex & ~NativeMethods.WS_EX_TRANSPARENT;
            if (wanted != ex)
                NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, wanted);
        }

        var fit = (s.Lines, s.FontSize, s.LineSpacing, s.ShowSong, s.ShowControls);
        if (IsVisible && _fittedTo is { } was && was != fit)
            FitHeight();
        _fittedTo = fit;
        _lyricInk.Invalidate();
        QueueLyrics(animate: false);
    }

    /// <summary>
    /// Games and full-screen apps often make themselves topmost when they come to the front, which
    /// puts them above the lyrics. While the lyrics stay on top, each change of foreground window
    /// (an event, no polling) puts them back above, at once and again a moment later, without
    /// taking the focus. Exclusive full screen (a game owning the display) shows no window at all.
    /// </summary>
    private void UpdateTopmostWatch()
    {
        var watch = IsVisible && _settings.Current.Minimal.Topmost;
        if (watch && _foreground is null)
        {
            _topmostAgain = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(600) };
            _topmostAgain.Tick += (_, _) =>
            {
                _topmostAgain!.Stop();
                RaiseAbove();
            };
            _foreground = new ForegroundWatcher(hwnd =>
            {
                if (hwnd == _hwnd)
                    return;
                RaiseAbove();
                _topmostAgain?.Stop();
                _topmostAgain?.Start();
            });
        }
        else if (!watch && _foreground is not null)
        {
            _foreground.Dispose();
            _foreground = null;
            _topmostAgain?.Stop();
            _topmostAgain = null;
        }
    }

    private void RaiseAbove()
    {
        if (_hwnd != IntPtr.Zero && IsVisible && _settings.Current.Minimal.Topmost)
            WindowInterop.BringToTopmost(_hwnd);
    }

    private void UpdateStatus()
    {
        var status = !string.IsNullOrEmpty(_vm.StatusMessage);
        StatusText.Visibility = status ? Visibility.Visible : Visibility.Collapsed;
        LyricsLayer.Visibility = status ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The height the chosen verses need (DIPs): the lyrics, plus the song and the controls when shown.</summary>
    private double IdealHeight()
    {
        var s = _settings.Current.Minimal;
        var (before, after) = s.Around;
        var verse = (_vm.LineHeight * 1.25) + _vm.LinePadding.Top + _vm.LinePadding.Bottom;
        return 14 + 30 + ((before + after + 1) * verse) + (s.ShowControls ? 40 : 0) + 12;
    }

    private void FitHeight()
    {
        var bounds = WindowInterop.GetBounds(_hwnd);
        var scale = WindowInterop.GetScale(_hwnd);
        var height = (int)Math.Round(IdealHeight() * scale);
        // Grows or shrinks around its middle, so the verse being sung stays where the eye is.
        var y = bounds.Y + ((bounds.Height - height) / 2);
        SetBounds(new PixelRect(bounds.X, y, bounds.Width, height));
        SaveBounds();
    }

    private void Place()
    {
        var s = _settings.Current.Minimal;
        var monitors = TaskbarPositionService.GetMonitors().OrderByDescending(m => m.IsPrimary).Select(m => m.WorkArea).ToList();
        var scale = monitors.Count > 0 ? WindowInterop.GetScale(_hwnd) : 1;
        var saved = new PixelRect(s.X, s.Y, s.Width, s.Height);
        var place = FloatingPlacement.Resolve(saved, monitors, ((int)Math.Round(720 * scale), (int)Math.Round(IdealHeight() * scale)));
        SetBounds(place);
        _fittedTo = (s.Lines, s.FontSize, s.LineSpacing, s.ShowSong, s.ShowControls);
        if (place != saved)
            SaveBounds(place);
    }

    private void SetBounds(PixelRect r) =>
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, r.X, r.Y, r.Width, r.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOOWNERZORDER);

    private void SaveBounds(PixelRect? bounds = null)
    {
        if (_hwnd == IntPtr.Zero)
            return;
        var r = bounds ?? WindowInterop.GetBounds(_hwnd);
        var s = _settings.Current.Minimal;
        if (r.IsEmpty || (s.X == r.X && s.Y == r.Y && s.Width == r.Width && s.Height == r.Height))
            return;
        _settings.Update(x => (x.Minimal.X, x.Minimal.Y, x.Minimal.Width, x.Minimal.Height) = (r.X, r.Y, r.Width, r.Height));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            // The edges resize the window (Windows does the rest, smoothly); not when locked.
            case NativeMethods.WM_NCHITTEST when !_settings.Current.Minimal.Locked:
                var x = (short)(lParam.ToInt64() & 0xFFFF);
                var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                var r = WindowInterop.GetBounds(hwnd);
                var scale = WindowInterop.GetScale(hwnd);
                var edge = (int)Math.Round(ResizeBorder * scale);
                var grip = (int)Math.Round(GripSize * scale);
                if (!_settings.Current.Minimal.ClickThrough && x >= r.Right - grip && y >= r.Bottom - grip)
                {
                    handled = true;
                    return new IntPtr(NativeMethods.HTBOTTOMRIGHT);
                }
                var left = x < r.Left + edge;
                var right = x >= r.Right - edge;
                var top = y < r.Top + edge;
                var bottom = y >= r.Bottom - edge;
                var hit = (top, bottom, left, right) switch
                {
                    (true, _, true, _) => NativeMethods.HTTOPLEFT,
                    (true, _, _, true) => NativeMethods.HTTOPRIGHT,
                    (_, true, true, _) => NativeMethods.HTBOTTOMLEFT,
                    (_, true, _, true) => NativeMethods.HTBOTTOMRIGHT,
                    (true, _, _, _) => NativeMethods.HTTOP,
                    (_, true, _, _) => NativeMethods.HTBOTTOM,
                    (_, _, true, _) => NativeMethods.HTLEFT,
                    (_, _, _, true) => NativeMethods.HTRIGHT,
                    _ => 0,
                };
                if (hit != 0)
                {
                    handled = true;
                    return new IntPtr(hit);
                }
                break;
            case NativeMethods.WM_EXITSIZEMOVE:
                SaveBounds();
                break;
            case NativeMethods.WM_DISPLAYCHANGE:
                // A monitor gone or a new resolution: bring the lyrics back on screen if needed.
                Dispatcher.InvokeAsync(() => { if (IsVisible) Place(); }, DispatcherPriority.Background);
                break;
        }
        return IntPtr.Zero;
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    // ---- The lyrics' position and Lyric Flow (as in the mini player) ----

    private void QueueLyrics(bool animate)
    {
        if (!IsVisible)
            return;
        _animateLyrics = animate;
        if (_lyricsQueued)
            return;
        _lyricsQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _lyricsQueued = false;
            ScrollToCurrent(_animateLyrics);
        });
    }

    private void QueueLyricInk()
    {
        if (!IsVisible || _vm.FlowMode == LyricFlowMode.Off || _inkQueued)
            return;
        _inkQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _inkQueued = false;
            _lyricInk.Refresh();
        });
    }

    private (TextBlock?, LyricFill, bool) SampleLyricInk()
    {
        if (!IsVisible || _vm.FlowMode == LyricFlowMode.Off
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
    /// Slides the list so the current verse sits in the middle, or, showing it with the next one
    /// only, so the two together do; before the first verse, the first ones wait in their places.
    /// </summary>
    private void ScrollToCurrent(bool animate)
    {
        if (!IsVisible || _vm.Lines.Count == 0)
            return;
        if (!LyricsList.IsArrangeValid)
            LyricsList.UpdateLayout();
        var index = _vm.CurrentIndex;
        var (before, after) = _settings.Current.Minimal.Around;
        var first = Math.Max(0, index);
        if (LyricsList.ItemContainerGenerator.ContainerFromIndex(first) is not FrameworkElement container)
            return;
        var top = container.TranslatePoint(new Point(0, 0), LyricsList).Y;
        var bottom = top + container.ActualHeight;
        if (index < 0)
        {
            // Not started: the first verses where they will be once it does (the first one current).
            var lastShown = Math.Min(_vm.Lines.Count - 1, before + after);
            if (LyricsList.ItemContainerGenerator.ContainerFromIndex(lastShown) is FrameworkElement last)
                bottom = last.TranslatePoint(new Point(0, last.ActualHeight), LyricsList).Y;
        }
        else if (after > before && index + 1 < _vm.Lines.Count
            && LyricsList.ItemContainerGenerator.ContainerFromIndex(index + 1) is FrameworkElement next)
        {
            bottom = next.TranslatePoint(new Point(0, next.ActualHeight), LyricsList).Y;
        }
        var target = (LyricsViewport.ActualHeight / 2) - ((top + bottom) / 2);
        var duration = animate && _settings.Current.EnableAnimations && SystemParameters.ClientAreaAnimation
            ? (_vm.ScrollMilliseconds > 0 ? _vm.ScrollMilliseconds : 240)
            : 0;
        LyricMotionText.Tween(LyricsOffset, TranslateTransform.YProperty, target, duration, _vm.ExpressiveMotion);
        _activeText = _vm.FlowMode != LyricFlowMode.Off && index >= 0 && container is ContentPresenter presenter
            && VisualTreeHelper.GetChildrenCount(presenter) > 0
            ? VisualTreeHelper.GetChild(presenter, 0) as TextBlock
            : null;
        _lyricInk.Refresh();
    }
}
