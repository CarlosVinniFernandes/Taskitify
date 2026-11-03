using System.Diagnostics;

namespace Taskitify.Platform.Linux;

/// <summary>The GNOME Shell extension in <c>gnome-extension/</c> that shows the lyric line in the top bar.</summary>
public static class GnomeExtension
{
    public const string Uuid = "taskitify-lyrics@carlosvinnifernandes.github.io";

    /// <summary>
    /// True when <paramref name="enabledExtensions"/> (the output of
    /// <c>gsettings get org.gnome.shell enabled-extensions</c>, e.g. <c>['a@b', 'c@d']</c>) lists it.
    /// </summary>
    public static bool IsListed(string? enabledExtensions) =>
        enabledExtensions is not null
        && enabledExtensions.Split(['[', ']', ',', ' ', '\'', '"', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Contains(Uuid, StringComparer.Ordinal);

    /// <summary>Ask GNOME whether the extension is enabled. False when gsettings is missing or slow.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("gsettings")
            {
                ArgumentList = { "get", "org.gnome.shell", "enabled-extensions" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null)
                return false;
            if (!process.WaitForExit(2000))
            {
                process.Kill();
                return false;
            }
            return process.ExitCode == 0 && IsListed(process.StandardOutput.ReadToEnd());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
