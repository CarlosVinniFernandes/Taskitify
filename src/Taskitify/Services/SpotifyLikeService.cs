using System.Net;
using System.Net.Http;
using System.Windows.Threading;
using Taskitify.Core.Lyrics;
using Taskitify.Infrastructure;

namespace Taskitify.Services;

/// <summary>
/// Whether the playing song is in the user's Liked Songs, and liking or unliking it. Shared by
/// the taskbar player and the mini player.
/// </summary>
/// <remarks>
/// The state comes from Spotify's own "now playing" bar (<see cref="SpotifyDesktopLikes"/>): the
/// exact track playing, as Spotify shows it, re-read every few seconds so a like made in Spotify
/// shows up here too. Liking clicks Spotify's own button. Unliking (Spotify's button then opens
/// its playlist menu instead) goes through the web player's API, for the track on the album
/// Spotify's bar names. When the bar cannot be read (Spotify hidden to the tray, another
/// language) everything falls back to the API, which knows the song only by its search: there,
/// any liked version of the song counts.
/// </remarks>
public sealed class SpotifyLikeService : IDisposable
{
    // Skipping through songs does not send one check per song.
    private static readonly TimeSpan CheckDelay = TimeSpan.FromSeconds(1.2);
    // Spotify's own bar is re-read this often (a few ms), so likes made in Spotify appear here.
    private static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(3);
    // Spotify updates its button a moment after a click.
    private static readonly TimeSpan ClickSettle = TimeSpan.FromMilliseconds(700);

    private readonly ISpotifyMonitor _monitor;
    private readonly LyricsSyncService _sync;
    private readonly SettingsService _settings;
    private readonly ISpotifyLyricsSession _session;
    private readonly SpotifyLyricsProvider _provider;
    private readonly SpotifyLibraryClient _library;
    private readonly SpotifyDesktopLikes _desktop = new();
    private readonly DispatcherTimer _checkTimer;
    private readonly DispatcherTimer _watchTimer;
    private int _generation;
    private Task? _check;
    private bool _watching;
    private bool _wasEnabled;
    private string? _albumId;

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
            _ = CheckAsync(_generation);
        };
        _watchTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = WatchInterval };
        _watchTimer.Tick += (_, _) => _ = WatchAsync();
        _monitor.TrackChanged += OnTrackChanged;
        _monitor.PlaybackChanged += OnPlaybackChanged;
        _sync.LyricsChanged += OnLyricsChanged;
        _settings.Changed += OnSettingsChanged;
        _wasEnabled = IsAvailable;
        OnTrackChanged(this, EventArgs.Empty);
    }

    /// <summary>Spotify is connected and a song is playing: the like button can be shown.</summary>
    public bool IsAvailable => _settings.Current.SpotifyLyricsEnabled && _monitor.Track is not null;

    /// <summary>In Liked Songs; null while unknown (not checked yet, or Spotify unreachable).</summary>
    public bool? IsLiked { get; private set; }

    /// <summary>A like or unlike is being sent.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>The last like or unlike failed (cleared by the next song or attempt).</summary>
    public bool Failed { get; private set; }

    /// <summary>Raised when the song becomes liked by the user's click here (the views animate it).</summary>
    public event EventHandler? Liked;

    public event EventHandler? Changed;

    /// <summary>Check again (the user may have liked it in Spotify meanwhile).</summary>
    public void Refresh()
    {
        if (IsAvailable && !IsBusy && !_checkTimer.IsEnabled)
            _ = CheckAsync(_generation);
    }

    /// <summary>
    /// Like the song, or unlike it when it is liked (the views ask for confirmation first). An
    /// unknown state is looked up first: a song that turns out to be liked already stays liked.
    /// </summary>
    public async Task ToggleAsync()
    {
        if (!IsAvailable || IsBusy || _monitor.Track is not { } track)
            return;
        var generation = _generation;
        Failed = false;
        IsBusy = true;
        Raise();
        try
        {
            if (IsLiked is null)
            {
                await CheckAsync(generation);
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
            var like = IsLiked != true;
            // Shown at once; put back if it does not work.
            IsLiked = like;
            Raise();
            if (like)
                Liked?.Invoke(this, EventArgs.Empty);
            var ok = like ? await LikeAsync(track.Title, generation) : await UnlikeAsync(track.Title, generation);
            if (generation != _generation)
                return;
            if (!ok)
            {
                IsLiked = !like;
                Failed = true;
            }
            Log.Debug($"Spotify like: {(like ? "liked" : "unliked")} {(ok ? "ok" : "failed")}");
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

    /// <summary>Spotify's own button first (the exact track), else the API.</summary>
    private async Task<bool> LikeAsync(string title, int generation)
    {
        if (await Task.Run(() => _desktop.Like(title)))
        {
            await Task.Delay(ClickSettle);
            if (await Task.Run(() => _desktop.Read(title)) is { Liked: true })
                return true;
        }
        if (generation != _generation)
            return false;
        var ids = await _provider.FindTrackIdsAsync(Query(), wake: true, CancellationToken.None, _albumId);
        return ids.Count > 0 && await WithCredentialsAsync(c => _library.SetSavedAsync(ids.Take(1).ToList(), true, c, CancellationToken.None));
    }

    /// <summary>Through the API: the liked versions of the song, the playing album's first.</summary>
    private async Task<bool> UnlikeAsync(string title, int generation)
    {
        var ids = await _provider.FindTrackIdsAsync(Query(), wake: true, CancellationToken.None, _albumId);
        if (ids.Count == 0 || generation != _generation)
            return false;
        IReadOnlyList<bool>? saved = null;
        if (!await WithCredentialsAsync(async c => saved = await _library.SavedAsync(ids, c, CancellationToken.None)) || saved is null)
            return false;
        var targets = ids.Where((_, i) => saved[i]).ToList();
        if (targets.Count == 0)
            return false;
        if (!await WithCredentialsAsync(c => _library.SetSavedAsync(targets, false, c, CancellationToken.None)))
            return false;
        // Spotify's bar has the last word when it can be read.
        await Task.Delay(ClickSettle);
        return await Task.Run(() => _desktop.Read(title)) is not { Liked: true };
    }

    /// <summary>The check running for this song, or a new one: a click during a check waits for it.</summary>
    private Task CheckAsync(int generation)
    {
        if (!IsAvailable)
            return Task.CompletedTask;
        if (_check is { IsCompleted: false } running)
            return running;
        return _check = RunCheckAsync(generation);
    }

    private async Task RunCheckAsync(int generation)
    {
        if (_monitor.Track is not { } track)
            return;
        try
        {
            if (await Task.Run(() => _desktop.Read(track.Title)) is { } state)
            {
                if (generation != _generation)
                    return;
                _albumId = state.AlbumId;
                SetLiked(state.Liked, "Spotify's bar");
                StartWatching();
                return;
            }
            // Spotify's window cannot be read: any liked version of the song, through the API.
            var ids = await _provider.FindTrackIdsAsync(Query(), wake: true, CancellationToken.None);
            if (ids.Count == 0 || generation != _generation)
                return;
            IReadOnlyList<bool>? saved = null;
            if (!await WithCredentialsAsync(async c => saved = await _library.SavedAsync(ids, c, CancellationToken.None)) || saved is null
                || generation != _generation)
                return;
            SetLiked(saved.Any(s => s), $"API, {saved.Count(s => s)}/{saved.Count} versions");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Log.Debug($"Spotify like state unavailable: {ex.GetType().Name}");
        }
    }

    private void SetLiked(bool liked, string source)
    {
        if (IsLiked == liked)
            return;
        IsLiked = liked;
        Log.Debug($"Spotify like state: {(liked ? "liked" : "not liked")} ({source})");
        Raise();
    }

    private void StartWatching()
    {
        if (!_watchTimer.IsEnabled && _monitor.IsPlaying)
            _watchTimer.Start();
    }

    /// <summary>Re-read Spotify's bar (cheap once found), so a like made in Spotify shows here.</summary>
    private async Task WatchAsync()
    {
        if (_watching || IsBusy || !IsAvailable || _monitor.Track is not { } track)
            return;
        _watching = true;
        var generation = _generation;
        try
        {
            if (await Task.Run(() => _desktop.Read(track.Title)) is { } state && generation == _generation && !IsBusy)
                SetLiked(state.Liked, "Spotify's bar");
        }
        finally
        {
            _watching = false;
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
        _check = null;
        _albumId = null;
        _checkTimer.Stop();
        _watchTimer.Stop();
        if (IsAvailable)
            _checkTimer.Start();
        Raise();
    }

    private void OnPlaybackChanged(object? sender, EventArgs e)
    {
        // Watching only while music plays: nothing changes in Spotify's bar otherwise.
        if (!_monitor.IsPlaying)
            _watchTimer.Stop();
        else if (IsLiked is not null && IsAvailable)
            StartWatching();
    }

    // A lookup that just ran has a fresh token: a check through the API costs one request.
    private void OnLyricsChanged(object? sender, EventArgs e)
    {
        if (IsLiked is null && !IsBusy && !_checkTimer.IsEnabled)
            _ = CheckAsync(_generation);
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
        _watchTimer.Stop();
        _monitor.TrackChanged -= OnTrackChanged;
        _monitor.PlaybackChanged -= OnPlaybackChanged;
        _sync.LyricsChanged -= OnLyricsChanged;
        _settings.Changed -= OnSettingsChanged;
    }
}
