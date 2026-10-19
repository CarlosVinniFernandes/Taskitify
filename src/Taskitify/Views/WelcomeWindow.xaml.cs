using System.Windows;
using Taskitify.Services;

namespace Taskitify.Views;

/// <summary>
/// First run: where lyrics come from (LRCLIB alone, or Spotify first with LRCLIB behind it) and
/// what connecting Spotify does to the account, before the user is asked to sign in anywhere.
/// </summary>
public partial class WelcomeWindow : Window
{
    private readonly SpotifyLyricsSession _spotify;

    public WelcomeWindow(SpotifyLyricsSession spotify)
    {
        InitializeComponent();
        _spotify = spotify;
    }

    // LRCLIB alone is the default: nothing to set.
    private void OnUseLrclib(object sender, RoutedEventArgs e) => Close();

    private async void OnConnectSpotify(object sender, RoutedEventArgs e)
    {
        // Spotify's own login window takes over from here.
        var connecting = _spotify.ConnectAsync();
        Close();
        await connecting;
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        Close();
        ((App)Application.Current).ShowSettings();
    }
}
