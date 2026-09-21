using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace RS2XboxConnect;

// Only forwards one configured game endpoint; no arbitrary destinations or packet capture.
public sealed class Gateway : IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly string target;
    private readonly int targetPort;
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentDictionary<long, Task> sessions = new();
    private Task? accept;
    private long nextId;
    public int ActiveConnections => sessions.Count;
    public long AcceptedConnections { get; private set; }
    public bool Running => accept is { IsCompleted: false };
    public Gateway(IPAddress address, int port, string target, int targetPort)
    {
        listener = new TcpListener(address, port); listener.Server.ExclusiveAddressUse = true;
        this.target = target; this.targetPort = targetPort;
    }
    public void Start() { listener.Start(16); accept = Accept(); }
    private async Task Accept()
    {
        try {
            while (!stop.IsCancellationRequested) {
                var client = await listener.AcceptTcpClientAsync(stop.Token);
                if (sessions.Count >= 64) { client.Dispose(); continue; }
                long id = Interlocked.Increment(ref nextId);
                AcceptedConnections++;
                var task = Forward(client);
                sessions[id] = task;
                _ = task.ContinueWith(_ => { sessions.TryRemove(id, out var ignored); }, TaskScheduler.Default);
            }
        } catch (OperationCanceledException) { } catch (SocketException) when (stop.IsCancellationRequested) { }
    }
    private async Task Forward(TcpClient client)
    {
        using (client) using (var remote = new TcpClient()) {
            try {
                client.NoDelay = remote.NoDelay = true;
                using var connect = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                connect.CancelAfter(TimeSpan.FromSeconds(10));
                await remote.ConnectAsync(target, targetPort, connect.Token);
                using var pair = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                var localStream = client.GetStream(); var remoteStream = remote.GetStream();
                var a = Pump(localStream, remoteStream, remote.Client, pair.Token);
                var b = Pump(remoteStream, localStream, client.Client, pair.Token);
                // Preserve TCP half-close while allowing a failed direction to cancel the other.
                var first = await Task.WhenAny(a, b);
                if (first.IsFaulted || first.IsCanceled) pair.Cancel();
                else pair.CancelAfter(TimeSpan.FromSeconds(30));
                await Task.WhenAll(a, b);
            } catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
        }
    }
    private static async Task Pump(Stream from, Stream to, Socket destination, CancellationToken token)
    {
        await from.CopyToAsync(to, 16384, token);
        try { destination.Shutdown(SocketShutdown.Send); } catch (SocketException) { }
    }
    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync(); listener.Stop();
        if (accept != null) await accept;
        await Task.WhenAll(sessions.Values); stop.Dispose();
    }
    public static async Task<bool> CheckGame(string host, int port)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var socket = new TcpClient();
        try {
            await socket.ConnectAsync(host, port, timeout.Token);
            // The revision-225 server sends an eight-byte seed immediately.
            await socket.GetStream().ReadExactlyAsync(new byte[8], timeout.Token);
            return true;
        } catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException) { return false; }
    }
    public static int FreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); return port;
    }
}
