namespace Taskitify.Infrastructure;

/// <summary>The official addresses, in one place (the website, its community pages, shared lyrics).</summary>
public static class OfficialLinks
{
    public const string Website = "https://taskitify.vercel.app/";
    public const string Community = "https://taskitify.vercel.app/community";
    public const string SuggestSong = "https://taskitify.vercel.app/community#suggest";
    public const string LyricsShare = "https://taskitify.vercel.app/s";
    public const string Repository = "https://github.com/CarlosVinniFernandes/Taskitify";

    /// <summary>Opens an address in the default browser; failures are logged, never thrown.</summary>
    public static void Open(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error($"Could not open {url}", ex);
        }
    }
}
