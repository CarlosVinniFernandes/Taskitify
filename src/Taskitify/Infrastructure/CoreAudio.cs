using System.Runtime.InteropServices;

namespace Taskitify.Infrastructure;

// Minimal Core Audio (WASAPI session) COM interop: just enough to find an app's audio
// session on the default output device and read/change its mixer volume.

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
}

[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    [PreserveSig]
    int GetAudioSessionControl(IntPtr sessionGuid, int streamFlags, out IntPtr sessionControl);

    [PreserveSig]
    int GetSimpleAudioVolume(IntPtr sessionGuid, int streamFlags, out IntPtr audioVolume);

    [PreserveSig]
    int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
}

[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    [PreserveSig]
    int GetCount(out int count);

    [PreserveSig]
    int GetSession(int index, out IAudioSessionControl2 session);
}

[ComImport]
[Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // IAudioSessionControl
    int GetState(out int state);
    int GetDisplayName(out IntPtr name);
    int SetDisplayName(IntPtr value, ref Guid eventContext);
    int GetIconPath(out IntPtr path);
    int SetIconPath(IntPtr value, ref Guid eventContext);
    int GetGroupingParam(out Guid groupingParam);
    int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
    int RegisterAudioSessionNotification(IntPtr client);
    int UnregisterAudioSessionNotification(IntPtr client);

    // IAudioSessionControl2
    int GetSessionIdentifier(out IntPtr id);
    int GetSessionInstanceIdentifier(out IntPtr id);

    [PreserveSig]
    int GetProcessId(out uint pid);
}

[ComImport]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")] // audioclient.h
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    [PreserveSig]
    int SetMasterVolume(float level, ref Guid eventContext);

    [PreserveSig]
    int GetMasterVolume(out float level);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}
