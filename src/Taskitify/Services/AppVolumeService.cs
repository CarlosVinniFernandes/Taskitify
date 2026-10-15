using System.Diagnostics;
using System.Runtime.InteropServices;
using Taskitify.Infrastructure;

namespace Taskitify.Services;

/// <summary>
/// Spotify's volume in the Windows volume mixer (per-app session volume on the default output
/// device). Spotify's own in-app slider is separate and not reachable without its Web API.
/// Only used on demand (hover, click, wheel), so there is no polling.
/// </summary>
public sealed class AppVolumeService
{
    private const int eRender = 0;
    private const int eMultimedia = 1;
    private const int CLSCTX_ALL = 23;

    private static readonly Guid SessionManagerIid = typeof(IAudioSessionManager2).GUID;

    private Guid _eventContext = Guid.NewGuid();

    /// <summary>Volume 0..1 and mute state, or null when Spotify has no audio session yet.</summary>
    public (float Volume, bool Muted)? Get()
    {
        var volume = Find();
        if (volume is null)
            return null;
        try
        {
            volume.GetMasterVolume(out var level);
            volume.GetMute(out var muted);
            return (level, muted);
        }
        finally
        {
            Marshal.ReleaseComObject(volume);
        }
    }

    public void SetVolume(float level)
    {
        var volume = Find();
        if (volume is null)
            return;
        try
        {
            volume.SetMasterVolume(Math.Clamp(level, 0f, 1f), ref _eventContext);
            if (level > 0)
                volume.SetMute(false, ref _eventContext);
        }
        finally
        {
            Marshal.ReleaseComObject(volume);
        }
    }

    public void ToggleMute()
    {
        var volume = Find();
        if (volume is null)
            return;
        try
        {
            volume.GetMute(out var muted);
            volume.SetMute(!muted, ref _eventContext);
        }
        finally
        {
            Marshal.ReleaseComObject(volume);
        }
    }

    private ISimpleAudioVolume? Find()
    {
        var spotifyPids = Process.GetProcessesByName("Spotify").Select(p =>
        {
            using (p)
                return (uint)p.Id;
        }).ToHashSet();
        if (spotifyPids.Count == 0)
            return null;

        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioSessionManager2? manager = null;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out device) != 0)
                return null;
            var iid = SessionManagerIid;
            if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var obj) != 0)
                return null;
            manager = (IAudioSessionManager2)obj;
            if (manager.GetSessionEnumerator(out sessions) != 0)
                return null;
            sessions.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out var control) != 0)
                    continue;
                if (control.GetProcessId(out var pid) == 0 && spotifyPids.Contains(pid))
                    return (ISimpleAudioVolume)control; // QueryInterface on the same session object
                Marshal.ReleaseComObject(control);
            }
            return null;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log.Warn($"Audio session lookup failed: {ex.Message}");
            return null;
        }
        finally
        {
            foreach (var com in new object?[] { sessions, manager, device, enumerator })
            {
                if (com is not null)
                    Marshal.ReleaseComObject(com);
            }
        }
    }
}
