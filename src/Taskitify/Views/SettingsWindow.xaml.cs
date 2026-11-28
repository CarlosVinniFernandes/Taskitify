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
        // A saved look gets its name right away: the cursor waits in it, the default name selected.
        if (vm.MiniLook is { } look)
            look.LookSaved += (_, _) => Dispatcher.InvokeAsync(() =>
            {
                LookNameBox.Focus();
                LookNameBox.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Input);
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
