using System.IO;

namespace Taskitify.Services;

/// <summary>
/// Decides whether a pointer to Taskitify (the desktop shortcut, the "start with Windows" entry)
/// should move to the copy that is running now. Several portable copies can live side by side, one
/// folder per release: the newest one wins, whichever of them was opened last.
/// </summary>
public static class LatestVersionPolicy
{
    /// <param name="currentTarget">
    /// The file the pointer opens now: null when there is none, "" when it opens something that is
    /// not a file (the Microsoft Store app, which updates itself and is left alone).
    /// </param>
    /// <param name="versionOf">The version of an executable, or null when it is gone or unreadable.</param>
    public static bool ShouldPointHere(string? currentTarget, string thisExe, Version thisVersion, Func<string, Version?> versionOf)
    {
        if (currentTarget is null) return true;
        if (currentTarget.Length == 0) return false;
        if (string.Equals(Path.GetFullPath(currentTarget), Path.GetFullPath(thisExe), StringComparison.OrdinalIgnoreCase)) return false;
        return versionOf(currentTarget) is not { } other || other <= thisVersion;
    }

    /// <summary>The version of an executable on disk, or null when it is gone or has none.</summary>
    public static Version? FileVersion(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
        }
        catch (Exception) { return null; }
    }
}
