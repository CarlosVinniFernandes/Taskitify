using System.IO;
using Taskitify.Infrastructure;
using Windows.ApplicationModel;

namespace Taskitify.Services;

/// <summary>
/// Keeps "Taskitify" on the desktop opening the latest copy of the app. The Microsoft Store version
/// links to the installed app, which Windows keeps current; a portable copy points the shortcut at
/// itself unless it already opens a newer one (see <see cref="LatestVersionPolicy"/>).
/// </summary>
public static class DesktopShortcutService
{
    private const string FileName = "Taskitify.lnk";
    private const string Description = "Taskitify";

    /// <summary>
    /// Development builds and measurement runs (which use their own data folder) leave the
    /// shortcut of the installed copy alone.
    /// </summary>
    public static bool IsManagedHere =>
#if DEBUG
        false;
#else
        !AppPaths.IsIsolated;
#endif

    private static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), FileName);

    /// <summary>Creates or updates the shortcut, or removes it when <paramref name="enabled"/> is false.</summary>
    public static void Apply(bool enabled)
    {
        if (!IsManagedHere) return;
        try
        {
            if (enabled) Ensure();
            else Remove();
        }
        catch (Exception ex)
        {
            Log.Error("Could not update the desktop shortcut", ex);
        }
    }

    private static void Ensure()
    {
        var lnk = ShortcutPath;
        if (StartupService.IsPackaged)
        {
            var app = AppUserModelId;
            if (File.Exists(lnk) && ShellLink.OpensApp(lnk, app)) return;
            ShellLink.SaveForApp(lnk, app, Description);
            Log.Info("Desktop shortcut now opens the installed app");
            return;
        }

        var exe = Environment.ProcessPath;
        var version = typeof(App).Assembly.GetName().Version;
        if (exe is null || version is null) return;
        var current = File.Exists(lnk) ? ShellLink.ReadFilePath(lnk) : null;
        if (!LatestVersionPolicy.ShouldPointHere(current, exe, version, LatestVersionPolicy.FileVersion)) return;
        ShellLink.SaveForFile(lnk, exe, Description);
        Log.Info($"Desktop shortcut now opens {exe}");
    }

    private static void Remove()
    {
        var lnk = ShortcutPath;
        if (!File.Exists(lnk)) return;
        // Only a shortcut that opens Taskitify: never one the user made for something else.
        var target = ShellLink.ReadFilePath(lnk);
        var ours = target.Length == 0
            ? StartupService.IsPackaged && ShellLink.OpensApp(lnk, AppUserModelId)
            : string.Equals(Path.GetFileName(target), "Taskitify.exe", StringComparison.OrdinalIgnoreCase);
        if (ours) File.Delete(lnk);
    }

    private static string AppUserModelId => Package.Current.Id.FamilyName + "!Taskitify";
}
