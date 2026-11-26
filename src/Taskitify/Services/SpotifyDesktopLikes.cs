using System.Diagnostics;
using System.Windows.Automation;
using Taskitify.Infrastructure;

namespace Taskitify.Services;

/// <summary>
/// Reads Spotify's own "now playing" bar through UI Automation (accessibility, the way a screen
/// reader does): whether the song playing is in Liked Songs, as Spotify itself shows it, and the
/// album it is playing from. Clicking its "add to Liked Songs" button likes exactly that track.
/// </summary>
/// <remarks>
/// Spotify's window is not ours: everything here can fail (window hidden to the tray, another
/// layout, another language), and then returns null so the caller falls back to the web API.
/// The search for the bar (~80 ms) runs once per song; reading the button again is a few ms.
/// Calls block, so callers run them off the UI thread.
/// </remarks>
public sealed class SpotifyDesktopLikes
{
    // "Liked Songs" in the languages Spotify's button names use it ("Add to Liked Songs").
    private static readonly string[] LikedSongsNames =
    [
        "Liked Songs", "Músicas curtidas", "Canciones que te gustan", "Tus me gusta", "Titres likés", "Lieblingssongs",
        "Brani che ti piacciono", "Gelikete nummers", "Polubione utwory", "Gillade låtar", "Likte sanger", "Beğenilen Şarkılar",
        "Понравившиеся треки", "お気に入りの曲", "좋아요 표시한 곡", "已点赞的歌曲", "Lagu yang Disukai",
    ];

    private readonly object _gate = new();
    private AutomationElement? _region;
    private AutomationElement? _button;
    private string? _regionTitle;
    private string? _albumId;

    public sealed record State(bool Liked, string? AlbumId);

    /// <summary>The song's state in Spotify's bar, or null when it cannot be read.</summary>
    public State? Read(string title)
    {
        lock (_gate)
        {
            try
            {
                if (_button is null || !string.Equals(_regionTitle, title, StringComparison.Ordinal) || !RegionStillShows(title))
                    Find(title);
                if (_button is null)
                    return null;
                var liked = IsLikedName(_button.Current.Name);
                return liked is null ? null : new State(liked.Value, _albumId);
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                Forget();
                return null;
            }
        }
    }

    /// <summary>Click Spotify's own "add to Liked Songs" for the song playing. False when it could not.</summary>
    public bool Like(string title)
    {
        lock (_gate)
        {
            try
            {
                if (_button is null || _regionTitle != title)
                    Find(title);
                if (_button is null || IsLikedName(_button.Current.Name) != false)
                    return false;
                ((InvokePattern)_button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                return true;
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                Forget();
                return false;
            }
        }
    }

    /// <summary>
    /// Liked: Spotify then offers "Add to playlist" (the check mark opens the playlist chooser);
    /// not liked: "Add to Liked Songs". Null for anything else.
    /// </summary>
    internal static bool? IsLikedName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        if (LikedSongsNames.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (name.Contains("playlist", StringComparison.OrdinalIgnoreCase))
            return true;
        return null;
    }

    private bool RegionStillShows(string title)
    {
        var name = _region?.Current.Name;
        return name is not null && name.Contains(title, StringComparison.OrdinalIgnoreCase);
    }

    private string? AlbumId()
    {
        if (_region is null)
            return null;
        foreach (AutomationElement link in _region.FindAll(TreeScope.Descendants,
                     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink)))
        {
            if (!link.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
                continue;
            var url = ((ValuePattern)pattern).Current.Value;
            var at = url.IndexOf("/album/", StringComparison.Ordinal);
            if (at < 0)
                continue;
            var id = new string(url[(at + 7)..].TakeWhile(char.IsAsciiLetterOrDigit).ToArray());
            if (id.Length == 22)
                return id;
        }
        return null;
    }

    private void Forget()
    {
        _region = null;
        _button = null;
        _regionTitle = null;
        _albumId = null;
    }

    /// <summary>The bar's region (its name is "Now playing: title by artist") and its like button.</summary>
    private void Find(string title)
    {
        Forget();
        var clock = Stopwatch.StartNew();
        foreach (var window in SpotifyWindows())
        {
            var document = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
            if (document is null)
                continue;
            // Breadth-first and shallow: the bar sits a few levels under the page, far above the lists.
            var walker = TreeWalker.ControlViewWalker;
            var queue = new Queue<(AutomationElement Element, int Depth)>();
            queue.Enqueue((document, 0));
            while (queue.Count > 0)
            {
                var (element, depth) = queue.Dequeue();
                for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
                {
                    if (child.Current.ControlType == ControlType.Group
                        && child.Current.Name.Contains(title, StringComparison.OrdinalIgnoreCase)
                        && child.Current.BoundingRectangle.Height < 160
                        && LikeButton(child) is { } button)
                    {
                        _region = child;
                        _button = button;
                        _regionTitle = title;
                        _albumId = AlbumId();
                        Log.Debug($"Spotify bar found in {clock.ElapsedMilliseconds} ms");
                        return;
                    }
                    if (depth < 5)
                        queue.Enqueue((child, depth + 1));
                }
            }
        }
    }

    private static AutomationElement? LikeButton(AutomationElement region)
    {
        foreach (AutomationElement button in region.FindAll(TreeScope.Descendants,
                     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
        {
            if (IsLikedName(button.Current.Name) is not null)
                return button;
        }
        return null;
    }

    private static IEnumerable<AutomationElement> SpotifyWindows()
    {
        foreach (var process in Process.GetProcessesByName("Spotify"))
        {
            using (process)
            {
                if (process.MainWindowHandle == IntPtr.Zero)
                    continue;
                AutomationElement? window = null;
                try { window = AutomationElement.FromHandle(process.MainWindowHandle); }
                catch (Exception ex) when (ex is ElementNotAvailableException or ArgumentException) { }
                if (window is not null)
                    yield return window;
            }
        }
    }
}
