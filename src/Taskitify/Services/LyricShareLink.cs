using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Taskitify.Models;
using Taskitify.Views;

namespace Taskitify.Services;

/// <summary>
/// "Share as a link" (experimental): a Lyrics Studio composition, and an optional dedication, packed
/// into the fragment of a website address. JSON (UTF-8) → gzip → base64url without padding. Nothing
/// is uploaded: the fragment never reaches a server. Contract: shared/lyrics-share-v1.md.
/// </summary>
public static class LyricShareLink
{
    /// <summary>
    /// Where shared compositions open: the official site. Its viewer is not ready yet, so the feature
    /// stays hidden in production builds (see <see cref="Enabled"/>).
    /// </summary>
    public const string BaseUrl = Infrastructure.OfficialLinks.LyricsShare;

    public const int Version = 1;

    /// <summary>Longest code produced (characters after '#'): fits chat apps and QR codes comfortably.</summary>
    public const int MaxCodeLength = 6000;

    public const int MaxName = 40, MaxMessage = 280, MaxText = 120, MaxLine = 200, MaxFields = 16, MaxFieldValue = 120;

    /// <summary>
    /// Development builds always; elsewhere only with TASKITIFY_SHARE_LINKS=1, so users never get
    /// links that open nothing.
    /// </summary>
    public static bool Enabled =>
#if DEBUG
        true;
#else
        Environment.GetEnvironmentVariable("TASKITIFY_SHARE_LINKS") == "1";
#endif

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Keeps accents and emoji as UTF-8 (smaller than \u escapes); the payload is never put in HTML unencoded.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public sealed record Payload(
        [property: JsonPropertyName("v")] int V,
        [property: JsonPropertyName("t")] string Template,
        [property: JsonPropertyName("f")] string? Format,
        [property: JsonPropertyName("s")] SongInfo Song,
        [property: JsonPropertyName("l")] IReadOnlyList<Line> Lines,
        [property: JsonPropertyName("o")] Look? Look,
        [property: JsonPropertyName("x")] IReadOnlyDictionary<string, string>? Fields,
        [property: JsonPropertyName("d")] Dedication? Dedication,
        [property: JsonPropertyName("c")] string? Created = null,
        [property: JsonPropertyName("r")] string? Reveal = null,
        [property: JsonPropertyName("lang")] string? Language = null);

    /// <summary>How the page opens the card: at once (default, also for links without <c>r</c>) or as a surprise to reveal.</summary>
    public const string RevealDirect = "direct", RevealSurprise = "surprise";

    public sealed record SongInfo(
        [property: JsonPropertyName("t")] string Title,
        [property: JsonPropertyName("a")] string Artist,
        [property: JsonPropertyName("b")] string? Album = null,
        [property: JsonPropertyName("sp")] string? SpotifyId = null,
        [property: JsonPropertyName("dur")] long? DurationMs = null);

    public sealed record Line(
        [property: JsonPropertyName("x")] string Text,
        [property: JsonPropertyName("ms")] long? Ms = null);

    /// <summary>Only what differs from the defaults is written.</summary>
    public sealed record Look(
        [property: JsonPropertyName("font")] string? Font = null,
        [property: JsonPropertyName("tf")] string? Typeface = null,
        [property: JsonPropertyName("scale")] double? Scale = null,
        [property: JsonPropertyName("align")] string? Align = null,
        [property: JsonPropertyName("color")] string? Color = null,
        [property: JsonPropertyName("cover")] bool? Cover = null,
        [property: JsonPropertyName("mark")] string? Mark = null,
        [property: JsonPropertyName("time")] bool? Time = null,
        [property: JsonPropertyName("img")] string? Image = null);

    public sealed record Dedication(
        [property: JsonPropertyName("from")] string? From = null,
        [property: JsonPropertyName("to")] string? To = null,
        [property: JsonPropertyName("msg")] string? Message = null);

    /// <summary>The payload for a composition as the Studio shows it. Images are never included.</summary>
    public static Payload Create(LyricCardContent content, LyricCardOptions options, string? spotifyId, string? from, string? to, string? message, DateTime? created = null, bool surprise = false, string? language = null)
    {
        options.Variants.TryGetValue(options.Style, out var variant);
        var look = new Look(
            Font: options.Font == LyricCardFont.Auto ? null : Lower(options.Font),
            Typeface: variant?.Typeface is { } typeface ? Lower(typeface) : null,
            Scale: Math.Abs(options.FontScale - 1) < 0.001 ? null : Math.Round(options.FontScale, 2),
            Align: options.Align == LyricCardAlign.Auto ? null : Lower(options.Align),
            Color: string.IsNullOrWhiteSpace(options.Color) ? null : options.Color!.ToUpperInvariant(),
            Cover: options.ShowCover ? null : false,
            Mark: options.Mark == LyricCardMark.Spotify ? null : Lower(options.Mark),
            Time: options.ShowTimestamp ? true : null,
            Image: variant is null || variant.ImageSource == StudioImageSource.Album ? null : Lower(variant.ImageSource));
        var fields = variant?.Fields is { Count: > 0 } f
            ? f.Where(p => !string.IsNullOrEmpty(p.Key)).Take(MaxFields).OrderBy(p => p.Key, StringComparer.Ordinal)
                .ToDictionary(p => Cut(p.Key, 32)!, p => Cut(p.Value, MaxFieldValue) ?? "", StringComparer.Ordinal)
            : null;
        var dedication = new Dedication(Cut(from, MaxName), Cut(to, MaxName), Cut(message, MaxMessage));
        return new Payload(
            Version,
            options.Style.ToString(),
            options.Format == LyricCardFormat.Story ? null : Lower(options.Format),
            new SongInfo(Cut(content.Title, MaxText) ?? "", Cut(content.Artist, MaxText) ?? "", Cut(content.Album, MaxText),
                IsSpotifyId(spotifyId) ? spotifyId : null, content.Duration is { } d ? (long)d.TotalMilliseconds : null),
            content.Lines.Select(l => new Line(Cut(l.Text, MaxLine) ?? "", l.Time is { } t ? (long)t.TotalMilliseconds : null)).ToList(),
            look == new Look() ? null : look,
            fields,
            dedication == new Dedication() ? null : dedication,
            (created ?? DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            surprise ? RevealSurprise : null,
            LanguageTag(language ?? LocalizationService.Language));
    }

    /// <summary>The app's language as a BCP 47 tag (the interface language of the sender, not of the lyrics).</summary>
    public static string LanguageTag(string code) => code switch
    {
        "pt" => "pt-BR",
        _ => code,
    };

    public static string ToJson(Payload payload) => JsonSerializer.Serialize(payload, Json);

    /// <summary>JSON → UTF-8 → gzip → base64url (no padding).</summary>
    public static string Encode(string json)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(buffer.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string Decode(string code)
    {
        var base64 = code.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        using var gzip = new GZipStream(new MemoryStream(Convert.FromBase64String(base64)), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static Payload? Parse(string json) => JsonSerializer.Deserialize<Payload>(json, Json);

    /// <summary>The whole link, or null when it would be longer than <see cref="MaxCodeLength"/>.</summary>
    public static string? Link(Payload payload)
    {
        var code = Encode(ToJson(payload));
        return code.Length > MaxCodeLength ? null : $"{BaseUrl}#{code}";
    }

    // ---- Short links (POST /api/share) ----------------------------------------------------------

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>
    /// The API that stores a code and returns a short link: the official one, or for development the
    /// address in TASKITIFY_SHARE_API (e.g. the website running locally).
    /// </summary>
    public static Uri ShortApi =>
        Uri.TryCreate(Environment.GetEnvironmentVariable("TASKITIFY_SHARE_API"), UriKind.Absolute, out var custom) ? custom : new Uri(Infrastructure.OfficialLinks.LyricsShareApi);

    /// <summary>
    /// One POST with the code the app already made; the short URL, or null on any failure (offline,
    /// timeout, rate limit, server error, an unexpected answer). Never retried: creating is not idempotent.
    /// </summary>
    public static async Task<string?> ShortenAsync(string code, Uri? api = null, CancellationToken ct = default)
    {
        api ??= ShortApi;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, api)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { code }), Encoding.UTF8, "application/json"),
            };
            request.Headers.UserAgent.ParseAdd($"Taskitify/{typeof(LyricShareLink).Assembly.GetName().Version?.ToString(3) ?? "1.0"}");
            using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode != System.Net.HttpStatusCode.Created)
            {
                Infrastructure.Log.Info($"Short link not created: HTTP {(int)response.StatusCode}");
                return null;
            }
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var url = json.RootElement.TryGetProperty("url", out var value) ? value.GetString() : null;
            return IsShortLink(url, api) ? url : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Infrastructure.Log.Info($"Short link not created: {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary>The API's own origin, then /s/ and a 12-character id of letters and digits.</summary>
    public static bool IsShortLink(string? url, Uri api)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != api.Scheme || !string.Equals(uri.Authority, api.Authority, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return false;
        var path = uri.AbsolutePath;
        return path.Length == 15 && path.StartsWith("/s/", StringComparison.Ordinal) && path[3..].All(char.IsAsciiLetterOrDigit);
    }

    public static bool IsSpotifyId(string? id) => id is { Length: 22 } && id.All(char.IsAsciiLetterOrDigit);

    private static string Lower<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    /// <summary>Trimmed, empty as null, at most <paramref name="max"/> characters (never splitting a surrogate pair).</summary>
    private static string? Cut(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim();
        if (text.Length <= max)
            return text;
        var info = new StringInfo(text);
        var cut = info.LengthInTextElements > max ? info.SubstringByTextElements(0, max) : text;
        while (cut.Length > max)
            cut = new StringInfo(cut).SubstringByTextElements(0, new StringInfo(cut).LengthInTextElements - 1);
        return cut.TrimEnd();
    }
}
