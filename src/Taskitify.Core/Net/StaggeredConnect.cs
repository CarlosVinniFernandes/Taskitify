using System.Net;
using System.Net.Sockets;

namespace Taskitify.Core.Net;

/// <summary>
/// Connects to a host's addresses the way browsers do (RFC 8305, simplified): families
/// alternate, the next address starts 250 ms after the previous one unless that one already
/// connected, and the first to connect wins. .NET otherwise waits on the first address, often
/// IPv6, until the system gives up, and some networks drop IPv6 to some hosts (Spotify's image
/// CDN, measured: IPv6 never answered, IPv4 in 0.7 s).
/// </summary>
public static class StaggeredConnect
{
    private static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(250);

    /// <summary>For <see cref="SocketsHttpHandler.ConnectCallback"/>.</summary>
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct).ConfigureAwait(false);
        var socket = await FirstAsync(Interleave(addresses),
            (address, token) => ConnectSocketAsync(address, context.DnsEndPoint.Port, token), Stagger, ct).ConfigureAwait(false);
        return new NetworkStream(socket, ownsSocket: true);
    }

    /// <summary>The first address's family first, then the families taking turns.</summary>
    internal static IReadOnlyList<IPAddress> Interleave(IReadOnlyList<IPAddress> addresses)
    {
        if (addresses.Count == 0)
            return addresses;
        var first = addresses.Where(a => a.AddressFamily == addresses[0].AddressFamily).ToList();
        var other = addresses.Where(a => a.AddressFamily != addresses[0].AddressFamily).ToList();
        var result = new List<IPAddress>(addresses.Count);
        for (var i = 0; i < Math.Max(first.Count, other.Count); i++)
        {
            if (i < first.Count) result.Add(first[i]);
            if (i < other.Count) result.Add(other[i]);
        }
        return result;
    }

    /// <summary>
    /// Start <paramref name="connect"/> on each address in turn, the next one after
    /// <paramref name="stagger"/> or as soon as the previous fails; the first success wins and
    /// the rest are cancelled, or closed if they connect anyway.
    /// </summary>
    internal static async Task<T> FirstAsync<T>(IReadOnlyList<IPAddress> addresses, Func<IPAddress, CancellationToken, Task<T>> connect,
        TimeSpan stagger, CancellationToken ct) where T : IDisposable
    {
        using var race = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var running = new List<Task<T>>();
        Exception? failure = null;
        var next = 0;
        try
        {
            while (next < addresses.Count || running.Count > 0)
            {
                if (next < addresses.Count)
                    running.Add(connect(addresses[next++], race.Token));
                var waits = new List<Task>(running);
                if (next < addresses.Count)
                    waits.Add(Task.Delay(stagger, race.Token));
                var done = await Task.WhenAny(waits).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (done is not Task<T> attempt)
                    continue; // the stagger elapsed: start the next address
                running.Remove(attempt);
                if (attempt.IsCompletedSuccessfully)
                    return attempt.Result;
                failure = attempt.Exception?.InnerException ?? failure;
            }
            throw failure ?? new SocketException((int)SocketError.HostNotFound);
        }
        finally
        {
            race.Cancel();
            foreach (var loser in running)
                _ = loser.ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                        t.Result.Dispose();
                    else
                        _ = t.Exception;
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private static async Task<Socket> ConnectSocketAsync(IPAddress address, int port, CancellationToken ct)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), ct).ConfigureAwait(false);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
