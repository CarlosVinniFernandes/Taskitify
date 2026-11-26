using System.Net;
using System.Net.Http;
using System.Windows.Threading;
using Taskitify.Core.Lyrics;
using Taskitify.Infrastructure;

namespace Taskitify.Services;

/// <summary>
/// Whether the playing song is in the user's Liked Songs, and liking or unliking it, through the
/// connected Spotify web session. Shared by the taskbar player and the mini player.
/// </summary>
/// <remarks>
/// Windows does not say which Spotify track is playing, so the song is found with Spotify's
/// search, and the same song is often on an album, a single and a compilation: every matching
/// version is checked, and the song counts as liked when any of them is. Liking adds the best
/// match; unliking removes every version that was liked. The check may need a token: it opens
/// the hidden web player when none is valid (at most about once an hour, it closes again by itself).
/// </remarks>
public sealed class SpotifyLikeService : IDisposable
{
    // Skipping through songs does not send one check per song.
    private static readonly TimeSpan CheckDelay = TimeSpan.FromSeconds(2);

    private readonly ISpotifyMonitor _monitor;
    private readonly LyricsSyncService _sync;
    private readonly SettingsService _settings;
    private readonly ISpotifyLyricsSession _session;
    private readonly SpotifyLyricsProvider _provider;
    private readonly SpotifyLibraryClient _library;
    private readonly DispatcherTimer _checkTimer;
    private int _generation;
    private Task? _check;
    private bool _wasEnabled;
    // The versions of the song and which of them are liked, from the last check.
    private IReadOnlyList<string> _ids = [];
    private IReadOnlyList<bool> _saved = [];

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
        _monitor.TrackChanged += OnTrackChanged;
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
        if (!IsAvailable || IsBusy)
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
            // Liking adds the best match; unliking removes every version that is liked.
            var targets = like ? _ids.Take(1).ToList() : _ids.Where((_, i) => i < _saved.Count && _saved[i]).ToList();
            if (targets.Count == 0)
            {
                Failed = true;
                return;
            }
            // Shown at once; put back if Spotify refuses.
            IsLiked = like;
            Raise();
            var ok = await WithCredentialsAsync(c => _library.SetSavedAsync(targets, like, c, CancellationToken.None));
            if (generation != _generation)
                return;
            if (ok)
                _saved = _ids.Select((id, i) => like ? (i == 0 || (i < _saved.Count && _saved[i])) : (i < _saved.Count && _saved[i] && !targets.Contains(id))).ToList();
            else
            {
                IsLiked = !like;
                Failed = true;
            }
            Log.Debug($"Spotify like: {(like ? "liked" : "unliked")} {targets.Count} version(s) {(ok ? "ok" : "failed")}");
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
        try
        {
            var ids = await _provider.FindTrackIdsAsync(Query(), wake: true, CancellationToken.None);
            if (ids.Count == 0 || generation != _generation)
                return;
            IReadOnlyList<bool>? saved = null;
            if (!await WithCredentialsAsync(async c => saved = await _library.SavedAsync(ids, c, CancellationToken.None)) || saved is null
                || generation != _generation)
                return;
            _ids = ids;
            _saved = saved;
            IsLiked = saved.Any(s => s);
            Log.Debug($"Spotify like state: {(IsLiked == true ? "liked" : "not liked")} ({saved.Count(s => s)}/{saved.Count} versions)");
            Raise();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Log.Debug($"Spotify like state unavailable: {ex.GetType().Name}");
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
        _ids = [];
        _saved = [];
        _check = null;
        _checkTimer.Stop();
        if (IsAvailable)
            _checkTimer.Start();
        Raise();
    }

    // A lookup that just ran has a fresh token and the song's versions: the check costs one request.
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
        _monitor.TrackChanged -= OnTrackChanged;
        _sync.LyricsChanged -= OnLyricsChanged;
        _settings.Changed -= OnSettingsChanged;
    }
}
