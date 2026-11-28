using System.Windows;
using Taskitify.ViewModels;
using Taskitify.Services;
using System.Windows.Controls;

namespace Taskitify.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    private readonly SpotifyLyricsSession _spotify;

    /// <summary>Opens the given page (the mini player's menu goes straight to its own).</summary>
    public void ShowPage(SettingsPage page) => Pages.SelectedIndex = (int)page;

    public SettingsWindow(SettingsViewModel vm, SpotifyLyricsSession spotify)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        _spotify = spotify;
        _spotify.StatusChanged += OnSpotifyStatus;
        Loaded += (_, _) => UpdatePlayerPreviews();
        Pages.SelectionChanged += (_, e) =>
        {
            if (IsLoaded && ReferenceEquals(e.Source, Pages))
                UpdatePlayerPreviews();
        };
        Closed += (_, _) =>
        {
            _spotify.StatusChanged -= OnSpotifyStatus;
            _vm.MiniLook?.SetPreviewActive(false);
            _vm.MinimalLook?.SetActive(false);
        };
        OnSpotifyStatus(this, EventArgs.Empty);
        // A new Minimal Mode shortcut is typed right here.
        PreviewKeyDown += (_, e) =>
        {
            if (vm.MinimalLook?.Capture(e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key, System.Windows.Input.Keyboard.Modifiers) == true)
                e.Handled = true;
        };
        Deactivated += (_, _) => { if (vm.MinimalLook is { IsCapturing: true } look) look.IsCapturing = false; };
        // The floating player stays on top of everything (maximized and full screen too): while the
        // settings are in use they go above it, and back to a normal window once another one is.
        Activated += (_, _) => Topmost = Application.Current.Windows.OfType<MiniPlayerWindow>().Any(w => w.IsVisible && w.IsPinned);
        Deactivated += (_, _) => Topmost = false;
        WirePlayerToolDrag();
        // A saved look gets its name right away: the cursor waits in it, the default name selected.
        if (vm.MiniLook is { } look)
            look.LookSaved += (_, _) => Dispatcher.InvokeAsync(() =>
            {
                LookNameBox.Focus();
                LookNameBox.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>
    /// The floating player's simulated button bar: a click shows or hides a button; pressed, held
    /// and dragged, it lifts and takes the place of the one it passes (the player follows live).
    /// </summary>
    private void WirePlayerToolDrag()
    {
        PlayerToolItem? pressed = null;
        FrameworkElement? pressedFace = null;
        Point pressedAt = default;
        var dragged = false;
        static PlayerToolItem? ItemAt(object source) =>
            (source as FrameworkElement ?? (source as FrameworkContentElement)?.Parent as FrameworkElement)?.DataContext as PlayerToolItem;
        static FrameworkElement? FaceOf(DependencyObject? d)
        {
            for (; d is not null; d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                     ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            {
                if (d is FrameworkElement { Name: "Tool" } face)
                    return face;
            }
            return null;
        }
        static void Lift(FrameworkElement? face, bool up)
        {
            if (face?.RenderTransform is not System.Windows.Media.ScaleTransform scale || scale.IsFrozen)
                return;
            scale.ScaleX = scale.ScaleY = up ? 1.18 : 1;
            face.Opacity = up ? 0.85 : 1;
        }

        PlayerToolList.PreviewMouseLeftButtonDown += (_, e) =>
        {
            pressed = ItemAt(e.OriginalSource);
            pressedFace = FaceOf(e.OriginalSource as DependencyObject);
            pressedAt = e.GetPosition(PlayerToolList);
            dragged = false;
            Lift(pressedFace, pressed is not null);
            if (pressed is not null)
                PlayerToolList.CaptureMouse();
        };
        PlayerToolList.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (PlayerToolList.IsMouseCaptured)
                PlayerToolList.ReleaseMouseCapture();
            Lift(pressedFace, false);
            // A click (no drag): show or hide it.
            if (pressed is { } item && !dragged)
                item.IsShown = !item.IsShown;
            pressed = null;
            pressedFace = null;
        };
        PlayerToolList.PreviewMouseMove += (_, e) =>
        {
            if (pressed is not { } item || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
                return;
            var moved = e.GetPosition(PlayerToolList) - pressedAt;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            dragged = true;
            if (PlayerToolList.IsMouseCaptured)
                PlayerToolList.ReleaseMouseCapture();
            var face = pressedFace;
            pressed = null;
            DragDrop.DoDragDrop(PlayerToolList, new DataObject(typeof(PlayerToolItem), item), DragDropEffects.Move);
            Lift(face, false);
            pressedFace = null;
        };
        PlayerToolList.DragOver += (_, e) =>
        {
            e.Effects = DragDropEffects.None;
            if (e.Data.GetData(typeof(PlayerToolItem)) is not PlayerToolItem item || _vm.MinimalLook is not { } look)
                return;
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            if (ItemAt(e.OriginalSource) is { } target && !ReferenceEquals(target, item))
                look.MoveTool(item, look.PlayerTools.IndexOf(target));
        };
        PlayerToolList.Drop += (_, e) => e.Handled = true;
    }

    private void UpdatePlayerPreviews()
    {
        var page = (SettingsPage)Pages.SelectedIndex;
        _vm.MiniLook?.SetPreviewActive(page is SettingsPage.Bar or SettingsPage.MinimizedBar or SettingsPage.MiniPlayer or SettingsPage.NotMinimal,
            showLooks: page is SettingsPage.MiniPlayer or SettingsPage.NotMinimal);
        _vm.MinimalLook?.SetActive(page == SettingsPage.Minimal);
    }

    private void OnSpotifyStatus(object? sender, EventArgs e) =>
        SpotifyLyricsStatus.SetResourceReference(TextBlock.TextProperty, "Loc." + _spotify.StatusKey);

    private async void OnConnectSpotify(object sender, RoutedEventArgs e)
    {
        ConnectSpotifyButton.IsEnabled = DisconnectSpotifyButton.IsEnabled = false;
        try { await _spotify.ConnectAsync(); }
        finally { ConnectSpotifyButton.IsEnabled = DisconnectSpotifyButton.IsEnabled = true; }
    }

    private async void OnDisconnectSpotify(object sender, RoutedEventArgs e)
    {
        ConnectSpotifyButton.IsEnabled = DisconnectSpotifyButton.IsEnabled = false;
        try { await _spotify.DisconnectAsync(); }
        finally { ConnectSpotifyButton.IsEnabled = DisconnectSpotifyButton.IsEnabled = true; }
    }

    private void OnAutoAppsOpened(object? sender, EventArgs e) => _vm.MinimalLook?.RefreshRunningApps();

    private void OnOffsetMinus(object sender, RoutedEventArgs e) => _vm.NudgeOffset(-250);

    private void OnOffsetPlus(object sender, RoutedEventArgs e) => _vm.NudgeOffset(250);

    private void OnOffsetReset(object sender, RoutedEventArgs e) => _vm.SyncOffsetMs = 0;

    private void OnTrackOffsetMinus(object sender, RoutedEventArgs e) => _vm.NudgeTrackOffset(-100);

    private void OnTrackOffsetPlus(object sender, RoutedEventArgs e) => _vm.NudgeTrackOffset(100);

    private void OnTrackOffsetReset(object sender, RoutedEventArgs e) => _vm.TrackOffsetMs = 0;
}

/// <summary>The settings pages, in their order.</summary>
public enum SettingsPage
{
    General,
    Bar,
    MinimizedBar,
    MiniPlayer,
    NotMinimal,
    Minimal,
    Lyrics,
    Position,
    Advanced,
}
