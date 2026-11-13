namespace Taskitify.Platform.Linux.Spotify;

/// <summary>One call the web player made to Spotify's API, as the page sent it.</summary>
/// <param name="Response">Body of the token and GraphQL search responses; null for other calls.</param>
public sealed record WebRequest(string Method, string Url, IReadOnlyDictionary<string, string> Headers, string? Body, int Status, string? Response);

/// <summary>The browser that runs Spotify's web player (WebKitGTK in a helper process).</summary>
public interface IWebPlayer : IDisposable
{
    event Action<WebRequest>? Request;

    /// <summary>The page the last Navigate/ShowLogin asked for finished (true) or did not load (false).</summary>
    event Action<bool>? Loaded;

    /// <summary>Starts the browser when it is not running. Throws when it cannot start.</summary>
    void Navigate(string url);

    /// <summary>Like Navigate, in a visible window where the user signs in.</summary>
    void ShowLogin(string url);

    Task<bool> HasSessionCookieAsync();

    /// <summary>Delete every cookie and all site data.</summary>
    Task ClearAsync();

    /// <summary>Stop the browser; the next Navigate starts it again.</summary>
    void Close();
}
