using System.Windows.Interop;
using System.Windows.Media;

namespace Taskitify.Infrastructure;

/// <summary>
/// The app renders in software to keep the GPU driver's memory out of the process; windows that
/// redraw large areas (the mini player while open, the lyric card maker) use the GPU while
/// they are on screen. Counted, so one closing does not switch another back to software.
/// </summary>
internal static class GpuRendering
{
    private static int _users;

    public static void Acquire()
    {
        if (_users++ == 0)
            RenderOptions.ProcessRenderMode = RenderMode.Default;
    }

    public static void Release()
    {
        if (_users > 0 && --_users == 0)
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
    }
}
