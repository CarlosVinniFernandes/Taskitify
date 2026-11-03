using System.Diagnostics;
using System.Text.Json;

namespace Taskitify.Platform.Linux.Spotify;

/// <summary>Talks JSON lines to <c>spotify_web.py</c>, which owns the WebKitGTK window and profile.</summary>
public sealed class HelperWebPlayer(string scriptPath) : IWebPlayer
{
    private readonly object _gate = new();
    private Process? _process;
    private TaskCompletionSource<bool>? _cookies;
    private TaskCompletionSource? _cleared;

    public event Action<WebRequest>? Request;

    public event Action<bool>? Loaded;

    public void Navigate(string url) => Send(new { cmd = "navigate", url });

    public void ShowLogin(string url) => Send(new { cmd = "login", url });

    public async Task<bool> HasSessionCookieAsync()
    {
        var answer = _cookies = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Send(new { cmd = "cookies" });
        return await answer.Task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
    }

    public async Task ClearAsync()
    {
        var done = _cleared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Send(new { cmd = "clear" });
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    }

    public void Close()
    {
        Process? process;
        lock (_gate)
        {
            process = _process;
            _process = null;
        }
        if (process is null)
            return;
        try
        {
            process.StandardInput.WriteLine("{\"cmd\":\"quit\"}");
            process.StandardInput.Flush();
            if (!process.WaitForExit(2000))
                process.Kill();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // Already gone.
        }
        process.Dispose();
    }

    public void Dispose() => Close();

    private void Send(object command)
    {
        lock (_gate)
        {
            _process ??= Start();
            _process.StandardInput.WriteLine(JsonSerializer.Serialize(command));
            _process.StandardInput.Flush();
        }
    }

    private Process Start()
    {
        var process = Process.Start(new ProcessStartInfo("python3")
        {
            ArgumentList = { scriptPath },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Could not start python3");
        _ = Task.Run(() => ReadEvents(process));
        return process;
    }

    private void ReadEvents(Process process)
    {
        try
        {
            string? line;
            while ((line = process.StandardOutput.ReadLine()) is not null)
                Handle(line);
        }
        catch (IOException)
        {
        }

        // The helper died (no python3-gi/WebKit, or crashed): whatever was loading did not load.
        bool current;
        lock (_gate)
        {
            current = ReferenceEquals(_process, process);
            if (current)
                _process = null;
        }
        if (current)
            Loaded?.Invoke(false);
    }

    private void Handle(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            switch (root.GetProperty("event").GetString())
            {
                case "request":
                    Request?.Invoke(ReadRequest(root));
                    break;
                case "load":
                    Loaded?.Invoke(root.GetProperty("ok").GetBoolean());
                    break;
                case "cookies":
                    _cookies?.TrySetResult(root.GetProperty("signedIn").GetBoolean());
                    break;
                case "cleared":
                    _cleared?.TrySetResult();
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Not an event from the helper; ignore.
        }
    }

    private static WebRequest ReadRequest(JsonElement e)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in e.GetProperty("headers").EnumerateObject())
            headers[h.Name] = h.Value.ToString();
        return new WebRequest(
            e.GetProperty("method").GetString() ?? "GET",
            e.GetProperty("url").GetString() ?? "",
            headers,
            e.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null,
            e.GetProperty("status").GetInt32(),
            e.TryGetProperty("response", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null);
    }
}
