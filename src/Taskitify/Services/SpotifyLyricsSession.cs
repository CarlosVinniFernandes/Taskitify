using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Taskitify.Core.Lyrics;
using Taskitify.Infrastructure;

namespace Taskitify.Services;

/// <summary>A separate Spotify web session owned by Taskitify. Credentials stay in memory;
/// persisted login cookies are managed by WebView2, never copied out of the profile.</summary>
/// <remarks>The web player costs about 500 MB across its processes but is only needed to
/// capture a token and run the occasional search, and every use loads a page anyway. So the
/// page is unloaded <see cref="UnloadAfter"/> after each use and the browser closed after
/// <see cref="CloseAfter"/>; the token outlives it and is reused until it ages. While hidden,
/// the page gets no images, fonts, media, DRM or third-party frames: it never shows anything.</remarks>
public sealed class SpotifyLyricsSession(SettingsService settings, Dispatcher dispatcher, string? profileDirectory = null) : ISpotifyLyricsSession, IDisposable
{
    // Used when the token's own expiry is unknown; Spotify's last an hour.
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(40);
    // Spotify hands out the same token until it expires (measured: still the old one with under
    // a minute left), so renewing early only costs a page load. When our clock says expired but
    // Spotify returns it again, it is trusted this much longer; a 401 renews it in any case.
    private static readonly TimeSpan SameTokenGrace = TimeSpan.FromMinutes(1);
    // Signed out: lookups skip Spotify for a while instead of each waiting TokenWait in the browser.
    private static readonly TimeSpan LoginRetryAfter = TimeSpan.FromMinutes(10);
    // A page that did not load (offline): lookups skip the web player this long, or until the
    // network changes, instead of every retry starting it again.
    private static readonly TimeSpan FailedLoadPause = TimeSpan.FromMinutes(1);
    // Signed in (the cookie is there) yet no token: retried, but this many pages in a row means
    // something else (a verification page, a change at Spotify): the user is asked to look.
    private const int TokenlessPagesBeforeLogin = 3;
    private static readonly TimeSpan TokenWait = TimeSpan.FromSeconds(15);
    // Suspending kept the page's ~400 MB committed; unloading frees it. A short grace period
    // lets back-to-back uses (token, then search) share one page load.
    private static readonly TimeSpan UnloadAfter = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CloseAfter = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FirstLoadWait = TimeSpan.FromSeconds(10);
    // On the search page, the web player sends its search a moment after fetching the token.
    private static readonly TimeSpan SearchCaptureWait = TimeSpan.FromSeconds(4);
    private const string HomeUrl = "https://open.spotify.com/";
    private const string LoginUrl = "https://accounts.spotify.com/login?continue=https%3A%2F%2Fopen.spotify.com%2F";

    private WebView2? _browser;
    private Window? _window;
    private Task? _initializing;
    private TaskCompletionSource? _firstLoad;
    private bool _unloaded;
    // The last page the web player was sent to did not load: offline or Spotify down, not signed out.
    private bool _loadFailed;
    private DateTimeOffset _loadFailedAt = DateTimeOffset.MinValue;
    private bool _watchingNetwork;
    private int _tokenlessPages;
    private SpotifyCredentials? _credentials;
    private DateTimeOffset _capturedAt;
    private DateTimeOffset? _expiresAt;
    private (string Token, DateTimeOffset ExpiresAt)? _issued;
    private string? _prewarmedFor;
    private string? _agedToken;
    private DateTimeOffset _loginNeededAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _searchLock = new(1);
    private TaskCompletionSource<IReadOnlyList<SpotifyTrack>>? _search;
    private TrackQuery? _searchQuery;
    private DispatcherTimer? _idle;
    private DateTimeOffset _lastUse;
    private DateTimeOffset _startedAt;
    private int _uses;
    private bool _disposed;
    private bool _connecting;
    private bool _disconnecting;
    public string StatusKey { get; private set; } = "SpotifyLyricsDisconnected";

    /// <summary>The web player's last successful track search, replayable without the browser.</summary>
    public SpotifySearchTemplate? SearchTemplate { get; private set; }
    public event EventHandler? StatusChanged;

    private bool IsActive => !_disposed && !_disconnecting && settings.Current.SpotifyLyricsEnabled;

    /// <summary>The token's own expiry when the web player reported it, else a safe guess.</summary>
    private DateTimeOffset ValidUntil => _expiresAt ?? _capturedAt + TokenLifetime;

    /// <summary>The login window is on screen: never unload or close it under the user.</summary>
    private bool IsShownToUser => _window is { IsVisible: true, Opacity: > 0 };

    /// <summary>The login page is loading or on screen, so it gets images, fonts and frames.</summary>
    private bool ForUser => _connecting || IsShownToUser;

    public void ReportStatus(string key)
    {
        if (_disposed) return;
        dispatcher.BeginInvoke(() =>
        {
            if (!IsActive) return;
            // A rejected token is useless; the next lookup wakes the web player for a new one.
            if (key == "SpotifyLyricsDenied") _credentials = null;
            SetStatus(key);
        });
    }

    private void SetStatus(string key)
    {
        if (StatusKey == key) return;
        StatusKey = key;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ConnectAsync()
    {
        if (_disconnecting) return;
        settings.Update(s => s.SpotifyLyricsEnabled = true);
        BeginUse();
        _connecting = true;
        try
        {
            await WakeAsync(LoginUrl);
            _window!.Opacity = 1;
            _window.ShowActivated = true;
            _window.ShowInTaskbar = true;
            var workArea = SystemParameters.WorkArea;
            _window.Left = workArea.Left + Math.Max(0, (workArea.Width - _window.Width) / 2);
            _window.Top = workArea.Top + Math.Max(0, (workArea.Height - _window.Height) / 2);
            _window!.Show();
            _window.Activate();
            SetStatus("SpotifyLyricsLogin");
        }
        catch (Exception)
        {
            // Do not log browser exceptions: they may contain URLs with session data.
            SetStatus("SpotifyLyricsBrowserError");
            _initializing = null;
        }
        finally
        {
            _connecting = false;
            EndUse();
        }
    }

    public async Task DisconnectAsync()
    {
        _disconnecting = true;
        settings.Update(s => s.SpotifyLyricsEnabled = false);
        _credentials = null;
        // The older token strings kept for comparisons go too: nothing of the session stays in memory.
        _issued = null;
        _agedToken = null;
        _prewarmedFor = null;
        _search?.TrySetCanceled();
        BeginUse();
        try
        {
            // The browser may have been closed while idle; its profile still holds the login.
            await WakeAsync("about:blank");
            if (_browser?.CoreWebView2 is { } core)
                await core.Profile.ClearBrowsingDataAsync();
            SetStatus("SpotifyLyricsDisconnected");
        }
        catch (Exception) { SetStatus("SpotifyLyricsBrowserError"); _initializing = null; }
        finally
        {
            _window?.Hide();
            _disconnecting = false;
            EndUse();
        }
    }

    public async Task<SpotifyCredentials?> GetCredentialsAsync(TrackQuery? searchFor, CancellationToken ct)
    {
        return await dispatcher.InvokeAsync(async () =>
        {
            if (!IsActive) return null;
            var age = DateTimeOffset.UtcNow - _capturedAt;
            if (_credentials is not null && DateTimeOffset.UtcNow < ValidUntil) return _credentials;
            if (_credentials is null && StatusKey == "SpotifyLyricsLogin" && !ForUser
                && DateTimeOffset.UtcNow - _loginNeededAt < LoginRetryAfter) return null;
            if (!ForUser && DateTimeOffset.UtcNow - _loadFailedAt < FailedLoadPause)
                throw new HttpRequestException("Spotify web player could not load");
            BeginUse();
            try
            {
                var running = _initializing is { IsCompletedSuccessfully: true } && _browser is not null;
                // A page load for the token can be the lookup's search page: one load then gives
                // both the token and the web player's search query.
                var searchUrl = searchFor is not null && SearchTemplate is null ? SearchUrl(searchFor) : null;
                var aged = _credentials is not null;
                if (aged)
                {
                    _agedToken = _credentials!.AccessToken;
                    _credentials = null;
                    Log.Debug($"Spotify token is {age.TotalMinutes:0} min old: {(running ? "reloading" : "starting")} the web player");
                }
                // The player fetches a token when it loads: an aged token or an unloaded page
                // needs it loaded again, and a closed browser starts on it. A browser already
                // starting loads it anyway; the login window is never navigated under the user.
                var url = running ? ((aged || _unloaded) && !ForUser ? searchUrl ?? HomeUrl : null)
                    : _initializing is null ? searchUrl ?? HomeUrl : null;
                await WakeAsync(url).WaitAsync(ct);
                var deadline = DateTimeOffset.UtcNow + TokenWait;
                while (_credentials is null && !_loadFailed && IsActive && DateTimeOffset.UtcNow < deadline)
                    await Task.Delay(100, ct);
                if (url is not null && url == searchUrl)
                {
                    deadline = DateTimeOffset.UtcNow + SearchCaptureWait;
                    while (_credentials is not null && SearchTemplate is null && IsActive && DateTimeOffset.UtcNow < deadline)
                        await Task.Delay(100, ct);
                }
                if (!IsActive) return null;
                // Not signed out: no login prompt or cooldown, and the lookup fails (and is retried)
                // instead of counting as a song Spotify has no lyrics for.
                if (_credentials is null && _loadFailed)
                {
                    _loadFailedAt = DateTimeOffset.UtcNow;
                    WatchNetwork();
                    throw new HttpRequestException("Spotify web player could not load");
                }
                // Likewise a slow web player whose session cookie is there, for a few pages in a
                // row: only a page without it is signed out.
                if (_credentials is null && await HasSessionCookieAsync() && ++_tokenlessPages < TokenlessPagesBeforeLogin)
                    throw new HttpRequestException($"Spotify web player gave no token within {TokenWait.TotalSeconds:0} s");
                if (_credentials is null)
                {
                    _tokenlessPages = 0;
                    Log.Debug($"Spotify web player gave no token within {TokenWait.TotalSeconds:0} s");
                    _loginNeededAt = DateTimeOffset.UtcNow;
                    SetStatus("SpotifyLyricsLogin");
                }
                return _credentials;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is not HttpRequestException)
            {
                SetStatus("SpotifyLyricsBrowserError");
                _initializing = null;
                return null;
            }
            finally { EndUse(); }
        }).Task.Unwrap().WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Spotify's login cookie, as the token capture checks it.</summary>
    private async Task<bool> HasSessionCookieAsync()
    {
        try
        {
            return _browser?.CoreWebView2 is { } core
                && (await core.CookieManager.GetCookiesAsync("https://open.spotify.com")).Any(c => c.Name == "sp_dc");
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>A network change ends the pause after a failed page load: the network may be back.</summary>
    private void WatchNetwork()
    {
        if (_watchingNetwork) return;
        _watchingNetwork = true;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
    }

    private void OnNetworkChanged(object? sender, EventArgs e) =>
        dispatcher.BeginInvoke(() => _loadFailedAt = DateTimeOffset.MinValue);

    /// <summary>A token fetched ahead of any lookup; if it fails, the next lookup tries again.</summary>
    private void FetchCredentialsInBackground() =>
        _ = GetCredentialsAsync(null, CancellationToken.None).ContinueWith(t => _ = t.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    public async Task<SpotifyCredentials?> RenewCredentialsAsync(SpotifyCredentials rejected, CancellationToken ct)
    {
        await dispatcher.InvokeAsync(() =>
        {
            if (_credentials?.AccessToken != rejected.AccessToken) return;
            Log.Debug("Spotify rejected the token; getting a new one");
            // Expired rather than dropped: the web player then loads again wherever it is
            // (a dropped token on an already loaded page would just be waited for).
            _expiresAt = DateTimeOffset.UtcNow;
        });
        return await GetCredentialsAsync(null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The current song is about to end. A token that expired during it is renewed now (once per
    /// token), so the next song's lookup does not wait for the web player. Spotify does not renew
    /// a token before it expires, so there is nothing to gain earlier.
    /// </summary>
    public void PrepareForNextSong()
    {
        if (!IsActive || _uses > 0 || _credentials is not { } current || _prewarmedFor == current.AccessToken) return;
        if (DateTimeOffset.UtcNow < ValidUntil) return;
        _prewarmedFor = current.AccessToken;
        Log.Debug("Spotify token expired: renewing it before the next song");
        FetchCredentialsInBackground();
    }

    /// <summary>
    /// The web player is no longer opened at startup; the settings window asks for the session
    /// state instead, so it never shows an enabled source as disconnected.
    /// </summary>
    public void CheckSession()
    {
        if (IsActive && StatusKey == "SpotifyLyricsDisconnected")
            FetchCredentialsInBackground();
    }

    public async Task<IReadOnlyList<SpotifyTrack>> SearchAsync(TrackQuery query, CancellationToken ct)
    {
        await _searchLock.WaitAsync(ct).ConfigureAwait(false);
        TaskCompletionSource<IReadOnlyList<SpotifyTrack>>? pending = null;
        var inUse = false;
        try
        {
            pending = await dispatcher.InvokeAsync(async () =>
            {
                ct.ThrowIfCancellationRequested();
                if (!IsActive) throw new OperationCanceledException();
                BeginUse();
                inUse = true;
                // Registered first: a browser that was closed starts right at the search page.
                var search = new TaskCompletionSource<IReadOnlyList<SpotifyTrack>>(TaskCreationOptions.RunContinuationsAsynchronously);
                _searchQuery = query;
                _search = search;
                try
                {
                    await WakeAsync(SearchUrl(query));
                }
                catch
                {
                    if (_search == search) { _search = null; _searchQuery = null; }
                    throw;
                }
                return search;
            }).Task.Unwrap();
            try { return await pending.Task.WaitAsync(TimeSpan.FromSeconds(8), ct).ConfigureAwait(false); }
            catch (TimeoutException) { return []; }
        }
        finally
        {
            await dispatcher.InvokeAsync(() =>
            {
                if (_search == pending) { _search = null; _searchQuery = null; }
                if (inUse) EndUse();
            });
            _searchLock.Release();
        }
    }

    private Task EnsureAsync(string url) => _initializing ??= InitializeAsync(url);

    /// <summary>
    /// Start the web player if it was closed and go to <paramref name="url"/> (null: stay on the
    /// current page, or the home page of a new browser). A new browser starts directly at the
    /// url: a second navigation issued before the first one completes leaves both hanging until
    /// the browser closes.
    /// </summary>
    private async Task WakeAsync(string? url = null)
    {
        var starting = _initializing is null;
        await EnsureAsync(url ?? HomeUrl);
        if (_browser?.CoreWebView2 is not { } core) throw new InvalidOperationException("Spotify web player unavailable");
        if (starting)
        {
            _unloaded = false;
            return;
        }
        if (url is null) return;
        await WaitForFirstLoadAsync();
        _unloaded = false;
        _loadFailed = false;
        core.Navigate(url);
    }

    private async Task WaitForFirstLoadAsync()
    {
        try
        {
            if (_firstLoad is { } load) await load.Task.WaitAsync(FirstLoadWait);
        }
        catch (TimeoutException)
        {
            // Navigate anyway; a page that never finished loading is no better.
        }
    }

    private void BeginUse()
    {
        _uses++;
        _idle?.Stop();
    }

    private void EndUse()
    {
        _uses--;
        _lastUse = DateTimeOffset.UtcNow;
        ScheduleIdle(UnloadAfter);
    }

    private void ScheduleIdle(TimeSpan after)
    {
        if (_disposed) return;
        if (_idle is null)
        {
            _idle = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
            _idle.Tick += OnIdle;
        }
        _idle.Stop();
        _idle.Interval = after > TimeSpan.Zero ? after : TimeSpan.FromSeconds(1);
        _idle.Start();
    }

    private async void OnIdle(object? sender, EventArgs e)
    {
        _idle!.Stop();
        if (_disposed || _uses > 0 || IsShownToUser || _browser is not { CoreWebView2: { } core }) return;
        var idleFor = DateTimeOffset.UtcNow - _lastUse;
        if (idleFor >= CloseAfter)
        {
            Log.Debug($"Spotify web player closed after {idleFor.TotalSeconds:0} s idle");
            CloseBrowser();
            return;
        }
        if (!_unloaded)
        {
            await LogProcessesAsync(core);
            // Every use navigates again anyway; an empty page frees the player's memory now.
            await WaitForFirstLoadAsync();
            if (_uses > 0 || _disposed || _browser?.CoreWebView2 != core) return;
            _unloaded = true;
            core.Navigate("about:blank");
            Log.Debug($"Spotify web player page unloaded after {idleFor.TotalSeconds:0} s idle");
        }
        if (_uses == 0) ScheduleIdle(CloseAfter - idleFor);
    }

    /// <summary>End the browser processes. The token in memory stays valid without them.</summary>
    private void CloseBrowser()
    {
        if (_window is not null)
        {
            _window.Content = null;
            _window.Hide();
        }
        _browser?.Dispose();
        _browser = null;
        _initializing = null;
    }

    private async Task InitializeAsync(string url)
    {
        _browser?.Dispose();
        _browser = new WebView2();
        var firstLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _firstLoad = firstLoad;
        _startedAt = DateTimeOffset.UtcNow;
        Log.Debug("Spotify web player starting");
        if (_window is null)
        {
            _window = new Window { Title = "Taskitify — Spotify", Width = 1000, Height = 740, MinWidth = 640, MinHeight = 480 };
            _window.Closing += (_, e) =>
            {
                // Only the login window is closed by the user; a parked or hidden one closes with the app.
                if (_disposed || !IsShownToUser) return;
                e.Cancel = true;
                ParkWindow();
                _lastUse = DateTimeOffset.UtcNow;
                if (_uses == 0) ScheduleIdle(UnloadAfter);
            };
        }
        var panel = new DockPanel();
        var hint = new TextBlock { Text = LocalizationService.Get("SpotifyLyricsLoginHint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
        DockPanel.SetDock(hint, Dock.Top);
        panel.Children.Add(hint);
        panel.Children.Add(_browser);
        _window.Content = panel;
        // The page needs no hardware acceleration; without it the GPU process stays small.
        var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--disable-gpu" };
        var environment = await CoreWebView2Environment.CreateAsync(
            userDataFolder: profileDirectory ?? Path.Combine(AppPaths.LocalDataDirectory, "spotify-web-session"), options: options);
        if (_disposed) return;
        // Keep a presentation source alive outside the desktop. Hiding the WPF window
        // before Spotify hydrates can leave login complete but the player/token unavailable.
        ParkWindow();
        await _browser.EnsureCoreWebView2Async(environment);
        if (_disposed) return;
        Log.Debug($"Spotify web player ready in {(DateTimeOffset.UtcNow - _startedAt).TotalMilliseconds:0} ms");
        var core = _browser.CoreWebView2;
        core.IsMuted = true;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        // The hidden player never plays or shows anything: no DRM (its broker process alone is
        // ~70 MB), images, fonts, media or third-party frames. The login page keeps them all.
        await core.AddScriptToExecuteOnDocumentCreatedAsync(
            "if (navigator.requestMediaKeySystemAccess) navigator.requestMediaKeySystemAccess = () => Promise.reject(new DOMException('Not available', 'NotSupportedError'));");
        foreach (var context in new[] { CoreWebView2WebResourceContext.Image, CoreWebView2WebResourceContext.Font, CoreWebView2WebResourceContext.Media })
            core.AddWebResourceRequestedFilter("*", context, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += (_, e) =>
        {
            if (!ForUser) e.Response = core.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
        };
        core.FrameNavigationStarting += (_, e) =>
        {
            // Only other sites' frames (Google's took a renderer of its own); the page's own
            // about:, blob: and data: frames are left alone.
            if (!ForUser && Uri.TryCreate(e.Uri, UriKind.Absolute, out var frame) && frame.Scheme is "http" or "https"
                && !IsSpotifyPage(e.Uri))
                e.Cancel = true;
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (IsSpotifyPage(e.Uri)) core.Navigate(e.Uri);
        };
        core.NavigationStarting += (_, e) =>
        {
            if (e.Uri != "about:blank" && !IsSpotifyPage(e.Uri)) e.Cancel = true;
        };
        core.WebResourceResponseReceived += OnResponse;
        core.NavigationStarting += (_, e) =>
            Log.Debug($"Web player navigation {e.NavigationId} starting: {(Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) ? u.Host + u.AbsolutePath : e.Uri)}{(e.Cancel ? " (cancelled)" : "")}");
        core.NavigationCompleted += (_, e) =>
        {
            firstLoad.TrySetResult();
            // A navigation replaced by the next one is not a failure; a later reload can succeed.
            if (e.IsSuccess) _loadFailed = false;
            else if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled) _loadFailed = true;
            Log.Debug($"Web player navigation {e.NavigationId} completed: {(e.IsSuccess ? "ok" : e.WebErrorStatus.ToString())} {e.HttpStatusCode}");
        };
        core.ProcessFailed += (_, e) => Log.Debug($"Web player process failed: {e.ProcessFailedKind} {e.Reason} exit {e.ExitCode}");
        _loadFailed = false;
        core.Navigate(url);
    }

    private void ParkWindow()
    {
        if (_window is null || _disposed) return;
        _window.Opacity = 0;
        _window.ShowActivated = false;
        _window.ShowInTaskbar = false;
        _window.Left = -32000;
        _window.Top = -32000;
        // The login window the user is closing is still shown, and WPF throws on Show() while a
        // window is closing (it then destroys the window, browser included).
        if (!_window.IsVisible) _window.Show();
    }

    private static string SearchUrl(TrackQuery query) =>
        "https://open.spotify.com/search/" + Uri.EscapeDataString(query.Title + " " + query.Artist) + "/tracks";

    private static bool IsSpotifyPage(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && (uri.Host == "spotify.com" || uri.Host.EndsWith(".spotify.com", StringComparison.OrdinalIgnoreCase));

    private async void OnResponse(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        if (!IsActive || _browser is not { } browser) return;
        try
        {
            var uri = new Uri(e.Request.Uri);
            // Request bodies are streams: read once.
            var body = uri.Host == "api-partner.spotify.com" && e.Request.Method == "POST" ? ReadBody(e.Request) : null;
            if (uri.Scheme == "https" && uri.Host == "open.spotify.com" && uri.AbsolutePath == "/api/token"
                && e.Response.StatusCode is >= 200 and < 300)
            {
                using var tokenStream = await e.Response.GetContentAsync();
                if (await SpotifyLyricsProvider.ReadTokenAsync(tokenStream) is { } issued)
                {
                    _issued = issued;
                    if (_credentials?.AccessToken == issued.AccessToken) _expiresAt = issued.ExpiresAt;
                }
                return;
            }
            if (uri.Scheme != "https" || uri.Host is not ("api.spotify.com" or "api-partner.spotify.com" or "spclient.wg.spotify.com")) return;
            if (e.Response.StatusCode is < 200 or >= 300) return;
            var headers = e.Request.Headers;
            if (SpotifySearchTemplate.TryCapture(e.Request.Method, e.Request.Uri, body, headers) is { } template)
            {
                if (template.QueryHash != SearchTemplate?.QueryHash)
                    Log.Debug($"Spotify search query captured: {template.OperationName} ({template.QueryHash?[..Math.Min(8, template.QueryHash.Length)]}…)");
                SearchTemplate = template;
                // The page's own results arrive scattered over later responses, depending on how
                // it renders; the provider sends the captured query itself, so stop waiting here.
                _search?.TrySetResult([]);
            }
            if (headers.Contains("Authorization"))
            {
                var auth = headers.GetHeader("Authorization");
                var cookies = await browser.CoreWebView2.CookieManager.GetCookiesAsync("https://open.spotify.com");
                if (!IsActive) return;
                if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && cookies.Any(c => c.Name == "sp_dc"))
                {
                    var access = auth[7..];
                    var first = _credentials is null;
                    // Observing an old token again must not extend its lifetime.
                    if (_credentials?.AccessToken != access)
                    {
                        _capturedAt = DateTimeOffset.UtcNow;
                        _expiresAt = _issued is { } issued && issued.Token == access ? issued.ExpiresAt : null;
                        // The token we just aged, handed out again: Spotify still considers it valid.
                        if (access == _agedToken && (_expiresAt ?? _capturedAt) < _capturedAt + SameTokenGrace)
                            _expiresAt = _capturedAt + SameTokenGrace;
                        Log.Debug($"Spotify token captured {(_capturedAt - _startedAt).TotalMilliseconds:0} ms after the web player started, "
                            + (access == _agedToken ? "the same one again, " : "")
                            + (_expiresAt is { } expires ? $"valid for {(expires - _capturedAt).TotalSeconds:0} s" : "expiry unknown"));
                    }
                    _credentials = new(access, headers.Contains("Client-Token") ? headers.GetHeader("Client-Token") : _credentials?.ClientToken);
                    _tokenlessPages = 0;
                    _loadFailedAt = DateTimeOffset.MinValue;
                    if (first) SetStatus("SpotifyLyricsReady");
                }
            }
            var pending = _search;
            var query = _searchQuery;
            if (pending is null || query is null || uri.Host != "api-partner.spotify.com") return;
            using var stream = await e.Response.GetContentAsync();
            // WebView2's COM stream does not implement every Stream member used by
            // JsonDocument.ParseAsync (including seeking/length on some runtimes).
            // Read sequentially off the UI thread instead.
            var found = await SpotifyLyricsProvider.ReadSearchAsync(stream);
            var tracks = found.Where(t => TrackMatcher.IsMatch(query, t.Metadata)).ToList();
            if (body?.Contains("\"search", StringComparison.Ordinal) == true)
                Log.Debug($"Web player search response: {found.Count} tracks, {tracks.Count} matching '{query.Title}' / '{query.Artist}' ({query.Duration?.TotalSeconds:0} s)"
                    + (tracks.Count == 0 && found.Count > 0 ? "; first: " + string.Join("; ", found.Take(3).Select(t => $"'{t.Metadata.TrackName}' / '{t.Metadata.ArtistName}' ({t.Metadata.Duration:0} s)")) : ""));
            if (_search == pending && tracks.Count > 0) pending.TrySetResult(tracks);
        }
        catch (Exception)
        {
            // Some web-player responses are binary, cancelled, unreadable (the GraphQL ones
            // throw COMException), or belong to a closed page.
        }
    }

    /// <summary>Which pages each web player process holds, by host only (diagnostics).</summary>
    private static async Task LogProcessesAsync(CoreWebView2 core)
    {
        if (!Log.WriteToFile) return;
        try
        {
            var infos = await core.Environment.GetProcessExtendedInfosAsync();
            Log.Debug("Spotify web player processes: " + string.Join("; ", infos.Select(i =>
            {
                var hosts = i.AssociatedFrameInfos.Select(f => Uri.TryCreate(f.Source, UriKind.Absolute, out var u) ? u.Host : f.Source)
                    .Distinct().DefaultIfEmpty("-");
                return $"{i.ProcessInfo.Kind} {i.ProcessInfo.ProcessId} [{string.Join(", ", hosts)}]";
            })));
        }
        catch (Exception)
        {
            // Diagnostics only.
        }
    }

    private static string? ReadBody(CoreWebView2WebResourceRequest request)
    {
        try
        {
            if (request.Content is not { } content) return null;
            using var reader = new StreamReader(content, System.Text.Encoding.UTF8, false, 4096, leaveOpen: true);
            return reader.ReadToEnd();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _idle?.Stop();
        if (_watchingNetwork)
        {
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
            NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        }
        _credentials = null;
        _search?.TrySetCanceled();
        _browser?.Dispose();
        _window?.Close();
    }
}
