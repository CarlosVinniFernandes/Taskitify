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
            Dispatcher.BeginInvoke(HidePopup);
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

    public void ShowAt(PixelRect anchor, PixelRect workArea, double scale, bool preview = false)
    {
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

    public void HidePopup()
    {
        IsPreview = false;
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
        // A preview stays while the settings are used; a menu of its own does not close it either.
        if (IsPreview || ContentCard.ContextMenu is { IsOpen: true } || VolumePopup.Child is { IsKeyboardFocusWithin: true })
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
            case nameof(MiniPlayerViewModel.WindowWidth):
                // Not a XAML binding: the window writes its HWND's size to Width, which drops one.
                Width = _vm.WindowWidth;
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
        var useGradient = look switch
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
            var (w, h) = _vm.IsCinema ? (192, 112) : (128, 176);
            var image = MiniPlayerBackdrop.Render(_vm.Cover as BitmapSource, look, w, h);
            var brush = new ImageBrush(image) { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
            brush.Freeze();
            ContentCard.Background = brush;
        }
        else
        {
            var gradient = new LinearGradientBrush(end, start, new Point(0, 0), new Point(1, 1));
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
            SetLyricsHeight(_vm.ShowCover ? 268 : 200);
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
            SetLyricsHeight(176);
        }
        Width = _vm.WindowWidth;
    }

    private static void Place(UIElement element, int row, int column, int span = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, span);
        Grid.SetRowSpan(element, 1);
    }

    private void SetLyricsHeight(double height)
    {
        LyricsViewport.Height = height;
        LyricsFade.EndPoint = new Point(0, height);
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
