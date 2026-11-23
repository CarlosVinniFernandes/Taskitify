using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Taskitify.Infrastructure;

/// <summary>Reads and writes .lnk shortcuts through the shell's own ShellLink object.</summary>
internal static class ShellLink
{
    /// <summary>A shortcut that runs <paramref name="exePath"/>, with its icon.</summary>
    public static void SaveForFile(string lnkPath, string exePath, string description)
    {
        var link = Create();
        try
        {
            link.SetPath(exePath);
            link.SetWorkingDirectory(Path.GetDirectoryName(exePath) ?? "");
            link.SetIconLocation(exePath, 0);
            link.SetDescription(description);
            ((IPersistFile)link).Save(lnkPath, true);
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    /// <summary>
    /// A shortcut to a packaged app, the same one Windows makes when the app is dragged from Start:
    /// it opens whichever version is installed and takes the app's icon.
    /// </summary>
    public static void SaveForApp(string lnkPath, string appUserModelId, string description)
    {
        var pidl = ParseAppsFolderItem(appUserModelId);
        var link = Create();
        try
        {
            link.SetIDList(pidl);
            link.SetDescription(description);
            ((IPersistFile)link).Save(lnkPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
            Marshal.FreeCoTaskMem(pidl);
        }
    }

    /// <summary>The file a shortcut opens, or "" when it opens something else (a packaged app).</summary>
    public static string ReadFilePath(string lnkPath)
    {
        var link = Load(lnkPath);
        try
        {
            var path = new StringBuilder(1024);
            link.GetPath(path, path.Capacity, IntPtr.Zero, 0x4 /* SLGP_RAWPATH */);
            return path.ToString();
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    /// <summary>Whether a shortcut opens the packaged app <paramref name="appUserModelId"/>.</summary>
    public static bool OpensApp(string lnkPath, string appUserModelId)
    {
        var link = Load(lnkPath);
        var wanted = ParseAppsFolderItem(appUserModelId);
        try
        {
            link.GetIDList(out var pidl);
            try { return pidl != IntPtr.Zero && ILIsEqual(pidl, wanted); }
            finally { Marshal.FreeCoTaskMem(pidl); }
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
            Marshal.FreeCoTaskMem(wanted);
        }
    }

    private static IShellLinkW Load(string lnkPath)
    {
        var link = Create();
        try { ((IPersistFile)link).Load(lnkPath, 0); }
        catch { Marshal.FinalReleaseComObject(link); throw; }
        return link;
    }

    private static IShellLinkW Create() =>
        (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!)!;

    private static IntPtr ParseAppsFolderItem(string appUserModelId)
    {
        Marshal.ThrowExceptionForHR(SHParseDisplayName(@"shell:AppsFolder\" + appUserModelId, IntPtr.Zero, out var pidl, 0, out _));
        return pidl;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ILIsEqual(IntPtr pidl1, IntPtr pidl2);

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxIconPath, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pathRel, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
