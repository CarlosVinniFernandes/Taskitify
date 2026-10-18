using System.IO;
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
/// capture a token and run the occasional search. It is suspended shortly after each use and
/// closed after <see cref="CloseAfter"/>; the token outlives it and is reused until it ages.</remarks>
public sealed class SpotifyLyricsSession(SettingsService settings, Dispatcher dispatcher, string? profileDirectory = null) : ISpotifyLyricsSession, IDisposable
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(40);
    private static readonly TimeSpan TokenWait = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SuspendAfter = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CloseAfter = TimeSpan.FromMinutes(2);

    private WebView2? _browser;
    private Window? _window;
    private Task? _initializing;
    private SpotifyCredentials? _credentials;
    private DateTimeOffset _capturedAt;
    private readonly SemaphoreSlim _searchLock = new(1);
    private TaskCompletionSource<IReadOnlyList<SpotifyTrack>>? _search;
    private TrackQuery? _searchQuery;
    private DispatcherTimer? _idle;
    private DateTimeOffset _lastUse;
    private int _uses;
    private bool _disposed;
    private bool _disconnecting;
    public string StatusKey { get; private set; } = "SpotifyLyricsDisconnected";
    public event EventHandler? StatusChanged;

    private bool IsActive => !_disposed && !_disconnecting && settings.Current.SpotifyLyricsEnabled;

    /// <summary>The login window is on screen: never suspend or close it under the user.</summary>
    private bool IsShownToUser => _window is { IsVisible: true, Opacity: > 0 };

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
        try
        {
            await WakeAsync();
            _window!.Opacity = 1;
            _window.ShowActivated = true;
            _window.ShowInTaskbar = true;
            var workArea = SystemParameters.WorkArea;
            _window.Left = workArea.Left + Math.Max(0, (workArea.Width - _window.Width) / 2);
            _window.Top = workArea.Top + Math.Max(0, (workArea.Height - _window.Height) / 2);
            _window!.Show();
            _window.Activate();
            _browser!.CoreWebView2.Navigate("https://accounts.spotify.com/login?continue=https%3A%2F%2Fopen.spotify.com%2F");
            SetStatus("SpotifyLyricsLogin");
        }
        catch (Exception)
        {
            // Do not log browser exceptions: they may contain URLs with session data.
            SetStatus("SpotifyLyricsBrowserError");
            _initializing = null;
        }
        finally { EndUse(); }
    }

    public async Task DisconnectAsync()
    {
        _disconnecting = true;
        settings.Update(s => s.SpotifyLyricsEnabled = false);
        _credentials = null;
        _search?.TrySetCanceled();
        BeginUse();
        try
        {
            // The browser may have been closed while idle; its profile still holds the login.
            await EnsureAsync();
            if (_browser?.CoreWebView2 is { } core)
            {
                core.Stop();
                core.Navigate("about:blank");
                await core.Profile.ClearBrowsingDataAsync();
            }
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

    public async Task<SpotifyCredentials?> GetCredentialsAsync(CancellationToken ct)
    {
        return await dispatcher.InvokeAsync(async () =>
        {
            if (!IsActive) return null;
            if (_credentials is not null && DateTimeOffset.UtcNow - _capturedAt <= TokenLifetime) return _credentials;
            BeginUse();
            try
            {
                var running = _initializing is { IsCompletedSuccessfully: true } && _browser is not null;
                await WakeAsync().WaitAsync(ct);
                if (_credentials is not null)
                {
                    // Aged token: let the web client fetch a new one. A browser that was just
                    // started is already loading the player and will do so anyway.
                    _credentials = null;
                    if (running) _browser!.CoreWebView2.Reload();
                }
                var deadline = DateTimeOffset.UtcNow + TokenWait;
                while (_credentials is null && IsActive && DateTimeOffset.UtcNow < deadline)
                    await Task.Delay(100, ct);
                if (!IsActive) return null;
                if (_credentials is null) SetStatus("SpotifyLyricsLogin");
                return _credentials;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                SetStatus("SpotifyLyricsBrowserError");
                _initializing = null;
                return null;
            }
            finally { EndUse(); }
        }).Task.Unwrap().WaitAsync(ct).ConfigureAwait(false);
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
                await WakeAsync();
                _searchQuery = query;
                _search = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _browser!.CoreWebView2.Navigate("https://open.spotify.com/search/" + Uri.EscapeDataString(query.Title + " " + query.Artist) + "/tracks");
                return _search;
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

    private Task EnsureAsync() => _initializing ??= InitializeAsync();

    /// <summary>Start the web player if it was closed and resume it if it was suspended.</summary>
    private async Task WakeAsync()
    {
        await EnsureAsync();
        if (_browser?.CoreWebView2 is not { } core) throw new InvalidOperationException("Spotify web player unavailable");
        _browser.Visibility = Visibility.Visible;
        if (core.IsSuspended) core.Resume();
        core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
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
        ScheduleIdle(SuspendAfter);
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
        if (_disposed || _uses > 0 || IsShownToUser || _browser is not { CoreWebView2: { } core } browser) return;
        var idleFor = DateTimeOffset.UtcNow - _lastUse;
        if (idleFor >= CloseAfter)
        {
            CloseBrowser();
            return;
        }
        try
        {
            if (!core.IsSuspended)
            {
                core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                // Suspending requires a hidden WebView; showing it again resumes the page.
                browser.Visibility = Visibility.Hidden;
                await core.TrySuspendAsync();
                if (_uses > 0 && _browser == browser)
                {
                    browser.Visibility = Visibility.Visible;
                    core.Resume();
                }
            }
        }
        catch (Exception)
        {
            // A page that cannot be suspended right now simply stays awake until it is closed.
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

    private async Task InitializeAsync()
    {
        _browser?.Dispose();
        _browser = new WebView2();
        if (_window is null)
        {
            _window = new Window { Title = "Taskitify — Spotify", Width = 1000, Height = 740, MinWidth = 640, MinHeight = 480 };
            _window.Closing += (_, e) =>
            {
                if (_disposed) return;
                e.Cancel = true;
                ParkWindow();
                _lastUse = DateTimeOffset.UtcNow;
                if (_uses == 0) ScheduleIdle(SuspendAfter);
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
        var core = _browser.CoreWebView2;
        core.IsMuted = true;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
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
        core.Navigate("https://open.spotify.com/");
    }

    private void ParkWindow()
    {
        if (_window is null || _disposed) return;
        _window.Opacity = 0;
        _window.ShowActivated = false;
        _window.ShowInTaskbar = false;
        _window.Left = -32000;
        _window.Top = -32000;
        _window.Show();
    }

    private static bool IsSpotifyPage(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && (uri.Host == "spotify.com" || uri.Host.EndsWith(".spotify.com", StringComparison.OrdinalIgnoreCase));

    private async void OnResponse(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        if (!IsActive || _browser is not { } browser) return;
        try
        {
            var uri = new Uri(e.Request.Uri);
            if (uri.Scheme != "https" || uri.Host is not ("api.spotify.com" or "api-partner.spotify.com" or "spclient.wg.spotify.com")) return;
            if (e.Response.StatusCode is < 200 or >= 300) return;
            var headers = e.Request.Headers;
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
                    if (_credentials?.AccessToken != access) _capturedAt = DateTimeOffset.UtcNow;
                    _credentials = new(access, headers.Contains("Client-Token") ? headers.GetHeader("Client-Token") : _credentials?.ClientToken);
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
            if (_search == pending && tracks.Count > 0) pending.TrySetResult(tracks);
        }
        catch (Exception)
        {
            // Some web-player responses are binary, cancelled, or belong to a closed page.
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _idle?.Stop();
        _credentials = null;
        _search?.TrySetCanceled();
        _browser?.Dispose();
        _window?.Close();
    }
}
