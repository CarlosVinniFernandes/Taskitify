using Taskitify.Core.Media;
using Taskitify.Platform.Linux.Mpris;
using Tmds.DBus;

namespace Taskitify.Platform.Linux.Tests;

/// <summary>
/// Runs <see cref="MprisMediaSource"/> against a fake MPRIS player registered on the real D-Bus
/// session bus. Needs a session bus (<c>dbus-run-session -- dotnet test</c> in CI); without one the
/// tests return early, because there is no portable way to skip from xunit 2 without a package.
/// </summary>
public sealed class MprisIntegrationTests
{
    private sealed class FakePlayer : IMprisPlayer
    {
        private readonly List<Action<PropertyChanges>> _propertyHandlers = [];
        private readonly List<Action<long>> _seekedHandlers = [];

        public ObjectPath ObjectPath { get; } = new("/org/mpris/MediaPlayer2");

        public string PlaybackStatus { get; set; } = "Playing";

        public string LoopStatus { get; set; } = "None";

        public bool Shuffle { get; set; }

        public long PositionMicros { get; set; } = 12_000_000;

        public IDictionary<string, object> Metadata { get; set; } = Meta("Song", "Artist", "track1", 200_000_000);

        public int PlayPauseCalls { get; private set; }

        public int NextCalls { get; private set; }

        public (string Track, long Micros)? LastSetPosition { get; private set; }

        public static IDictionary<string, object> Meta(string title, string artist, string id, long length) => new Dictionary<string, object>
        {
            ["mpris:trackid"] = new ObjectPath("/com/spotify/track/" + id),
            ["mpris:length"] = length,
            ["xesam:title"] = title,
            ["xesam:artist"] = new[] { artist },
            ["xesam:album"] = "Album",
        };

        public void Changed(params string[] names)
        {
            var changes = new PropertyChanges([], names);
            foreach (var h in _propertyHandlers.ToArray())
                h(changes);
        }

        public void Seeked(long micros)
        {
            foreach (var h in _seekedHandlers.ToArray())
                h(micros);
        }

        public Task NextAsync() { NextCalls++; return Task.CompletedTask; }

        public Task PreviousAsync() => Task.CompletedTask;

        public Task PauseAsync() => Task.CompletedTask;

        public Task PlayPauseAsync() { PlayPauseCalls++; return Task.CompletedTask; }

        public Task PlayAsync() => Task.CompletedTask;

        public Task SeekAsync(long offsetMicroseconds) => Task.CompletedTask;

        public Task SetPositionAsync(ObjectPath trackId, long positionMicroseconds)
        {
            LastSetPosition = (trackId.ToString(), positionMicroseconds);
            PositionMicros = positionMicroseconds;
            return Task.CompletedTask;
        }

        public Task<object> GetAsync(string prop) => Task.FromResult<object>(prop switch
        {
            "Position" => PositionMicros,
            "PlaybackStatus" => PlaybackStatus,
            _ => throw new ArgumentException(prop),
        });

        public Task<IDictionary<string, object>> GetAllAsync() => Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
        {
            ["PlaybackStatus"] = PlaybackStatus,
            ["LoopStatus"] = LoopStatus,
            ["Shuffle"] = Shuffle,
            ["Metadata"] = Metadata,
            ["Position"] = PositionMicros,
            ["CanGoNext"] = true,
            ["CanGoPrevious"] = true,
            ["CanPlay"] = true,
            ["CanPause"] = true,
            ["CanSeek"] = true,
        });

        public Task SetAsync(string prop, object val)
        {
            if (prop == "Shuffle")
                Shuffle = (bool)val;
            else if (prop == "LoopStatus")
                LoopStatus = (string)val;
            Changed(prop);
            return Task.CompletedTask;
        }

        public Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler)
        {
            _propertyHandlers.Add(handler);
            return Task.FromResult<IDisposable>(new Unsubscribe(() => _propertyHandlers.Remove(handler)));
        }

        public Task<IDisposable> WatchSeekedAsync(Action<long> handler)
        {
            _seekedHandlers.Add(handler);
            return Task.FromResult<IDisposable>(new Unsubscribe(() => _seekedHandlers.Remove(handler)));
        }

        private sealed class Unsubscribe(Action action) : IDisposable
        {
            public void Dispose() => action();
        }
    }

    /// <summary>
    /// Whether a session bus answers. CI sets TASKITIFY_REQUIRE_DBUS=1 so that "no bus" fails the test
    /// instead of silently skipping it.
    /// </summary>
    private static async Task<bool> HasSessionBusAsync()
    {
        var available = false;
        if (Address.Session is { } address)
        {
            try
            {
                using var probe = new Connection(address);
                await probe.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(3));
                available = true;
            }
            catch (Exception ex) when (ex is ConnectException or IOException or TimeoutException or DBusException)
            {
            }
        }

        if (!available && Environment.GetEnvironmentVariable("TASKITIFY_REQUIRE_DBUS") == "1")
            Assert.Fail("TASKITIFY_REQUIRE_DBUS=1 but there is no D-Bus session bus (run under dbus-run-session).");
        return available;
    }

    private static async Task<bool> WaitFor(Func<bool> condition, int ms = 5000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end)
        {
            if (condition())
                return true;
            await Task.Delay(20);
        }
        return condition();
    }

    private static async Task<(Connection Conn, FakePlayer Player, string Name)> StartFakeAsync()
    {
        var name = "org.mpris.MediaPlayer2.taskitifytest" + Guid.NewGuid().ToString("N")[..12];
        var conn = new Connection(Address.Session!);
        await conn.ConnectAsync();
        var player = new FakePlayer();
        await conn.RegisterObjectAsync(player);
        await conn.RegisterServiceAsync(name);
        return (conn, player, name);
    }

    [Fact]
    public async Task Reads_state_follows_changes_and_sends_commands()
    {
        if (!await HasSessionBusAsync())
            return;
        var (conn, player, name) = await StartFakeAsync();
        using var _ = conn;
        using var source = new MprisMediaSource(name);
        var trackChanges = 0;
        source.TrackChanged += (_, _) => Interlocked.Increment(ref trackChanges);

        await source.StartAsync();

        Assert.True(source.HasSession);
        Assert.Equal(MediaPlaybackStatus.Playing, source.Status);
        Assert.Equal(("Song", "Artist"), (source.Track!.Title, source.Track.Artist));
        Assert.Equal(TimeSpan.FromSeconds(200), source.Track.Duration);
        Assert.True(source.Position >= TimeSpan.FromSeconds(12));
        Assert.True(source.CanSeek && source.CanGoNext);
        Assert.Equal(1, trackChanges);

        // Commands reach the player.
        await source.PlayPauseAsync();
        await source.NextAsync();
        Assert.Equal((1, 1), (player.PlayPauseCalls, player.NextCalls));

        // Shuffle and repeat round-trip through the Set + PropertiesChanged path.
        await source.ToggleShuffleAsync();
        Assert.True(await WaitFor(() => source.IsShuffleActive));
        await source.CycleRepeatAsync();
        Assert.True(await WaitFor(() => source.Repeat == MediaRepeatMode.List));

        // A new song: metadata changes and the player signals it.
        player.Metadata = FakePlayer.Meta("Other", "Someone", "track2", 100_000_000);
        player.PositionMicros = 0;
        player.Changed("Metadata");
        Assert.True(await WaitFor(() => source.Track?.Title == "Other"));
        Assert.Equal(2, Volatile.Read(ref trackChanges));

        // Pausing is reported.
        player.PlaybackStatus = "Paused";
        player.Changed("PlaybackStatus");
        Assert.True(await WaitFor(() => source.Status == MediaPlaybackStatus.Paused));

        // Seeking from the app reaches the player with the current track id and moves the clock.
        await source.SeekAsync(TimeSpan.FromSeconds(42));
        Assert.Equal(("/com/spotify/track/track2", 42_000_000L), player.LastSetPosition);
        Assert.Equal(TimeSpan.FromSeconds(42), source.Position);

        // A seek from another client arrives as the Seeked signal.
        player.Seeked(7_000_000);
        Assert.True(await WaitFor(() => source.Position == TimeSpan.FromSeconds(7)));
    }

    [Fact]
    public async Task Reports_no_session_when_the_player_is_absent_and_attaches_when_it_appears()
    {
        if (!await HasSessionBusAsync())
            return;
        var name = "org.mpris.MediaPlayer2.taskitifytest" + Guid.NewGuid().ToString("N")[..12];
        using var source = new MprisMediaSource(name);
        await source.StartAsync();
        Assert.False(source.HasSession);
        Assert.Equal(MediaPlaybackStatus.NoSession, source.Status);

        var conn = new Connection(Address.Session!);
        await conn.ConnectAsync();
        await conn.RegisterObjectAsync(new FakePlayer());
        await conn.RegisterServiceAsync(name);

        Assert.True(await WaitFor(() => source.HasSession));
        Assert.Equal("Song", source.Track!.Title);

        // The player quits.
        conn.Dispose();
        Assert.True(await WaitFor(() => !source.HasSession));
        Assert.Null(source.Track);
    }
}
