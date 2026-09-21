using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RS2XboxConnect;

// A bounded listener shared by direct TLS and the Xbox login bridge. It never
// accepts a destination from the network; each instance has one game target.
public sealed class TcpService : IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly Func<TcpClient, CancellationToken, Task> handle;
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentDictionary<long, Task> sessions = new();
    private Task? accept;
    private long nextId;
    private int disposed;
    private long authenticated;
    public long AuthenticatedConnections => Interlocked.Read(ref authenticated);
    internal void RecordAuthentication() => Interlocked.Increment(ref authenticated);
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public int Connections => sessions.Count;
    public bool Running => accept is { IsCompleted: false };
    public Action<Exception>? Error { get; set; }
    public TcpService(IPAddress address, int port, Func<TcpClient, CancellationToken, Task> handle)
    {
        listener = new(address, port); listener.Server.ExclusiveAddressUse = true; this.handle = handle;
    }
    public void Start() { listener.Start(16); accept = Accept(); }
    private async Task Accept()
    {
        var window = DateTime.UtcNow; int attempts = 0;
        try {
            while (!stop.IsCancellationRequested) {
                var client = await listener.AcceptTcpClientAsync(stop.Token);
                if (DateTime.UtcNow - window > TimeSpan.FromMinutes(1)) { window = DateTime.UtcNow; attempts = 0; }
                if (++attempts > 120 || sessions.Count >= 64) { client.Dispose(); continue; }
                var id = Interlocked.Increment(ref nextId);
                var task = Run(client); sessions[id] = task;
                _ = task.ContinueWith(_ => sessions.TryRemove(id, out var ignored), TaskScheduler.Default);
            }
        } catch (OperationCanceledException) { } catch (SocketException) when (stop.IsCancellationRequested) { }
    }
    private async Task Run(TcpClient client)
    {
        using (client) {
            client.NoDelay = true;
            try { await handle(client, stop.Token); }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or AuthenticationException or ObjectDisposedException or CryptographicException or InvalidDataException) { Error?.Invoke(ex); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await stop.CancelAsync(); listener.Stop();
        if (accept != null) await accept;
        await Task.WhenAll(sessions.Values); stop.Dispose();
    }
}

public static class DirectTunnel
{
    private static readonly byte[] magic = "RS2DIR02"u8.ToArray();
    public static TcpService Host(IPAddress address, int port, X509Certificate2 certificate, string secret, int enginePort)
    {
        var expected = Convert.FromHexString(secret);
        if (expected.Length != 32) throw new ArgumentException("Invalid direct secret.");
        TcpService? service = null;
        service = new(address, port, async (client, token) => {
            using var ssl = new SslStream(client.GetStream(), false);
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(token); handshake.CancelAfter(TimeSpan.FromSeconds(10));
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, handshake.Token);
            var request = new byte[40]; await ssl.ReadExactlyAsync(request, handshake.Token);
            if (!request.AsSpan(0, 8).SequenceEqual(magic) || !CryptographicOperations.FixedTimeEquals(request.AsSpan(8), expected)) return;
            using var remote = new TcpClient { NoDelay = true };
            await remote.ConnectAsync(IPAddress.Loopback, enginePort, handshake.Token);
            service!.RecordAuthentication();
            await ssl.WriteAsync(new byte[] { 1 }, handshake.Token);
            await Bridge(ssl, remote.GetStream(), token);
        });
        return service;
    }
    public static async Task<Stream> Connect(DirectInvite invite, CancellationToken token, bool localTest = false)
    {
        if (!localTest) invite.Validate();
        var client = new TcpClient { NoDelay = true };
        try {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await client.ConnectAsync(invite.Host, invite.Port, timeout.Token);
            var pin = Convert.FromHexString(invite.CertificateSha256);
            var ssl = new SslStream(client.GetStream(), false, (_, cert, _, _) => {
                if (cert == null || pin.Length != 32) return false;
                using var parsed = X509CertificateLoader.LoadCertificate(cert.GetRawCertData());
                return parsed.NotBefore.ToUniversalTime() <= DateTime.UtcNow && parsed.NotAfter.ToUniversalTime() > DateTime.UtcNow &&
                    CryptographicOperations.FixedTimeEquals(SHA256.HashData(parsed.RawData), pin);
            });
            try {
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "RS2 Xbox Connect", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, timeout.Token);
                await ssl.WriteAsync(magic.Concat(Convert.FromHexString(invite.Secret)).ToArray(), timeout.Token);
                var reply = new byte[1]; await ssl.ReadExactlyAsync(reply, timeout.Token);
                if (reply[0] != 1) throw new AuthenticationException("The host rejected this invitation.");
                return ssl; // SslStream owns the NetworkStream and its socket.
            } catch { ssl.Dispose(); throw; }
        } catch { client.Dispose(); throw; }
    }
    public static async Task<Stream> Loopback(int port, CancellationToken token)
    {
        var client = new TcpClient { NoDelay = true };
        try { await client.ConnectAsync(IPAddress.Loopback, port, token); return client.GetStream(); }
        catch { client.Dispose(); throw; }
    }
    public static async Task Bridge(Stream left, Stream right, CancellationToken token)
    {
        using var pair = CancellationTokenSource.CreateLinkedTokenSource(token);
        var a = Pump(left, right, pair.Token); var b = Pump(right, left, pair.Token);
        var first = await Task.WhenAny(a, b);
        if (first.IsFaulted || first.IsCanceled) pair.Cancel(); else pair.CancelAfter(TimeSpan.FromSeconds(5));
        try { await Task.WhenAll(a, b); } finally { await pair.CancelAsync(); }
    }
    private static async Task Pump(Stream from, Stream to, CancellationToken token)
    {
        await from.CopyToAsync(to, 16384, token);
        if (to is SslStream ssl) await ssl.ShutdownAsync().WaitAsync(token);
        else if (to is NetworkStream network) { try { network.Socket.Shutdown(SocketShutdown.Send); } catch (SocketException) { } }
    }
}
