using System.Net.Http;
using System.Text;
using Taskitify.Core.Lyrics;

namespace Taskitify.Platform.Linux.Spotify;

/// <summary>
/// The optional Spotify lyrics source on Linux: the web player's own token and track search, taken from
/// the browser's traffic like the Windows app does with WebView2. The browser is only started when a token
/// or search is needed and is stopped 30 s later. Nothing is stored here; the login lives in the helper's
/// own profile.
/// </summary>
/// <remarks>
/// shortcut: the token is renewed on demand (when a lookup finds it expired), not ahead of the next song
/// like Windows does, so the first song after expiry waits for the page to load; upgrade if that shows.
/// </remarks>
public sealed class SpotifyWebSession(IWebPlayer player) : ISpotifyLyricsSession, IDisposable
{
    private const string HomeUrl = "https://open.spotify.com/";
    private const string LoginUrl = "https://accounts.spotify.com/login?continue=https%3A%2F%2Fopen.spotify.com%2F";
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(40);
    private static readonly HashSet<string> ApiHosts = ["api.spotify.com", "api-partner.spotify.com", "spclient.wg.spotify.com"];

    private readonly object _gate = new();
    private readonly SemaphoreSlim _searchLock = new(1);
    private Timer? _idle;
    private SpotifyCredentials? _credentials;
    private DateTimeOffset _capturedAt;
    private DateTimeOffset? _expiresAt;
    private (string Token, DateTimeOffset ExpiresAt)? _issued;
    private bool _loadFailed;
    private DateTimeOffset _loadFailedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _loginNeededAt = DateTimeOffset.MinValue;
    private TaskCompletionSource<IReadOnlyList<SpotifyTrack>>? _search;
    private TrackQuery? _searchQuery;
    private bool _wired;

    public TimeSpan TokenWait { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan SearchCaptureWait { get; init; } = TimeSpan.FromSeconds(4);

    public TimeSpan SearchWait { get; init; } = TimeSpan.FromSeconds(8);

    public TimeSpan FailedLoadPause { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan LoginRetryAfter { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan CloseAfter { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Whether the user turned this source on. Off: every call answers "signed out".</summary>
    public bool Enabled { get; set; }

    /// <summary>A key of the shared translations: SpotifyLyricsDisconnected, ...Login, ...Ready, ...Working, ...Denied, ...BrowserError.</summary>
    public string StatusKey { get; private set; } = "SpotifyLyricsDisconnected";

    public event Action? StatusChanged;

    public SpotifySearchTemplate? SearchTemplate { get; private set; }

    public SpotifyCredentials? ValidCredentials =>
        Enabled && _credentials is { } c && DateTimeOffset.UtcNow < ValidUntil ? c : null;

    private DateTimeOffset ValidUntil => _expiresAt ?? _capturedAt + TokenLifetime;

    public void ReportStatus(string key)
    {
        if (!Enabled)
            return;
        // A rejected token is useless; the next lookup gets a new one.
        if (key == "SpotifyLyricsDenied")
            _credentials = null;
        SetStatus(key);
    }

    /// <summary>Show the sign-in window. Success shows up as a token, which turns the status to Ready.</summary>
    public void Connect()
    {
        Enabled = true;
        Wire();
        try
        {
            player.ShowLogin(LoginUrl);
            SetStatus("SpotifyLyricsLogin");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus("SpotifyLyricsBrowserError");
        }
    }

    public async Task DisconnectAsync()
    {
        Enabled = false;
        _credentials = null;
        _issued = null;
        _search?.TrySetCanceled();
        try
        {
            Wire();
            await player.ClearAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            // No browser running means nothing stored by this session to clear.
        }
        finally
        {
            player.Close();
            SetStatus("SpotifyLyricsDisconnected");
        }
    }

    public async Task<SpotifyCredentials?> GetCredentialsAsync(TrackQuery? searchFor, CancellationToken ct)
    {
        if (!Enabled)
            return null;
        if (ValidCredentials is { } valid)
            return valid;
        // Signed out recently: do not make every song wait for a page that will not have a token.
        if (_credentials is null && StatusKey == "SpotifyLyricsLogin" && DateTimeOffset.UtcNow - _loginNeededAt < LoginRetryAfter)
            return null;
        if (DateTimeOffset.UtcNow - _loadFailedAt < FailedLoadPause)
            throw new HttpRequestException("Spotify web player could not load");

        _credentials = null;
        var wantSearch = searchFor is not null && SearchTemplate is null;
        if (!Open(wantSearch ? SearchUrl(searchFor!) : HomeUrl))
            return null;
        try
        {
            await WaitAsync(() => _credentials is not null || _loadFailed, TokenWait, ct).ConfigureAwait(false);
            if (wantSearch && _credentials is not null)
                await WaitAsync(() => SearchTemplate is not null, SearchCaptureWait, ct).ConfigureAwait(false);
        }
        finally
        {
            ScheduleClose();
        }

        if (_credentials is not null)
            return _credentials;
        // Not signed out: the lookup fails and is retried, instead of counting as "no lyrics".
        if (_loadFailed)
        {
            _loadFailedAt = DateTimeOffset.UtcNow;
            throw new HttpRequestException("Spotify web player could not load");
        }
        if (await player.HasSessionCookieAsync().ConfigureAwait(false))
            throw new HttpRequestException("Spotify web player gave no token in time");

        _loginNeededAt = DateTimeOffset.UtcNow;
        SetStatus("SpotifyLyricsLogin");
        return null;
    }

    public Task<SpotifyCredentials?> RenewCredentialsAsync(SpotifyCredentials rejected, CancellationToken ct)
    {
        if (_credentials?.AccessToken == rejected.AccessToken)
            _credentials = null;
        return GetCredentialsAsync(null, ct);
    }

    public async Task<IReadOnlyList<SpotifyTrack>> SearchAsync(TrackQuery query, CancellationToken ct)
    {
        await _searchLock.WaitAsync(ct).ConfigureAwait(false);
        var pending = new TaskCompletionSource<IReadOnlyList<SpotifyTrack>>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (!Enabled)
                throw new OperationCanceledException();
            _searchQuery = query;
            _search = pending;
            if (!Open(SearchUrl(query)))
                return [];
            try
            {
                return await pending.Task.WaitAsync(SearchWait, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return [];
            }
        }
        finally
        {
            _search = null;
            _searchQuery = null;
            ScheduleClose();
            _searchLock.Release();
        }
    }

    public void Dispose()
    {
        _idle?.Dispose();
        _credentials = null;
        _search?.TrySetCanceled();
        player.Dispose();
        _searchLock.Dispose();
    }

    private bool Open(string url)
    {
        Wire();
        _loadFailed = false;
        _idle?.Change(Timeout.Infinite, Timeout.Infinite);
        try
        {
            player.Navigate(url);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus("SpotifyLyricsBrowserError");
            return false;
        }
    }

    private void Wire()
    {
        lock (_gate)
        {
            if (_wired)
                return;
            _wired = true;
        }
        player.Request += r => _ = OnRequestAsync(r);
        player.Loaded += ok =>
        {
            if (!ok)
                _loadFailed = true;
        };
    }

    private void ScheduleClose()
    {
        _idle ??= new Timer(_ => player.Close());
        _idle.Change(CloseAfter, Timeout.InfiniteTimeSpan);
    }

    private void SetStatus(string key)
    {
        if (StatusKey == key)
            return;
        StatusKey = key;
        StatusChanged?.Invoke();
    }

    private static string SearchUrl(TrackQuery query) =>
        "https://open.spotify.com/search/" + Uri.EscapeDataString(query.Title + " " + query.Artist) + "/tracks";

    private static async Task WaitAsync(Func<bool> done, TimeSpan timeout, CancellationToken ct)
    {
        var end = DateTimeOffset.UtcNow + timeout;
        while (!done() && DateTimeOffset.UtcNow < end)
            await Task.Delay(50, ct).ConfigureAwait(false);
    }

    private async Task OnRequestAsync(WebRequest r)
    {
        try
        {
            if (!Enabled || !Uri.TryCreate(r.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || r.Status is < 200 or >= 300)
                return;

            if (uri.Host == "open.spotify.com" && uri.AbsolutePath == "/api/token" && r.Response is not null)
            {
                using var body = new MemoryStream(Encoding.UTF8.GetBytes(r.Response));
                if (await SpotifyLyricsProvider.ReadTokenAsync(body).ConfigureAwait(false) is { } issued)
                {
                    _issued = issued;
                    if (_credentials?.AccessToken == issued.AccessToken)
                        _expiresAt = issued.ExpiresAt;
                }
                return;
            }
            if (!ApiHosts.Contains(uri.Host))
                return;

            if (SpotifySearchTemplate.TryCapture(r.Method, r.Url, r.Body, r.Headers) is { } template)
            {
                SearchTemplate = template;
                // The provider sends the captured query itself, so stop waiting for the page's own results.
                _search?.TrySetResult([]);
            }

            if (r.Headers.TryGetValue("Authorization", out var auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                && await player.HasSessionCookieAsync().ConfigureAwait(false))
                TakeToken(auth[7..], r.Headers.GetValueOrDefault("Client-Token"));

            if (_search is { } pending && _searchQuery is { } query && uri.Host == "api-partner.spotify.com" && r.Response is not null)
            {
                using var results = new MemoryStream(Encoding.UTF8.GetBytes(r.Response));
                var matching = (await SpotifyLyricsProvider.ReadSearchAsync(results).ConfigureAwait(false))
                    .Where(t => TrackMatcher.IsMatch(query, t.Metadata)).ToList();
                if (matching.Count > 0)
                    pending.TrySetResult(matching);
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or TimeoutException or InvalidOperationException or IOException)
        {
            // Some responses are not JSON or arrive after the page was closed.
        }
    }

    private void TakeToken(string access, string? clientToken)
    {
        var first = _credentials is null;
        // Seeing an old token again must not extend its life.
        if (_credentials?.AccessToken != access)
        {
            _capturedAt = DateTimeOffset.UtcNow;
            _expiresAt = _issued is { } issued && issued.Token == access ? issued.ExpiresAt : null;
        }
        _credentials = new SpotifyCredentials(access, clientToken ?? _credentials?.ClientToken);
        _loadFailedAt = DateTimeOffset.MinValue;
        if (first)
            SetStatus("SpotifyLyricsReady");
    }
}
