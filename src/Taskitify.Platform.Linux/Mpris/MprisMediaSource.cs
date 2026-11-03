using Taskitify.Core.Lyrics;
using Taskitify.Core.Media;
using Tmds.DBus;

namespace Taskitify.Platform.Linux.Mpris;

/// <summary>
/// Follows Spotify Desktop through MPRIS2 on the D-Bus session bus. Event driven (PropertiesChanged
/// and Seeked); the only polling is a light position check while playing, because MPRIS does not
/// signal the position, to correct drift. Works with the deb, snap and flatpak builds, which all
/// own <c>org.mpris.MediaPlayer2.spotify</c>.
/// </summary>
public sealed class MprisMediaSource : IMediaSource
{
    public const string SpotifyBusName = "org.mpris.MediaPlayer2.spotify";
    private const string PlayerPath = "/org/mpris/MediaPlayer2";
    private static readonly TimeSpan CorrectionInterval = TimeSpan.FromMilliseconds(1500);

    private readonly string _busName;
    private readonly PlaybackClock _clock = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Action<string>? _log;
    private Connection? _connection;
    private IMprisPlayer? _player;
    private IDisposable? _propertiesWatch;
    private IDisposable? _seekedWatch;
    private IDisposable? _ownerWatch;
    private Timer? _correction;
    private string? _trackKey;
    private string? _trackId;
    private bool _disposed;

    public MprisMediaSource(string busName = SpotifyBusName, Action<string>? log = null)
    {
        _busName = busName;
        _log = log;
    }

    public bool HasSession => _player is not null && Status != MediaPlaybackStatus.NoSession;

    public MediaTrack? Track { get; private set; }

    public MediaPlaybackStatus Status { get; private set; } = MediaPlaybackStatus.NoSession;

    public TimeSpan Position => _clock.Position;

    public bool CanSeek { get; private set; }

    public bool CanGoNext { get; private set; }

    public bool CanGoPrevious { get; private set; }

    public bool IsShuffleActive { get; private set; }

    public MediaRepeatMode Repeat { get; private set; }

    public event EventHandler? TrackChanged;

    public event EventHandler? PlaybackChanged;

    public event EventHandler? TimelineChanged;

    public async Task StartAsync()
    {
        _connection = new Connection(Address.Session ?? throw new InvalidOperationException("No D-Bus session bus (DBUS_SESSION_BUS_ADDRESS is not set)."));
        await _connection.ConnectAsync().ConfigureAwait(false);

        // Spotify may start (or quit) after Taskitify: follow the bus name.
        _ownerWatch = await _connection.ResolveServiceOwnerAsync(_busName, OnOwnerChanged).ConfigureAwait(false);
        await AttachAsync().ConfigureAwait(false);

        _correction = new Timer(_ => _ = CorrectAsync(), null, CorrectionInterval, CorrectionInterval);
    }

    public Task PlayPauseAsync() => Call(p => p.PlayPauseAsync());

    public Task NextAsync() => Call(p => p.NextAsync());

    public Task PreviousAsync() => Call(p => p.PreviousAsync());

    public async Task SeekAsync(TimeSpan position)
    {
        if (_player is not { } p || _trackId is null)
            return;
        await Call(_ => p.SetPositionAsync(new ObjectPath(_trackId), (long)(position.TotalMilliseconds * 1000))).ConfigureAwait(false);
        _clock.Sync(position, DateTimeOffset.UtcNow, Status == MediaPlaybackStatus.Playing);
        TimelineChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task ToggleShuffleAsync() => Call(p => p.SetAsync("Shuffle", !IsShuffleActive));

    public Task CycleRepeatAsync() =>
        Call(p => p.SetAsync("LoopStatus", MprisMetadata.ToLoopStatus(MprisMetadata.NextRepeat(Repeat))));

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _correction?.Dispose();
        Detach();
        _ownerWatch?.Dispose();
        _connection?.Dispose();
        _refreshLock.Dispose();
    }

    private async Task Call(Func<IMprisPlayer, Task> action)
    {
        if (_player is not { } p)
            return;
        try
        {
            await action(p).ConfigureAwait(false);
        }
        catch (DBusException ex)
        {
            _log?.Invoke($"MPRIS call failed: {ex.Message}");
        }
    }

    private void OnOwnerChanged(ServiceOwnerChangedEventArgs e)
    {
        if (_disposed)
            return;
        if (string.IsNullOrEmpty(e.NewOwner))
        {
            Detach();
            _ = ClearAsync();
        }
        else
        {
            _ = AttachAsync();
        }
    }

    private async Task AttachAsync()
    {
        if (_connection is null)
            return;
        try
        {
            Detach();
            var player = _connection.CreateProxy<IMprisPlayer>(_busName, PlayerPath);
            // Throws when nobody owns the name (Spotify not running).
            await player.GetAllAsync().ConfigureAwait(false);
            _player = player;
            _propertiesWatch = await player.WatchPropertiesAsync(changes => { _ = RefreshAsync(); }).ConfigureAwait(false);
            _seekedWatch = await player.WatchSeekedAsync(micros => OnSeeked(micros)).ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (DBusException)
        {
            _player = null;
            await ClearAsync().ConfigureAwait(false);
        }
    }

    private void Detach()
    {
        _propertiesWatch?.Dispose();
        _seekedWatch?.Dispose();
        _propertiesWatch = null;
        _seekedWatch = null;
        _player = null;
    }

    private async Task ClearAsync()
    {
        await _refreshLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var hadTrack = Track is not null;
            Track = null;
            _trackKey = null;
            _trackId = null;
            Status = MediaPlaybackStatus.NoSession;
            _clock.SetPlaying(false);
            _clock.Reset();
            if (hadTrack)
                TrackChanged?.Invoke(this, EventArgs.Empty);
            PlaybackChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void OnSeeked(long micros)
    {
        _clock.Sync(TimeSpan.FromTicks(micros * 10), DateTimeOffset.UtcNow, Status == MediaPlaybackStatus.Playing);
        TimelineChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Poll the position while playing: MPRIS never signals it, so a missed seek would drift.</summary>
    private async Task CorrectAsync()
    {
        if (_disposed || _player is not { } p || Status != MediaPlaybackStatus.Playing)
            return;
        try
        {
            var raw = await p.GetAsync("Position").ConfigureAwait(false);
            if (raw is long micros)
            {
                var reported = TimeSpan.FromTicks(micros * 10);
                if ((reported - _clock.Position).Duration() > TimeSpan.FromMilliseconds(400))
                {
                    _clock.Sync(reported, DateTimeOffset.UtcNow, playing: true);
                    TimelineChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (DBusException)
        {
            // The player went away; the owner watch reports it.
        }
    }

    private async Task RefreshAsync()
    {
        if (_disposed || _player is not { } p)
            return;
        await _refreshLock.WaitAsync().ConfigureAwait(false);
        try
        {
            MprisState props;
            try
            {
                props = MprisState.From(await p.GetAllAsync().ConfigureAwait(false));
            }
            catch (DBusException)
            {
                return;
            }

            var status = MprisMetadata.ParseStatus(props.PlaybackStatus);
            var track = MprisMetadata.ParseTrack(props.Metadata);
            var key = track is null ? null : MprisMetadata.TrackId(props.Metadata) ?? $"{track.Title}|{track.Artist}|{track.Album}";
            _trackId = MprisMetadata.TrackId(props.Metadata);

            var trackChanged = key != _trackKey;
            var playbackChanged = status != Status
                || props.Shuffle != IsShuffleActive
                || MprisMetadata.ParseLoop(props.LoopStatus) != Repeat
                || props.CanGoNext != CanGoNext
                || props.CanGoPrevious != CanGoPrevious;
            var durationChanged = track?.Duration != Track?.Duration;

            _trackKey = key;
            Track = track;
            Status = track is null && status == MediaPlaybackStatus.Stopped ? MediaPlaybackStatus.Stopped : status;
            CanSeek = props.CanSeek;
            CanGoNext = props.CanGoNext;
            CanGoPrevious = props.CanGoPrevious;
            IsShuffleActive = props.Shuffle;
            Repeat = MprisMetadata.ParseLoop(props.LoopStatus);

            _clock.Duration = track?.Duration;
            _clock.Sync(TimeSpan.FromTicks(props.Position * 10), DateTimeOffset.UtcNow, status == MediaPlaybackStatus.Playing);

            if (trackChanged)
                TrackChanged?.Invoke(this, EventArgs.Empty);
            if (playbackChanged)
                PlaybackChanged?.Invoke(this, EventArgs.Empty);
            if (trackChanged || playbackChanged || durationChanged)
                TimelineChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
