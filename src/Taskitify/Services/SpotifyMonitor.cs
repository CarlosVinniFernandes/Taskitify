using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Taskitify.Core.Imaging;
using Taskitify.Core.Lyrics;
using Taskitify.Infrastructure;
using Taskitify.Models;
using MediaPlaybackAutoRepeatMode = Windows.Media.MediaPlaybackAutoRepeatMode;
using Windows.Media.Control;
using Windows.Storage.Streams;
using GsmtcStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace Taskitify.Services;

public interface ISpotifyMonitor : IDisposable
{
    bool HasSession { get; }

    string? SessionAppId { get; }

    IReadOnlyList<string> AllSessionAppIds { get; }

    TrackInfo? Track { get; }

    PlaybackState Status { get; }

    bool IsPlaying => Status == PlaybackState.Playing;

    /// <summary>Locally projected position (see <see cref="PlaybackClock"/>).</summary>
    TimeSpan Position { get; }

    TimeSpan? Duration { get; }

    ImageSource? Thumbnail { get; }

    bool CanSeek { get; }

    bool CanGoNext { get; }

    bool CanGoPrevious { get; }

    bool CanShuffle { get; }

    bool CanRepeat { get; }

    bool IsShuffleActive { get; }

    RepeatMode RepeatMode { get; }

    /// <summary>Background gradient colours derived from the album art (dark left, accent right).</summary>
    (Color Start, Color End) Palette { get; }

    /// <summary>Different song (title/artist/album). Raised on the UI thread, like all events here.</summary>
    event EventHandler? TrackChanged;

    event EventHandler? PlaybackChanged;

    /// <summary>The position base changed (seek, new timeline report) or the duration became known.</summary>
    event EventHandler? TimelineChanged;

    event EventHandler? ThumbnailChanged;

    Task StartAsync();

    Task PlayPauseAsync();

    Task NextAsync();

    Task PreviousAsync();

    Task SeekAsync(TimeSpan position);

    Task ToggleShuffleAsync();

    /// <summary>None → List → Track → None, like Spotify's repeat button.</summary>
    Task CycleRepeatAsync();
}

/// <summary>
/// Watches the Windows media session (GSMTC) that belongs to Spotify Desktop. Event driven;
/// the only polling is a 1.5 s timeline/state check while playing, to correct drift in case
/// Spotify does not raise an event, which also notices a new song whose event is late.
/// </summary>
public sealed class SpotifyMonitor : ISpotifyMonitor
{
    // Spotify sometimes publishes a song without its cover, or adds the cover later without
    // another event: look again this long after the song change, then ask CoverFallback.
    private static readonly TimeSpan[] CoverRetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    private readonly Dispatcher _dispatcher;
    private readonly PlaybackClock _clock = new();
    private readonly DispatcherTimer _correctionTimer;
    private readonly DispatcherTimer _coverRetry;
    private int _coverRetries;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private DateTimeOffset _lastTimelineStamp;
    private TimeSpan _lastTimelinePosition;
    private int _propertiesVersion;
    private bool _thumbnailLoaded;
    private DateTimeOffset _trackChangedAt = DateTimeOffset.MinValue;
    private (bool Value, DateTime Until)? _pendingShuffle;
    private (RepeatMode Value, DateTime Until)? _pendingRepeat;

    // How long a just-sent shuffle/repeat change wins over (possibly stale) session reports.
    private static readonly TimeSpan PendingModeWindow = TimeSpan.FromSeconds(2);

    public SpotifyMonitor(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _correctionTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(1500) };
        _correctionTimer.Tick += (_, _) =>
        {
            ReadPlaybackInfo();
            ReadTimeline();
        };
        _coverRetry = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        _coverRetry.Tick += async (_, _) =>
        {
            _coverRetry.Stop();
            await RetryCoverAsync();
        };
    }

    /// <summary>
    /// A cover for a song Spotify published without one (the encoded image, or null), asked once
    /// the retries found none. Runs on the UI thread.
    /// </summary>
    public Func<TrackInfo, TimeSpan?, Task<byte[]?>>? CoverFallback { get; set; }

    public event EventHandler? TrackChanged;
    public event EventHandler? PlaybackChanged;
    public event EventHandler? TimelineChanged;
    public event EventHandler? ThumbnailChanged;

    public bool HasSession => _session is not null;
    public string? SessionAppId { get; private set; }
    public IReadOnlyList<string> AllSessionAppIds { get; private set; } = [];
    public TrackInfo? Track { get; private set; }
    public PlaybackState Status { get; private set; } = PlaybackState.NoSession;
    public TimeSpan Position => _clock.Position;
    public TimeSpan? Duration => _clock.Duration;
    public ImageSource? Thumbnail { get; private set; }
    public bool CanSeek { get; private set; }
    public bool CanGoNext { get; private set; }
    public bool CanGoPrevious { get; private set; }
    public bool CanShuffle { get; private set; }
    public bool CanRepeat { get; private set; }
    public bool IsShuffleActive { get; private set; }
    public RepeatMode RepeatMode { get; private set; }
    public (Color Start, Color End) Palette { get; private set; } = ToColors(AlbumPalette.Fallback);

    /// <summary>
    /// Spotify Desktop (installer) registers as "Spotify.exe"; the Microsoft Store build as
    /// "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify". Browsers playing open.spotify.com do NOT match.
    /// </summary>
    public static bool IsSpotify(string? appId) =>
        !string.IsNullOrEmpty(appId)
        && (appId.Equals("Spotify.exe", StringComparison.OrdinalIgnoreCase)
            || appId.EndsWith("\\Spotify.exe", StringComparison.OrdinalIgnoreCase)
            || appId.StartsWith("SpotifyAB.SpotifyMusic", StringComparison.OrdinalIgnoreCase));

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.SessionsChanged += OnSessionsChanged;
        await RefreshSessionAsync();
    }

    public async Task PlayPauseAsync()
    {
        if (_session is { } s)
            await TryControl(() => s.TryTogglePlayPauseAsync().AsTask(), "play/pause");
    }

    public async Task NextAsync()
    {
        if (_session is { } s)
            await TryControl(() => s.TrySkipNextAsync().AsTask(), "next");
    }

    public async Task PreviousAsync()
    {
        if (_session is { } s)
            await TryControl(() => s.TrySkipPreviousAsync().AsTask(), "previous");
    }

    public async Task SeekAsync(TimeSpan position)
    {
        if (_session is not { } s || !CanSeek)
            return;
        if (await TryControl(() => s.TryChangePlaybackPositionAsync(position.Ticks).AsTask(), "seek"))
        {
            // Optimistic: Spotify confirms with a timeline event a moment later.
            _clock.Sync(position, DateTimeOffset.UtcNow, Status == PlaybackState.Playing);
            TimelineChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task ToggleShuffleAsync()
    {
        if (_session is not { } s || !CanShuffle)
            return;
        var target = !IsShuffleActive;
        if (await TryControl(() => s.TryChangeShuffleActiveAsync(target).AsTask(), "shuffle"))
        {
            // Optimistic: a quick second click must toggle from the new state, not a stale report.
            _pendingShuffle = (target, DateTime.UtcNow + PendingModeWindow);
            IsShuffleActive = target;
            PlaybackChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task CycleRepeatAsync()
    {
        if (_session is not { } s || !CanRepeat)
            return;
        var next = RepeatMode switch
        {
            RepeatMode.None => MediaPlaybackAutoRepeatMode.List,
            RepeatMode.List => MediaPlaybackAutoRepeatMode.Track,
            _ => MediaPlaybackAutoRepeatMode.None,
        };
        if (await TryControl(() => s.TryChangeAutoRepeatModeAsync(next).AsTask(), "repeat"))
        {
            var mode = next switch
            {
                MediaPlaybackAutoRepeatMode.List => RepeatMode.List,
                MediaPlaybackAutoRepeatMode.Track => RepeatMode.Track,
                _ => RepeatMode.None,
            };
            _pendingRepeat = (mode, DateTime.UtcNow + PendingModeWindow);
            RepeatMode = mode;
            PlaybackChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _correctionTimer.Stop();
        _coverRetry.Stop();
        if (_manager is not null)
            _manager.SessionsChanged -= OnSessionsChanged;
        Detach();
    }

    private static async Task<bool> TryControl(Func<Task<bool>> action, string name)
    {
        try
        {
            var ok = await action();
            if (!ok)
                Log.Warn($"Spotify refused '{name}'");
            return ok;
        }
        catch (Exception ex)
        {
            Log.Error($"Media control '{name}' failed", ex);
            return false;
        }
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        _dispatcher.InvokeAsync(RefreshSessionAsync);

    private async Task RefreshSessionAsync()
    {
        if (_manager is null)
            return;

        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions;
        try
        {
            sessions = _manager.GetSessions();
        }
        catch (Exception ex)
        {
            Log.Error("GetSessions failed", ex);
            return;
        }

        AllSessionAppIds = sessions.Select(s => s.SourceAppUserModelId).ToList();
        var spotify = sessions.FirstOrDefault(s => IsSpotify(s.SourceAppUserModelId));

        // Always re-attach: after a Spotify restart the AUMID is the same but the session object is new.
        Detach();

        if (spotify is null)
        {
            if (SessionAppId is not null)
                Log.Info("Spotify session gone");
            SessionAppId = null;
            SetNoSession();
            return;
        }

        if (SessionAppId is null)
            Log.Info($"Spotify session found: {spotify.SourceAppUserModelId}");
        _session = spotify;
        SessionAppId = spotify.SourceAppUserModelId;
        _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;

        await LoadMediaPropertiesAsync();
        ReadPlaybackInfo();
        ReadTimeline();
    }

    private void Detach()
    {
        if (_session is null)
            return;
        _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        _session = null;
    }

    private void SetNoSession()
    {
        _correctionTimer.Stop();
        _coverRetry.Stop();
        _clock.SetPlaying(false);
        _clock.Reset();
        _clock.Duration = null;
        CanSeek = CanGoNext = CanGoPrevious = false;
        var hadTrack = Track is not null;
        Track = null;
        Thumbnail = null;
        _thumbnailLoaded = false;
        if (Status != PlaybackState.NoSession)
        {
            Status = PlaybackState.NoSession;
            PlaybackChanged?.Invoke(this, EventArgs.Empty);
        }
        if (hadTrack)
        {
            TrackChanged?.Invoke(this, EventArgs.Empty);
            ThumbnailChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        // Arrival time vs. the "Track:" line tells delivery delays from processing delays.
        Log.Debug("Media properties changed (event received)");
        _dispatcher.InvokeAsync(async () =>
        {
            if (IsCurrent(sender))
                await LoadMediaPropertiesAsync();
        });
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        _dispatcher.InvokeAsync(() =>
        {
            if (!IsCurrent(sender))
                return;
            ReadPlaybackInfo();
            ReadTimeline();
        });

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) =>
        _dispatcher.InvokeAsync(() =>
        {
            if (IsCurrent(sender))
                ReadTimeline();
        });

    private bool IsCurrent(GlobalSystemMediaTransportControlsSession sender) =>
        _session is not null && sender.SourceAppUserModelId == _session.SourceAppUserModelId;

    /// <param name="changedBy">When a timeline report revealed the new song: that report's
    /// time, so the report counts as the new song's (its event may come much later).</param>
    private async Task LoadMediaPropertiesAsync(DateTimeOffset? changedBy = null)
    {
        var session = _session;
        if (session is null)
            return;

        var version = ++_propertiesVersion;
        GlobalSystemMediaTransportControlsSessionMediaProperties props;
        try
        {
            props = await session.TryGetMediaPropertiesAsync();
        }
        catch (Exception ex)
        {
            Log.Error("TryGetMediaPropertiesAsync failed", ex);
            return;
        }

        // A newer request started while this one was in flight, or the session changed.
        if (version != _propertiesVersion || !ReferenceEquals(session, _session) || props is null)
            return;

        // Spotify often sends an empty update between songs; ignore it instead of flickering.
        if (string.IsNullOrWhiteSpace(props.Title))
            return;

        var track = new TrackInfo(props.Title.Trim(), (props.Artist ?? string.Empty).Trim(), (props.AlbumTitle ?? string.Empty).Trim());
        if (track != Track)
        {
            Log.Info($"Track: {track.Artist} - {track.Title} [{track.Album}]");
            // Only a change *from* another song can leave a stale timeline behind.
            _trackChangedAt = Track is null ? DateTimeOffset.MinValue
                : changedBy is { } at && at < DateTimeOffset.UtcNow ? at - TimeSpan.FromSeconds(1) : DateTimeOffset.UtcNow;
            Track = track;
            _clock.Reset();
            _clock.Duration = null;
            // The report that revealed the song is its own, but it went to the previous song's
            // clock and only new reports are applied: read it again after the reset.
            if (changedBy is not null)
                _lastTimelineStamp = default;
            _thumbnailLoaded = false;
            _coverRetries = 0;
            _coverRetry.Stop();
            Thumbnail = null;
            TrackChanged?.Invoke(this, EventArgs.Empty);
            ThumbnailChanged?.Invoke(this, EventArgs.Empty);
            ReadTimeline();
        }

        if (!_thumbnailLoaded && props.Thumbnail is not null)
        {
            _thumbnailLoaded = true;
            await LoadThumbnailAsync(props.Thumbnail, track);
            // Each media-properties call leaves ~0.2 MB of native memory (the cover) behind
            // WinRT wrappers that only a collection frees, and this app allocates too little for
            // one to come on its own (measured: +16 MB over 80 calls; flat with a young-generation
            // collection). One per song costs about a millisecond.
            GC.Collect(1);
        }
        if (!_thumbnailLoaded && !_coverRetry.IsEnabled && _coverRetries < CoverRetryDelays.Length)
        {
            _coverRetry.Interval = CoverRetryDelays[_coverRetries++];
            _coverRetry.Start();
        }
    }

    /// <summary>The song still has no cover: read it again, and when the retries are used up, ask CoverFallback.</summary>
    private async Task RetryCoverAsync()
    {
        if (_thumbnailLoaded || Track is not { } track)
            return;
        await LoadMediaPropertiesAsync();
        if (_thumbnailLoaded || Track != track || _coverRetry.IsEnabled || CoverFallback is not { } fallback)
            return;
        // The retries' media-properties calls each hold native memory until a collection.
        GC.Collect(1);
        try
        {
            if (await fallback(track, Duration) is { } encoded && Track == track && !_thumbnailLoaded)
            {
                // Not marked as loaded: a cover Spotify publishes later still replaces it.
                ShowCover(encoded, track);
                Log.Debug("Album art from Spotify's search (none in the media session)");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not get album art from Spotify's search", ex);
        }
    }

    /// <summary>The song GSMTC reports now, when its MediaPropertiesChanged has not arrived yet.</summary>
    private async Task CheckTrackAsync(DateTimeOffset reportedAt)
    {
        if (_session is not { } session)
            return;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (props is null || string.IsNullOrWhiteSpace(props.Title) || !ReferenceEquals(session, _session))
                return;
            var track = new TrackInfo(props.Title.Trim(), (props.Artist ?? string.Empty).Trim(), (props.AlbumTitle ?? string.Empty).Trim());
            if (track != Track)
            {
                Log.Debug("Song changed before its media event arrived");
                await LoadMediaPropertiesAsync(reportedAt);
            }
        }
        catch (Exception)
        {
            // The next tick, or the event itself, tries again.
        }
    }

    private async Task LoadThumbnailAsync(IRandomAccessStreamReference reference, TrackInfo forTrack)
    {
        try
        {
            using var ras = await reference.OpenReadAsync();
            await using var source = ras.AsStreamForRead();
            var buffer = new MemoryStream();
            await source.CopyToAsync(buffer);
            ShowCover(buffer.ToArray(), forTrack);
        }
        catch (Exception ex)
        {
            _thumbnailLoaded = false;
            Log.Error("Could not load album art", ex);
        }
    }

    /// <summary>Decode an encoded cover (and its gradient colours) for the song still playing.</summary>
    private void ShowCover(byte[] encoded, TrackInfo forTrack)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 256;
        image.StreamSource = new MemoryStream(encoded);
        image.EndInit();
        image.Freeze();
        var palette = ExtractPalette(new MemoryStream(encoded));

        if (forTrack == Track)
        {
            Thumbnail = image;
            Palette = palette;
            ThumbnailChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Decode a tiny copy of the cover and derive the background gradient from it.</summary>
    private static (Color Start, Color End) ExtractPalette(Stream encoded)
    {
        try
        {
            var small = new BitmapImage();
            small.BeginInit();
            small.CacheOption = BitmapCacheOption.OnLoad;
            small.DecodePixelWidth = 48;
            small.StreamSource = encoded;
            small.EndInit();
            var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            var stride = bgra.PixelWidth * 4;
            var pixels = new byte[stride * bgra.PixelHeight];
            bgra.CopyPixels(pixels, stride, 0);
            return ToColors(AlbumPalette.Extract(pixels, bgra.PixelWidth, bgra.PixelHeight, stride));
        }
        catch (Exception ex)
        {
            Log.Error("Could not extract album colours", ex);
            return ToColors(AlbumPalette.Fallback);
        }
    }

    private static (Color Start, Color End) ToColors((Rgb Start, Rgb End) p) =>
        (Color.FromRgb(p.Start.R, p.Start.G, p.Start.B), Color.FromRgb(p.End.R, p.End.G, p.End.B));

    private void ReadPlaybackInfo()
    {
        if (_session is null)
            return;
        GlobalSystemMediaTransportControlsSessionPlaybackInfo info;
        try
        {
            info = _session.GetPlaybackInfo();
        }
        catch (Exception ex)
        {
            Log.Error("GetPlaybackInfo failed", ex);
            return;
        }
        if (info is null)
            return;

        CanSeek = info.Controls.IsPlaybackPositionEnabled;
        CanGoNext = info.Controls.IsNextEnabled;
        CanGoPrevious = info.Controls.IsPreviousEnabled;

        var shuffle = info.IsShuffleActive == true;
        var repeat = info.AutoRepeatMode switch
        {
            MediaPlaybackAutoRepeatMode.Track => RepeatMode.Track,
            MediaPlaybackAutoRepeatMode.List => RepeatMode.List,
            _ => RepeatMode.None,
        };
        if (_pendingShuffle is { } ps)
        {
            if (shuffle == ps.Value || DateTime.UtcNow > ps.Until)
                _pendingShuffle = null;
            else
                shuffle = ps.Value;
        }
        if (_pendingRepeat is { } pr)
        {
            if (repeat == pr.Value || DateTime.UtcNow > pr.Until)
                _pendingRepeat = null;
            else
                repeat = pr.Value;
        }
        var modesChanged = shuffle != IsShuffleActive || repeat != RepeatMode
            || CanShuffle != info.Controls.IsShuffleEnabled || CanRepeat != info.Controls.IsRepeatEnabled;
        IsShuffleActive = shuffle;
        RepeatMode = repeat;
        CanShuffle = info.Controls.IsShuffleEnabled;
        CanRepeat = info.Controls.IsRepeatEnabled;

        var status = info.PlaybackStatus switch
        {
            GsmtcStatus.Playing => PlaybackState.Playing,
            GsmtcStatus.Paused => PlaybackState.Paused,
            GsmtcStatus.Changing => PlaybackState.Changing,
            GsmtcStatus.Stopped => PlaybackState.Stopped,
            GsmtcStatus.Opened => PlaybackState.Stopped,
            _ => PlaybackState.Stopped, // Closed
        };

        if (status == Status)
        {
            if (modesChanged)
                PlaybackChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        Log.Info($"Playback: {Status} -> {status}");
        Status = status;
        _clock.SetPlaying(status == PlaybackState.Playing);
        if (status == PlaybackState.Playing)
            _correctionTimer.Start();
        else
            _correctionTimer.Stop();
        PlaybackChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Right after a song change Spotify keeps exposing the previous song's timeline for a
    /// few seconds (it refreshes it every ~4-5 s). Its duration must not be used for the new
    /// song: it made lyrics lookups use the wrong length and miss. A report counts as fresh
    /// when it was produced after the change, or just before it with the position at the start.
    /// </summary>
    private bool IsTimelineForCurrentTrack(GlobalSystemMediaTransportControlsSessionTimelineProperties t)
    {
        if (_trackChangedAt == DateTimeOffset.MinValue)
            return true; // first track seen at startup: nothing older to confuse it with
        var stamp = t.LastUpdatedTime;
        if (stamp >= _trackChangedAt + TimeSpan.FromMilliseconds(300))
            return true;
        return stamp >= _trackChangedAt - TimeSpan.FromSeconds(1) && (t.Position - t.StartTime) < TimeSpan.FromSeconds(10);
    }

    private void ReadTimeline()
    {
        if (_session is null)
            return;
        GlobalSystemMediaTransportControlsSessionTimelineProperties t;
        try
        {
            t = _session.GetTimelineProperties();
        }
        catch (Exception ex)
        {
            Log.Error("GetTimelineProperties failed", ex);
            return;
        }
        if (t is null)
            return;

        var changed = false;
        var duration = t.EndTime - t.StartTime;
        // MediaPropertiesChanged can reach us late (measured: 10 s after a skip whose title GSMTC
        // already returned on request). A timeline with another length, or one that went back
        // to the start, is checked against the song at once; polling the song itself would
        // pile up the native memory described in LoadMediaPropertiesAsync.
        var position = t.Position - t.StartTime;
        if (t.LastUpdatedTime != _lastTimelineStamp && Track is not null
            && ((_clock.Duration is { } current && duration > TimeSpan.Zero && Math.Abs((current - duration).TotalSeconds) > 1)
                || position < _lastTimelinePosition - TimeSpan.FromSeconds(5)))
            _ = CheckTrackAsync(t.LastUpdatedTime);
        if (duration > TimeSpan.Zero
            && IsTimelineForCurrentTrack(t)
            && (_clock.Duration is not { } known || Math.Abs((known - duration).TotalMilliseconds) > 500))
        {
            _clock.Duration = duration;
            changed = true;
        }

        // Only apply reports we have not seen yet: re-applying an old (stale) report while
        // playing would project from the wrong origin after a pause.
        if (t.LastUpdatedTime != _lastTimelineStamp || position != _lastTimelinePosition)
        {
            _lastTimelineStamp = t.LastUpdatedTime;
            _lastTimelinePosition = position;
            _clock.Sync(position, t.LastUpdatedTime, Status == PlaybackState.Playing);
            changed = true;
        }

        if (changed)
            TimelineChanged?.Invoke(this, EventArgs.Empty);
    }
}
