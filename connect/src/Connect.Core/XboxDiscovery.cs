using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RS2XboxConnect;

public sealed record XboxCharacter(string Username, string Password);
public sealed record FoundXbox(string Address, string Code, DateTime Seen);

// Discovery never contains character credentials. A trusted home-LAN Xbox must
// be approved in the app; the login bridge inserts its character on the PC.
// LAN approval is address-based, not protection against a hostile LAN operator.
public sealed class XboxDiscovery : IAsyncDisposable
{
    public const int DiscoveryPort = 43596;
    public const int LoginPort = 43597;
    private readonly IPAddress local, mask;
    private readonly UdpClient udp;
    private readonly UdpClient reply;
    private readonly RSA rsa = RSA.Create(1024); // Existing revision-225 wire format limit.
    private readonly WorldInfo world;
    private readonly Func<CancellationToken, Task<Stream>> upstream;
    private readonly Func<string, XboxCharacter?> character;
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentDictionary<string, FoundXbox> found = new();
    private readonly TcpService login;
    private Task? receive;
    public IReadOnlyList<FoundXbox> Found => found.Values.Where(x => DateTime.UtcNow - x.Seen < TimeSpan.FromMinutes(2)).OrderBy(x => x.Address).ToList();
    public int Connections => login.Connections;
    public string Exponent { get; }
    public string Modulus { get; }
    public XboxDiscovery(IPAddress local, IPAddress mask, WorldInfo world, Func<CancellationToken, Task<Stream>> upstream, Func<string, XboxCharacter?> character)
    {
        this.local = local; this.mask = mask; this.world = world; this.upstream = upstream; this.character = character;
        udp = new UdpClient(AddressFamily.InterNetwork); udp.ExclusiveAddressUse = true;
        reply = new UdpClient(new IPEndPoint(local, 0));
        var key = rsa.ExportParameters(false); Exponent = Convert.ToHexStringLower(key.Exponent!); Modulus = Convert.ToHexStringLower(key.Modulus!);
        login = new TcpService(local, LoginPort, Login);
    }
    public void Start()
    {
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort)); login.Start(); receive = Receive();
    }
    private async Task Receive()
    {
        var window = DateTime.UtcNow; int count = 0;
        try {
            while (!stop.IsCancellationRequested) {
                var packet = await udp.ReceiveAsync(stop.Token);
                if (DateTime.UtcNow - window > TimeSpan.FromSeconds(1)) { window = DateTime.UtcNow; count = 0; }
                if (++count > 32 || packet.Buffer.Length > 64 || !NetworkAddresses.SameSubnet(local, packet.RemoteEndPoint.Address, mask)) continue;
                var text = Encoding.ASCII.GetString(packet.Buffer);
                var match = Regex.Match(text, "^RS2CONNECT2 ([a-f0-9]{16})$");
                if (!match.Success) continue;
                var ip = packet.RemoteEndPoint.Address.ToString(); var nonce = match.Groups[1].Value;
                if (!found.ContainsKey(ip) && found.Count >= 32) {
                    foreach (var stale in found.Where(x => DateTime.UtcNow - x.Value.Seen > TimeSpan.FromMinutes(2)).ToList()) found.TryRemove(stale.Key, out _);
                    if (found.Count >= 32) continue;
                }
                found[ip] = new(ip, nonce[^6..].ToUpperInvariant(), DateTime.UtcNow);
                var approved = character(ip) != null;
                var response = approved ? $"RS2CONNECT2 {nonce} OK {LoginPort} {Exponent} {Modulus} {(world.Members ? 1 : 0)}" : $"RS2CONNECT2 {nonce} PAIR";
                await reply.SendAsync(Encoding.ASCII.GetBytes(response), packet.RemoteEndPoint, stop.Token);
            }
        } catch (OperationCanceledException) { } catch (SocketException) when (stop.IsCancellationRequested) { }
    }
    private async Task Login(TcpClient client, CancellationToken token)
    {
        var ip = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
        if (!NetworkAddresses.SameSubnet(local, ip, mask)) return;
        var account = character(ip.ToString()); if (account == null) return;
        XboxConfig.ValidateLogin(account.Username, account.Password);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var remote = await upstream(timeout.Token);
        var stream = client.GetStream(); var seed = new byte[8]; await remote.ReadExactlyAsync(seed, timeout.Token); await stream.WriteAsync(seed, timeout.Token);
        var header = new byte[2]; await stream.ReadExactlyAsync(header, timeout.Token);
        if (header[0] is not (16 or 18) || header[1] < 40) return;
        var body = new byte[header[1]]; await stream.ReadExactlyAsync(body, timeout.Token);
        var rewritten = Rewrite(body, rsa.ExportParameters(true), world, account, seed);
        header[1] = checked((byte)rewritten.Length);
        await remote.WriteAsync(header, timeout.Token); await remote.WriteAsync(rewritten, timeout.Token);
        await DirectTunnel.Bridge(stream, remote, token);
    }
    public static byte[] Rewrite(byte[] body, RSAParameters key, WorldInfo world, XboxCharacter account, byte[] seed)
    {
        XboxConfig.ValidateLogin(account.Username, account.Password);
        if (seed.Length != 8 || body.Length < 40 || body[0] != 225 || body[1] > 1 || body[38] != body.Length - 39 || body[38] > 129)
            throw new InvalidDataException("Unsupported Xbox login packet.");
        static BigInteger Number(byte[] value) => new(value, true, true);
        var cipher = new BigInteger(body.AsSpan(39), true, true); var modulus = Number(key.Modulus!);
        if (cipher <= 0 || cipher >= modulus) throw new InvalidDataException("Invalid login ciphertext.");
        var plain = BigInteger.ModPow(cipher, Number(key.D!), modulus).ToByteArray(true, true);
        if (plain.Length != 37 || plain[0] != 10 || !plain.AsSpan(9, 8).SequenceEqual(seed) || !plain.AsSpan(21).SequenceEqual("connect\nconnect\n"u8))
            throw new InvalidDataException("Invalid Connect login handshake.");
        var replacement = plain.AsSpan(0, 21).ToArray().Concat(Encoding.ASCII.GetBytes(account.Username + "\n" + account.Password + "\n")).ToArray();
        var encrypted = BigInteger.ModPow(Number(replacement), Number(Convert.FromHexString(world.RsaExponent)), Number(Convert.FromHexString(world.RsaModulus))).ToByteArray(true, true);
        var result = new byte[40 + encrypted.Length]; body.AsSpan(0, 38).CopyTo(result);
        result[38] = checked((byte)(encrypted.Length + 1)); result[39] = 0; encrypted.CopyTo(result, 40);
        CryptographicOperations.ZeroMemory(plain); CryptographicOperations.ZeroMemory(replacement);
        return result;
    }
    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync(); if (receive != null) await receive;
        await login.DisposeAsync(); udp.Dispose(); reply.Dispose(); rsa.Dispose(); stop.Dispose();
    }
}
