using System.Net;
using System.Net.Sockets;
using Taskitify.Core.Net;

namespace Taskitify.Tests;

public sealed class StaggeredConnectTests
{
    private static readonly IPAddress V6a = IPAddress.Parse("2001:db8::1");
    private static readonly IPAddress V6b = IPAddress.Parse("2001:db8::2");
    private static readonly IPAddress V4a = IPAddress.Parse("192.0.2.1");
    private static readonly IPAddress V4b = IPAddress.Parse("192.0.2.2");
    private static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(50);

    private sealed class Connection(IPAddress address) : IDisposable
    {
        public IPAddress Address { get; } = address;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void Families_take_turns_starting_with_the_first()
    {
        Assert.Equal([V6a, V4a, V6b, V4b], StaggeredConnect.Interleave([V6a, V6b, V4a, V4b]));
        Assert.Equal([V4a, V6a, V4b], StaggeredConnect.Interleave([V4a, V4b, V6a]));
    }

    [Fact]
    public async Task An_address_that_never_answers_does_not_hold_up_the_next()
    {
        // Like IPv6 to Spotify's image CDN on some networks: the connection just hangs.
        var cancelled = false;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var connection = await StaggeredConnect.FirstAsync([V6a, V4a], async (address, ct) =>
        {
            if (address.Equals(V6a))
            {
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { cancelled = true; throw; }
            }
            return new Connection(address);
        }, Stagger, default);

        Assert.Equal(V4a, connection.Address);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2));
        await Task.Delay(100);
        Assert.True(cancelled);
    }

    [Fact]
    public async Task A_failed_address_starts_the_next_at_once()
    {
        var started = new List<IPAddress>();
        var connection = await StaggeredConnect.FirstAsync([V6a, V4a], (address, _) =>
        {
            started.Add(address);
            return address.Equals(V6a)
                ? Task.FromException<Connection>(new SocketException((int)SocketError.NetworkUnreachable))
                : Task.FromResult(new Connection(address));
        }, TimeSpan.FromMinutes(1), default);

        Assert.Equal(V4a, connection.Address);
        Assert.Equal([V6a, V4a], started);
    }

    [Fact]
    public async Task When_every_address_fails_the_last_failure_surfaces()
    {
        var error = await Assert.ThrowsAsync<SocketException>(() => StaggeredConnect.FirstAsync<Connection>([V6a, V4a],
            (address, _) => Task.FromException<Connection>(new SocketException(address.Equals(V4a)
                ? (int)SocketError.ConnectionRefused : (int)SocketError.NetworkUnreachable)), Stagger, default));
        Assert.Equal(SocketError.ConnectionRefused, error.SocketErrorCode);
    }

    [Fact]
    public async Task A_slower_address_that_connects_after_the_winner_is_closed()
    {
        var slow = new TaskCompletionSource<Connection>();
        var connection = await StaggeredConnect.FirstAsync([V6a, V4a], (address, _) =>
            address.Equals(V6a) ? slow.Task : Task.FromResult(new Connection(address)), Stagger, default);

        var late = new Connection(V6a);
        slow.SetResult(late);

        Assert.Equal(V4a, connection.Address);
        Assert.True(late.Disposed);
        Assert.False(connection.Disposed);
    }
}
