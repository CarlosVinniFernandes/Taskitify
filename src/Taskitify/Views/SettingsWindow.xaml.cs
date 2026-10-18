using System.Windows;
using Taskitify.ViewModels;
using Taskitify.Services;
using System.Windows.Controls;

namespace Taskitify.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    private readonly SpotifyLyricsSession _spotify;

    public SettingsWindow(SettingsViewModel vm, SpotifyLyricsSession spotify)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        _spotify = spotify;
        _spotify.StatusChanged += OnSpotifyStatus;
        Closed += (_, _) => _spotify.StatusChanged -= OnSpotifyStatus;
        OnSpotifyStatus(this, EventArgs.Empty);
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
