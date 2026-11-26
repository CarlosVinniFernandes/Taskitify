using System.Net;
using System.Net.Http;
using System.Windows.Threading;
using Taskitify.Core.Lyrics;
using Taskitify.Infrastructure;

namespace Taskitify.Services;

/// <summary>
/// Whether the playing song is in the user's Liked Songs, and liking or unliking it, through the
/// connected Spotify web session. Shared by the taskbar player and the mini player. Checking a
/// song never opens the web player (only a token still valid is used, so most songs are known
/// right after their lyrics lookup); a click may open it.
/// </summary>
public sealed class SpotifyLikeService : IDisposable
{
    // Skipping through songs does not send one check per song.
    private static readonly TimeSpan CheckDelay = TimeSpan.FromSeconds(1.5);

    private readonly ISpotifyMonitor _monitor;
    private readonly LyricsSyncService _sync;
    private readonly SettingsService _settings;
    private readonly ISpotifyLyricsSession _session;
    private readonly SpotifyLyricsProvider _provider;
    private readonly SpotifyLibraryClient _library;
    private readonly DispatcherTimer _checkTimer;
    private int _generation;
    private bool _checking;

    public SpotifyLikeService(ISpotifyMonitor monitor, LyricsSyncService sync, SettingsService settings, ISpotifyLyricsSession session,
        SpotifyLyricsProvider provider, SpotifyLibraryClient library, Dispatcher dispatcher)
    {
        _monitor = monitor;
        _sync = sync;
        _settings = settings;
        _session = session;
        _provider = provider;
        _library = library;
        _checkTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = CheckDelay };
        _checkTimer.Tick += (_, _) =>
        {
            _checkTimer.Stop();
            _ = CheckAsync(wake: false);
        };
        _monitor.TrackChanged += OnTrackChanged;
        _sync.LyricsChanged += OnLyricsChanged;
        _settings.Changed += OnSettingsChanged;
        _wasEnabled = IsAvailable;
        OnTrackChanged(this, EventArgs.Empty);
    }

    private bool _wasEnabled;

    /// <summary>Spotify is connected and a song is playing: the like button can be shown.</summary>
    public bool IsAvailable => _settings.Current.SpotifyLyricsEnabled && _monitor.Track is not null;

    /// <summary>In Liked Songs; null while unknown (not checked yet, or Spotify unreachable).</summary>
    public bool? IsLiked { get; private set; }

    /// <summary>A like or unlike is being sent.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>The last like or unlike failed (cleared by the next song or attempt).</summary>
    public bool Failed { get; private set; }

    public event EventHandler? Changed;

    /// <summary>Check again without opening the web player (the user may have liked it in Spotify).</summary>
    public void Refresh()
    {
        if (IsAvailable && !IsBusy)
            _ = CheckAsync(wake: false);
    }

    /// <summary>
    /// Like the song, or unlike it when it is liked (the views ask for confirmation first). An
    /// unknown state is looked up first: a song that turns out to be liked already stays liked.
    /// </summary>
    public async Task ToggleAsync()
    {
        if (!IsAvailable || IsBusy || _monitor.Track is null)
            return;
        var generation = _generation;
        Failed = false;
        IsBusy = true;
        Raise();
        try
        {
            if (IsLiked is null)
            {
                await CheckAsync(wake: true, generation);
                if (generation != _generation)
                    return;
                if (IsLiked is null)
                {
                    Failed = true;
                    return;
                }
                if (IsLiked == true)
                    return;
            }
            var target = IsLiked != true;
            var query = Query();
            var id = await _provider.FindTrackIdAsync(query, wake: true, CancellationToken.None);
            if (generation != _generation)
                return;
            if (id is null)
            {
                Failed = true;
                return;
            }
            // Shown at once; put back if Spotify refuses.
            IsLiked = target;
            Raise();
            var ok = await WithCredentialsAsync(c => _library.SetSavedAsync(id, target, c, CancellationToken.None));
            if (generation != _generation)
                return;
            if (!ok)
            {
                IsLiked = !target;
                Failed = true;
            }
            Log.Debug($"Spotify like: {(target ? "liked" : "unliked")} {(ok ? "ok" : "failed")}");
        }
        finally
        {
            if (generation == _generation)
            {
                IsBusy = false;
                Raise();
            }
        }
    }

    private Task CheckAsync(bool wake) => CheckAsync(wake, _generation);

    private async Task CheckAsync(bool wake, int generation)
    {
        if (!IsAvailable || (_checking && !wake))
            return;
        _checking = true;
        try
        {
            var query = Query();
            var id = await _provider.FindTrackIdAsync(query, wake, CancellationToken.None);
            if (id is null || generation != _generation)
                return;
            bool? saved = null;
            if (wake)
            {
                if (!await WithCredentialsAsync(async c => saved = await _library.IsSavedAsync(id, c, CancellationToken.None)))
                    saved = null;
            }
            else if (_session.ValidCredentials is { } credentials)
            {
                saved = await _library.IsSavedAsync(id, credentials, CancellationToken.None);
            }
            if (saved is null || generation != _generation)
                return;
            IsLiked = saved;
            Raise();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Log.Debug($"Spotify like state unavailable: {ex.GetType().Name}");
        }
        finally
        {
            _checking = false;
        }
    }

    /// <summary>Run with a token (opening the web player if needed), renewing it once on a 401.</summary>
    private async Task<bool> WithCredentialsAsync(Func<SpotifyCredentials, Task> action)
    {
        try
        {
            if (await _session.GetCredentialsAsync(null, CancellationToken.None) is not { } credentials)
                return false;
            try
            {
                await action(credentials);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                if (await _session.RenewCredentialsAsync(credentials, CancellationToken.None) is not { } renewed)
                    return false;
                await action(renewed);
            }
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Log.Debug($"Spotify library request failed: {ex.GetType().Name}");
            return false;
        }
    }

    private TrackQuery Query()
    {
        var track = _monitor.Track!;
        return new TrackQuery(track.Title, track.Artist, track.Album, _monitor.Duration);
    }

    private void OnTrackChanged(object? sender, EventArgs e)
    {
        _generation++;
        IsLiked = null;
        IsBusy = false;
        Failed = false;
        _checkTimer.Stop();
        if (IsAvailable)
            _checkTimer.Start();
        Raise();
    }

    // After a lookup the token is fresh and the song's Spotify id known: the check is free.
    private void OnLyricsChanged(object? sender, EventArgs e)
    {
        if (IsLiked is null && !IsBusy && !_checkTimer.IsEnabled)
            _ = CheckAsync(wake: false);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        var enabled = IsAvailable;
        if (enabled == _wasEnabled)
            return;
        _wasEnabled = enabled;
        OnTrackChanged(sender, e);
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _checkTimer.Stop();
        _monitor.TrackChanged -= OnTrackChanged;
        _sync.LyricsChanged -= OnLyricsChanged;
        _settings.Changed -= OnSettingsChanged;
    }
}
