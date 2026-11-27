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
        };
        OnSpotifyStatus(this, EventArgs.Empty);
    }

    private void UpdatePlayerPreviews()
    {
        var page = (SettingsPage)Pages.SelectedIndex;
        _vm.MiniLook?.SetPreviewActive(page is SettingsPage.Bar or SettingsPage.MinimizedBar or SettingsPage.MiniPlayer,
            showLooks: page == SettingsPage.MiniPlayer);
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

    private void OnOffsetMinus(object sender, RoutedEventArgs e) => _vm.NudgeOffset(-250);

    private void OnOffsetPlus(object sender, RoutedEventArgs e) => _vm.NudgeOffset(250);

    private void OnOffsetReset(object sender, RoutedEventArgs e) => _vm.SyncOffsetMs = 0;
}

/// <summary>The settings pages, in their order.</summary>
public enum SettingsPage
{
    General,
    Bar,
    MinimizedBar,
    MiniPlayer,
    Lyrics,
    Position,
    Advanced,
}
