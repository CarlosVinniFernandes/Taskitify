namespace Taskitify.Core.Media;

public enum MediaPlaybackStatus
{
    NoSession,
    Stopped,
    Paused,
    Playing,
}

public enum MediaRepeatMode
{
    None,
    List,
    Track,
}

/// <param name="ArtUrl">http(s) or file URL of the cover, when the player publishes one.</param>
public sealed record MediaTrack(string Title, string Artist, string Album, TimeSpan? Duration, string? ArtUrl);

/// <summary>
/// The media player the app follows (Spotify Desktop), without any UI or OS types. The Windows app
/// reads it from the system media session (GSMTC); the Linux app from MPRIS over D-Bus.
/// Events may be raised from any thread; UI code marshals them itself.
/// </summary>
public interface IMediaSource : IDisposable
{
    bool HasSession { get; }

    MediaTrack? Track { get; }

    MediaPlaybackStatus Status { get; }

    /// <summary>Locally projected position (see <see cref="Lyrics.PlaybackClock"/>).</summary>
    TimeSpan Position { get; }

    bool CanSeek { get; }

    bool CanGoNext { get; }

    bool CanGoPrevious { get; }

    bool IsShuffleActive { get; }

    MediaRepeatMode Repeat { get; }

    /// <summary>A different song (title/artist/album).</summary>
    event EventHandler? TrackChanged;

    /// <summary>Playing/paused/stopped, or the session appeared or went away.</summary>
    event EventHandler? PlaybackChanged;

    /// <summary>The position base changed (seek, fresh report) or the duration became known.</summary>
    event EventHandler? TimelineChanged;

    Task StartAsync();

    Task PlayPauseAsync();

    Task NextAsync();

    Task PreviousAsync();

    Task SeekAsync(TimeSpan position);

    Task ToggleShuffleAsync();

    /// <summary>None → List → Track → None, like Spotify's repeat button.</summary>
    Task CycleRepeatAsync();
}
