using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Taskitify.Core.Lyrics;

public interface ILrclibClient
{
    Task<LrclibRecord?> GetAsync(TrackQuery query, CancellationToken ct);

    Task<IReadOnlyList<LrclibRecord>> SearchAsync(string title, string artist, CancellationToken ct);
}

/// <summary>Thin client for https://lrclib.net (no API key). See https://lrclib.net/docs.</summary>
public sealed class LrclibClient : ILrclibClient
{
    public static readonly Uri BaseAddress = new("https://lrclib.net/");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // LRCLIB sits behind a Cloudflare rate limit (observed: ~1 accepted request per 5-6 s per
    // IP under load, plus occasional 503). Requests are therefore serialized with a minimum
    // spacing, and 429/503 are retried with backoff instead of failing the whole lookup.
    // ~1 minute in total: under load LRCLIB often refuses 5+ requests in a row before one passes.
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6),
        TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10),
    ];

    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _nextRequestAt = DateTimeOffset.MinValue;

    /// <param name="http">Client whose BaseAddress is <see cref="BaseAddress"/> and that sends a User-Agent.</param>
    /// <param name="delay">Waiting primitive (tests pass a fake).</param>
    public LrclibClient(HttpClient http, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http;
        _delay = delay ?? Task.Delay;
    }

    /// <summary>Minimum time between two requests from this app.</summary>
    public TimeSpan MinSpacing { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Number of rate-limited (429/503) responses seen; for diagnostics.</summary>
    public int RateLimitedCount { get; private set; }

    public static HttpClient CreateHttpClient(string userAgent)
    {
        var http = new HttpClient { BaseAddress = BaseAddress, Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return http;
    }

    public async Task<LrclibRecord?> GetAsync(TrackQuery query, CancellationToken ct)
    {
        var url = $"api/get?track_name={E(query.Title)}&artist_name={E(query.Artist)}";
        if (!string.IsNullOrWhiteSpace(query.Album))
            url += $"&album_name={E(query.Album)}";
        if (query.Duration is { } d && d > TimeSpan.Zero)
            url += $"&duration={Math.Round(d.TotalSeconds).ToString(CultureInfo.InvariantCulture)}";

        using var response = await SendAsync(url, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LrclibRecord>(Json, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LrclibRecord>> SearchAsync(string title, string artist, CancellationToken ct)
    {
        var url = $"api/search?track_name={E(title)}&artist_name={E(artist)}";
        using var response = await SendAsync(url, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return [];
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<LrclibRecord>>(Json, ct).ConfigureAwait(false) ?? [];
    }

    /// <summary>
    /// One request at a time, spaced by <see cref="MinSpacing"/>. On 429/503 waits
    /// (Retry-After, or 1, 2, 4, 6, 8, 8, 10, 10, 10 s) and retries; the last response is returned as is.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(string url, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var wait = _nextRequestAt - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                    await _delay(wait, ct).ConfigureAwait(false);
                try
                {
                    response = await _http.GetAsync(url, ct).ConfigureAwait(false);
                }
                finally
                {
                    _nextRequestAt = DateTimeOffset.UtcNow + MinSpacing;
                }
            }
            finally
            {
                _gate.Release();
            }

            if (response.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable))
                return response;

            RateLimitedCount++;
            if (attempt >= RetryDelays.Length)
                return response;

            var delay = RetryDelays[attempt];
            if (response.Headers.RetryAfter?.Delta is { } retryAfter && retryAfter > delay)
                delay = retryAfter < MaxRetryAfter ? retryAfter : MaxRetryAfter;
            response.Dispose();
            await _delay(delay, ct).ConfigureAwait(false);
        }
    }

    private static string E(string? s) => Uri.EscapeDataString(s ?? string.Empty);
}
