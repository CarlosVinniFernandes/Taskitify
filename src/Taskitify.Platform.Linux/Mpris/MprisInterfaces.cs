using Tmds.DBus;

namespace Taskitify.Platform.Linux.Mpris;

[DBusInterface("org.mpris.MediaPlayer2.Player")]
public interface IMprisPlayer : IDBusObject
{
    Task NextAsync();

    Task PreviousAsync();

    Task PauseAsync();

    Task PlayPauseAsync();

    Task PlayAsync();

    Task SeekAsync(long offsetMicroseconds);

    Task SetPositionAsync(ObjectPath trackId, long positionMicroseconds);

    Task<object> GetAsync(string prop);

    Task<IDictionary<string, object>> GetAllAsync();

    Task SetAsync(string prop, object val);

    Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler);

    Task<IDisposable> WatchSeekedAsync(Action<long> handler);
}
