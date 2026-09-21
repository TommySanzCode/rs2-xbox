using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using RS2XboxConnect;

static void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
static async Task Reject(Func<Task> action, string label) { try { await action(); } catch { Console.WriteLine("PASS: rejects " + label); return; } throw new Exception("Accepted " + label); }
static IPAddress Ip(byte a, byte b, byte c, byte d) => new(new byte[] { a, b, c, d });

foreach (var address in new[] { Ip(10,0,0,1), Ip(100,64,1,1), Ip(100,127,255,254), Ip(172,16,0,1), Ip(192,168,1,1), Ip(169,254,1,1), Ip(192,0,2,1), IPAddress.Loopback, IPAddress.Any, IPAddress.Broadcast })
    Check(!NetworkAddresses.PublicIPv4(address), "reject non-public hosting address");
Check(NetworkAddresses.PublicIPv4(Ip(8,8,8,8)), "classify public IPv4 without contacting it");
await Reject(() => Task.FromResult(UpnpRouter.ParseXml("<!DOCTYPE x [<!ENTITY leak SYSTEM 'file:///test'>]><x>&leak;</x>")), "router XML external entities");
await Reject(() => Task.FromResult(UpnpRouter.RouterUri(new Uri("http://127.0.0.1/"), "http://example.com/private", IPAddress.Loopback)), "router URL leaving gateway");

using (var handler = new RouterMock()) using (var router = UpnpRouter.ForTest(handler)) {
    await using (var lease = await router.Map(43595, CancellationToken.None)) {
        Check(handler.Adds == 1 && handler.LeaseSeconds == "3600", "finite router lease created");
        await lease.Renew(CancellationToken.None); Check(handler.Adds == 2, "owned mapping renewed");
    }
    Check(handler.Deletes == 1, "owned mapping removed on stop");
    var foreign = await router.Map(43595, CancellationToken.None); handler.Description = "another program";
    await foreign.DisposeAsync(); Check(handler.Deletes == 1, "another program's mapping is preserved");
    handler.External = Ip(100,64,1,1).ToString();
    await Reject(() => router.Map(43595, CancellationToken.None), "CGNAT before mapping");
}
using (var handler = new RouterMock { PermanentOnly = true }) using (var router = UpnpRouter.ForTest(handler))
    await Reject(() => router.Map(43595, CancellationToken.None), "routers requiring permanent mappings");

using var gameKey = RSA.Create(1024); var pub = gameKey.ExportParameters(false);
var world = new WorldInfo(Guid.NewGuid().ToString("N"), "Test world", Convert.ToHexStringLower(pub.Exponent!), Convert.ToHexStringLower(pub.Modulus!), true, 43594);
var identity = DirectIdentity.Create(); using var certificate = identity.OpenCertificate();
var invite = identity.Invite(world, Ip(8,8,8,8), 43595);
Check(DirectInvite.FromCode(invite.ToCode()) == invite, "private invitation code round trip");
Check(!invite.ToCode().Contains(identity.Certificate), "invitation excludes host private certificate");
await Reject(() => Task.FromResult((invite with { Secret = "bad" }).ToCode()), "malformed invitation secret");
await Reject(() => Task.FromResult(DirectInvite.FromCode("RS2D2-" + new string('x', 5000))), "oversized invitation");
await Reject(() => Task.FromResult(DirectInvite.FromCode("RS2D2-e30")), "missing invitation fields");

using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
await using var echo = new TcpService(IPAddress.Loopback, 0, async (client, token) => {
    var stream = client.GetStream(); var bytes = new byte[733]; int n;
    while ((n = await stream.ReadAsync(bytes, token)) != 0) await stream.WriteAsync(bytes.AsMemory(0,n), token);
}); echo.Start();
await using var host = DirectTunnel.Host(IPAddress.Loopback, 0, certificate, identity.Secret, echo.Port); host.Start();
var localInvite = invite with { Host = IPAddress.Loopback.ToString(), Port = host.Port };
await Task.WhenAll(Enumerable.Range(0,8).Select(async i => {
    using var stream = await DirectTunnel.Connect(localInvite, lifetime.Token, localTest:true);
    var sent = RandomNumberGenerator.GetBytes(262144 + i); var received = new byte[sent.Length];
    var read = stream.ReadExactlyAsync(received, lifetime.Token).AsTask();
    for (int offset=0;offset<sent.Length;offset+=941) await stream.WriteAsync(sent.AsMemory(offset, Math.Min(941,sent.Length-offset)), lifetime.Token);
    await read; Check(sent.SequenceEqual(received), "isolated encrypted stream " + i);
}));
await Reject(async () => { using var stream = await DirectTunnel.Connect(localInvite with { Secret = WorldInvite.NewSecret() }, lifetime.Token, true); }, "wrong invitation secret");
await Reject(async () => { using var stream = await DirectTunnel.Connect(localInvite with { CertificateSha256 = new string('0',64) }, lifetime.Token, true); }, "wrong certificate pin");
int reusablePort = host.Port;
using var held = await DirectTunnel.Connect(localInvite,lifetime.Token,true);
await host.DisposeAsync();
await Reject(async () => { await held.ReadExactlyAsync(new byte[1],lifetime.Token); }, "session after host stop");
var rotatedSecret = WorldInvite.NewSecret();
await using var rotatedHost = DirectTunnel.Host(IPAddress.Loopback,reusablePort,certificate,rotatedSecret,echo.Port); rotatedHost.Start();
await Reject(async () => { using var stream = await DirectTunnel.Connect(localInvite,lifetime.Token,true); }, "old invitation after rotation/restart");
using (var fresh = await DirectTunnel.Connect(localInvite with { Secret = rotatedSecret },lifetime.Token,true)) {
    await fresh.WriteAsync(new byte[]{42},lifetime.Token); var one=new byte[1]; await fresh.ReadExactlyAsync(one,lifetime.Token);
    Check(one[0]==42,"fresh invitation reconnects after host restart on the same port");
}

// Real UDP discovery and raw revision-225 RSA login rewrite. The world fixture
// stays in memory and passwords never leave the process in discovery packets.
var account = new XboxCharacter("tester", "testpassword"); bool approved = false;
var seed = RandomNumberGenerator.GetBytes(8); byte[]? decrypted = null;
await using var engine = new TcpService(IPAddress.Loopback, 0, async (client, token) => {
    var stream = client.GetStream(); await stream.WriteAsync(seed, token);
    var header = new byte[2]; await stream.ReadExactlyAsync(header, token);
    var body = new byte[header[1]]; await stream.ReadExactlyAsync(body, token);
    var key = gameKey.ExportParameters(true);
    decrypted = BigInteger.ModPow(new BigInteger(body.AsSpan(39),true,true), new BigInteger(key.D!,true,true),new BigInteger(key.Modulus!,true,true)).ToByteArray(true,true);
    await stream.WriteAsync(new byte[]{2,44,55}, token);
}); engine.Start();
await using var discovery = new XboxDiscovery(IPAddress.Loopback, Ip(255,0,0,0), world, token => DirectTunnel.Loopback(engine.Port, token), _ => approved ? account : null);
discovery.Start();
using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback,0));
const string nonce = "0123456789abcdef";
async Task<string> Discover() { await udp.SendAsync(Encoding.ASCII.GetBytes("RS2CONNECT2 " + nonce), new IPEndPoint(IPAddress.Loopback,XboxDiscovery.DiscoveryPort),lifetime.Token); return Encoding.ASCII.GetString((await udp.ReceiveAsync(lifetime.Token)).Buffer); }
Check((await Discover()).EndsWith(" PAIR"), "Xbox requires local approval"); approved = true;
var response = await Discover(); Check(response.Contains(" OK 43597 ") && !response.Contains(account.Password) && !response.Contains(account.Username), "discovery contains public connection details only");
var fields = response.Split(' ');
using (var client = new TcpClient()) {
    await client.ConnectAsync(IPAddress.Loopback,XboxDiscovery.LoginPort,lifetime.Token);
    var stream = client.GetStream(); var receivedSeed = new byte[8]; await stream.ReadExactlyAsync(receivedSeed,lifetime.Token);
    var plain = new byte[21]; plain[0]=10; seed.CopyTo(plain,9); plain[1]=17;
    var clear = plain.Concat("connect\nconnect\n"u8.ToArray()).ToArray();
    var cipher = BigInteger.ModPow(new BigInteger(clear,true,true),new BigInteger(Convert.FromHexString(fields[4]),true,true),new BigInteger(Convert.FromHexString(fields[5]),true,true)).ToByteArray(true,true);
    var body = new byte[40+cipher.Length]; body[0]=225; body[1]=1; body[38]=(byte)(cipher.Length+1); cipher.CopyTo(body,40);
    await stream.WriteAsync(new byte[]{16,(byte)body.Length},lifetime.Token); await stream.WriteAsync(body,lifetime.Token);
    var reply = new byte[3]; await stream.ReadExactlyAsync(reply,lifetime.Token);
    Check(reply.SequenceEqual(new byte[]{2,44,55}), "game reply survives Xbox bridge");
    Check(decrypted != null && decrypted.AsSpan(0,21).SequenceEqual(plain) && Encoding.ASCII.GetString(decrypted.AsSpan(21)) == "tester\ntestpassword\n", "character replaced; seed and ISAAC words preserved");
}
Console.WriteLine("Direct software checks passed. No home router was changed; cross-household and hardware tests remain separate.");

sealed class RouterMock : HttpMessageHandler
{
    public string External = new IPAddress(new byte[]{8,8,8,8}).ToString(), Description = "", LeaseSeconds = "";
    public int Adds, Deletes; public bool PermanentOnly;
    private string internalPort = "";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        var document = XDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        var action = request.Headers.GetValues("SOAPAction").Single().Split('#')[1].Trim('"');
        string Input(string name) => document.Descendants().Single(x=>x.Name.LocalName==name).Value;
        string payload;
        if(action=="GetExternalIPAddress") payload="<NewExternalIPAddress>"+External+"</NewExternalIPAddress>";
        else if(action=="GetSpecificPortMappingEntry") payload=Description==""?"<errorCode>714</errorCode>":$"<NewInternalClient>127.0.0.1</NewInternalClient><NewInternalPort>{internalPort}</NewInternalPort><NewPortMappingDescription>{Description}</NewPortMappingDescription><NewLeaseDuration>{LeaseSeconds}</NewLeaseDuration>";
        else if(action=="AddPortMapping") {
            if(PermanentOnly) payload="<errorCode>725</errorCode>";
            else { Adds++; Description=Input("NewPortMappingDescription"); internalPort=Input("NewInternalPort"); LeaseSeconds=Input("NewLeaseDuration"); payload=""; }
        } else if(action=="DeletePortMapping") { Deletes++; Description=""; payload=""; }
        else throw new Exception("Unexpected router action");
        return new(HttpStatusCode.OK){Content=new StringContent("<Envelope>"+payload+"</Envelope>")};
    }
}
