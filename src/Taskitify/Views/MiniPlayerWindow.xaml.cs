using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Taskitify.Core.Layout;
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
    private bool _dragging;

    public MiniPlayerWindow(MiniPlayerViewModel vm, Func<bool> animationsEnabled, Func<bool> useGradient)
    {
        InitializeComponent();
        _vm = vm;
        _animationsEnabled = animationsEnabled;
        _useGradient = useGradient;
        DataContext = vm;

        _outsideClicks = new OutsideClickWatcher((x, y) =>
        {
            var inside = WindowInterop.GetBounds(_hwnd);
            // Clicks on the overlay toggle the popup themselves.
            if (!Contains(inside, x, y) && !Contains(_anchor, x, y))
                Dispatcher.BeginInvoke(HidePopup);
        });
        Closed += (_, _) => _outsideClicks.Dispose();

        vm.PropertyChanged += OnViewModelChanged;
        vm.CurrentLineChanged += (_, _) => Dispatcher.InvokeAsync(() => ScrollToCurrent(animate: true), DispatcherPriority.Loaded);
        vm.Lines.CollectionChanged += (_, _) => Dispatcher.InvokeAsync(() => ScrollToCurrent(animate: false), DispatcherPriority.Loaded);

        Progress.PreviewMouseLeftButtonDown += (_, _) => _dragging = true;
        Progress.PreviewMouseLeftButtonUp += (_, _) =>
        {
            _dragging = false;
            _vm.SeekTo(Progress.Value);
        };
        Progress.LostMouseCapture += (_, _) => _dragging = false;

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
        IsVisibleChanged += (_, _) => RenderOptions.ProcessRenderMode =
            IsVisible ? System.Windows.Interop.RenderMode.Default : System.Windows.Interop.RenderMode.SoftwareOnly;
    }

    /// <summary>When the popup last hid; lets the overlay click that caused it not re-open it.</summary>
    public DateTime LastHidden { get; private set; }

    public void ShowAt(PixelRect anchor, PixelRect workArea, double scale)
    {
        _vm.IsActive = true;
        UpdatePlayGlyph();
        UpdateProgressValue();
        OnViewModelChanged(this, new PropertyChangedEventArgs(nameof(MiniPlayerViewModel.StatusMessage)));
        ApplyBackground();

        // Remember who had focus so closing with Esc gives it back.
        _previousForeground = NativeMethods.GetForegroundWindow();

        Opacity = 0;
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
        _outsideClicks.Start();
        FocusHelper.ForceForeground(_hwnd);
        Activate();
        ScrollToCurrent(animate: false);

        if (_animationsEnabled())
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            ChromeOffset.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        }
        else
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
        }
    }

    public void HidePopup()
    {
        if (!IsVisible)
            return;
        LastHidden = DateTime.UtcNow;
        _vm.IsActive = false;
        _outsideClicks.Stop();
        var restoreFocus = NativeMethods.GetForegroundWindow() == _hwnd;
        Hide();
        if (restoreFocus && _previousForeground != IntPtr.Zero && _previousForeground != _hwnd)
            NativeMethods.SetForegroundWindow(_previousForeground);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        HidePopup();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Escape:
                HidePopup();
                break;
            case Key.Space:
                _vm.PlayPauseCommand.Execute(null);
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
            case nameof(MiniPlayerViewModel.StatusMessage):
                StatusText.Visibility = string.IsNullOrEmpty(_vm.StatusMessage) ? Visibility.Collapsed : Visibility.Visible;
                LyricsList.Visibility = string.IsNullOrEmpty(_vm.StatusMessage) ? Visibility.Visible : Visibility.Collapsed;
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
        if (!_useGradient())
        {
            foreach (var key in keys)
                Resources.Remove(key);
            ContentCard.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "PopupBackgroundBrush");
            return;
        }

        var (start, end) = _vm.Palette;
        var gradient = new LinearGradientBrush(end, start, new Point(0, 0), new Point(1, 1));
        gradient.Freeze();
        ContentCard.Background = gradient;
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

    private void UpdatePlayGlyph() => PlayPauseGlyph.Text = _vm.IsPlaying ? "" : "";

    private void UpdateProgressValue()
    {
        if (!_dragging)
            Progress.Value = _vm.PositionSeconds;
    }

    private void OnLyricLineClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LyricLineViewModel line })
            _vm.SeekToLineCommand.Execute(line);
    }

    /// <summary>Translate the list so the current line is vertically centred in the viewport.</summary>
    private void ScrollToCurrent(bool animate)
    {
        if (!IsVisible || _vm.Lines.Count == 0)
            return;

        var viewport = LyricsViewport.ActualHeight;
        double target;
        var index = _vm.CurrentIndex;
        var container = LyricsList.ItemContainerGenerator.ContainerFromIndex(Math.Max(0, index)) as FrameworkElement;
        if (container is null)
            return;

        var top = container.TranslatePoint(new Point(0, 0), LyricsList).Y;
        target = index < 0
            ? (viewport / 2) - (container.ActualHeight / 2) + container.ActualHeight // first line just below centre
            : (viewport / 2) - top - (container.ActualHeight / 2);

        if (animate && _animationsEnabled())
        {
            LyricsOffset.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(target, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        else
        {
            LyricsOffset.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
            LyricsOffset.Y = target;
        }
    }
}
