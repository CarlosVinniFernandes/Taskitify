using System.Diagnostics;
using System.Runtime.InteropServices;
using Taskitify.Infrastructure;

namespace Taskitify.Services;

/// <summary>
/// The peak level of Spotify's audio sessions (on every active output device), read from the
/// Windows volume mixer's meters. It tells play/pause seconds before Spotify's media session does.
/// Use from one thread (the UI thread).
/// </summary>
public sealed class SpotifyAudioMeter : IDisposable
{
    private const int eRender = 0;
    private const int DEVICE_STATE_ACTIVE = 1;
    private const int CLSCTX_ALL = 23;

    // Spotify opens its audio session on its first sound: while none is found, look again this often.
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

    // Silent sessions may be stale ones (Spotify moved to another output device and opened a new
    // session there): while all is silent, look for new sessions this often.
    private static readonly TimeSpan SilentRefreshInterval = TimeSpan.FromSeconds(10);

    private static readonly Guid SessionManagerIid = typeof(IAudioSessionManager2).GUID;

    private readonly List<IAudioMeterInformation> _meters = [];
    private DateTime _lastLookup = DateTime.MinValue;
    private bool _silent;

    /// <summary>The highest peak (0..1) among Spotify's sessions, or null when it has none.</summary>
    public float? ReadPeak()
    {
        if (_meters.Count == 0)
        {
            if (DateTime.UtcNow - _lastLookup < RetryInterval)
                return null;
            Refresh();
            if (_meters.Count == 0)
                return null;
        }
        else if (_silent && DateTime.UtcNow - _lastLookup >= SilentRefreshInterval)
        {
            Refresh();
            if (_meters.Count == 0)
                return null;
        }

        var peak = 0f;
        try
        {
            foreach (var meter in _meters)
            {
                if (meter.GetPeakValue(out var value) == 0 && value > peak)
                    peak = value;
            }
            _silent = peak <= 0;
            return peak;
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException)
        {
            // The device or the session went away: find them again next time.
            Release();
            return null;
        }
    }

    /// <summary>Find Spotify's sessions again (Spotify restarted, or the output device changed).</summary>
    public void Refresh()
    {
        Release();
        _lastLookup = DateTime.UtcNow;
        var spotifyPids = Process.GetProcessesByName("Spotify").Select(p =>
        {
            using (p)
                return (uint)p.Id;
        }).ToHashSet();
        if (spotifyPids.Count == 0)
            return;

        var com = new List<object>();
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            com.Add(enumerator);
            if (enumerator.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, out var devices) != 0)
                return;
            com.Add(devices);
            devices.GetCount(out var deviceCount);
            for (var d = 0; d < deviceCount; d++)
            {
                if (devices.Item(d, out var device) != 0)
                    continue;
                com.Add(device);
                var iid = SessionManagerIid;
                if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var obj) != 0)
                    continue;
                var manager = (IAudioSessionManager2)obj;
                com.Add(manager);
                if (manager.GetSessionEnumerator(out var sessions) != 0)
                    continue;
                com.Add(sessions);
                sessions.GetCount(out var count);
                for (var i = 0; i < count; i++)
                {
                    if (sessions.GetSession(i, out var control) != 0)
                        continue;
                    if (control.GetProcessId(out var pid) == 0 && spotifyPids.Contains(pid))
                        _meters.Add((IAudioMeterInformation)control); // QueryInterface on the same session object
                    else
                        Marshal.ReleaseComObject(control);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log.Warn($"Audio meter lookup failed: {ex.Message}");
            Release();
        }
        finally
        {
            for (var i = com.Count - 1; i >= 0; i--)
                Marshal.ReleaseComObject(com[i]);
        }
    }

    public void Dispose() => Release();

    private void Release()
    {
        foreach (var meter in _meters)
            Marshal.ReleaseComObject(meter);
        _meters.Clear();
    }
}
