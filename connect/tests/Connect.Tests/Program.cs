using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using RS2XboxConnect;

static void Check(bool pass, string label) { if (!pass) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
static void Reject(Action action, string label) { try { action(); } catch { Console.WriteLine("PASS: rejects " + label); return; } throw new Exception("Accepted " + label); }

using var caKey = RSA.Create(2048);
var request = new CertificateRequest("CN=Connect test CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
using var ca = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
var world = new WorldInvite { Relay = new RelayProfile { Host = "relay.example.com", Token = WorldInvite.NewSecret(), CaPem = ca.ExportCertificatePem() }, RsaExponent = "010001", RsaModulus = new string('a', 256) };
world.Validate(); Check(true, "valid versioned invitation and CA");
Reject(() => (world with { Revision = 317 }).Validate(), "wrong revision");
Reject(() => (world with { Relay = world.Relay with { Host = "example.com\nmalicious = true" } }).Validate(), "TOML endpoint injection");
Reject(() => (world with { Relay = world.Relay with { CaPem = ca.ExportCertificatePem() + "PRIVATE KEY" } }).Validate(), "private key in invitation");
var template = "\uFEFFsocketip=old\nlowmem=0\nxbox_video=720\nxbox_audio=1\nxbox_music=1\ncustom_controls=keep\nusername=old\nusername=duplicate\n[other]\nsocketip=untouched\n";
var exported = XboxConfig.Export(template, world, "192.0.2.10", 43595, "testuser", "uniquepass");
Check(exported.Contains("lowmem=0\nxbox_video=720\nxbox_audio=1\nxbox_music=1\ncustom_controls=keep"), "preserves enhanced graphics, audio and controls");
Check(exported.Contains("portoff = 1") && exported.IndexOf("rsa_modulus =") < exported.IndexOf("[other]") && exported.Contains("[other]\nsocketip=untouched"), "INI section boundaries and endpoint mapping");
Check(!exported.Contains("username=duplicate") && exported.Contains("members = 0"), "duplicate removal and inherited members inversion");
Reject(() => XboxConfig.Export("", world, "127.0.0.1", 43594, "user", "pass"), "loopback Xbox address");
Reject(() => XboxConfig.Export("", world, "192.0.2.10", 43594, "user", "pass\nlowmem=1"), "INI credential injection");
var frp = FrpConfig.Create(world, false, "C:\\a b\\ca.crt", 43594, 45000, 45001, "testadmin");
Check(frp.Contains("trustedCaFile") && frp.Contains("serverName = \"relay.example.com\"") && frp.Contains("type = \"stcp\"") && !frp.Contains("remotePort"), "verified TLS and private-only forwarding config");

// Eight concurrent streams, fragmented writes and half-close exercise backpressure and isolation.
var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
int echoPort = ((IPEndPoint)listener.LocalEndpoint).Port;
var echo = Task.Run(async () => {
    var sessions = new List<Task>();
    for (int i = 0; i < 8; i++) {
        var client = await listener.AcceptTcpClientAsync();
        sessions.Add(Task.Run(async () => { using (client) { var stream = client.GetStream(); var buffer = new byte[733]; int read; while ((read = await stream.ReadAsync(buffer)) > 0) await stream.WriteAsync(buffer.AsMemory(0, read)); } }));
    }
    await Task.WhenAll(sessions);
});
int gatewayPort = Gateway.FreeLoopbackPort();
await using (var gateway = new Gateway(IPAddress.Loopback, gatewayPort, "127.0.0.1", echoPort)) {
    gateway.Start();
    await Task.WhenAll(Enumerable.Range(0, 8).Select(async i => {
        using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, gatewayPort);
        var payload = RandomNumberGenerator.GetBytes(131072 + i);
        var receive = new byte[payload.Length];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var reading = client.GetStream().ReadExactlyAsync(receive, timeout.Token).AsTask();
        for (int pos = 0; pos < payload.Length; pos += 941) await client.GetStream().WriteAsync(payload.AsMemory(pos, Math.Min(941, payload.Length - pos)), timeout.Token);
        client.Client.Shutdown(SocketShutdown.Send); await reading;
        Check(payload.SequenceEqual(receive), "isolated fragmented stream " + (i + 1));
    }));
    await echo;
    Reject(() => { using var conflict = new TcpListener(IPAddress.Loopback, gatewayPort); conflict.Start(); }, "occupied gateway port");
}
listener.Stop();
Check(!await Gateway.CheckGame("127.0.0.1", gatewayPort), "gateway closes after stop");

var temp = Path.Combine(Path.GetTempPath(), "RS2ConnectTests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
var zipPath = Path.Combine(temp, "unsafe.rs2backup");
using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry("../outside.txt").Open()); writer.Write("test"); }
var store = new WorldStore(Path.Combine(temp, "worlds"), temp);
try { await store.Restore(zipPath); throw new Exception("Accepted traversal"); } catch (IOException) { Check(!File.Exists(Path.Combine(temp, "worlds", "outside.txt")), "backup rejects directory traversal"); }

// Optional integration against the clean bundled portable server, never the user's live world.
if (args.Length == 3 && args[0] == "--server") {
    var integration = new WorldStore(Path.Combine(temp, "integration"), Path.GetFullPath(args[2]));
    var created = await integration.Create(Path.GetFullPath(args[1]), "Integration world", true);
    Check(created.EnginePort == 43594 && created.RsaModulus.Length == 256, "fresh world and matching RSA identity");
    await integration.Start(created);
    try {
        Check(await Gateway.CheckGame("127.0.0.1", created.EnginePort), "actual revision-225 server handshake");
        int gameGatewayPort = Gateway.FreeLoopbackPort();
        await using (var gameGateway = new Gateway(IPAddress.Loopback, gameGatewayPort, "127.0.0.1", created.EnginePort)) {
            gameGateway.Start();
            var engine = Path.Combine(integration.Folder(created.Id), "Server225", "engine");
            var bun = Path.Combine(integration.Folder(created.Id), "Server225", "runtime", "bun-windows-x64", "bun.exe");
            var probe = Path.GetFullPath(Path.Combine(args[2], "..", "tests", "game_login.mjs"));
            Console.Write(await WorldStore.Run(bun, engine, "run", probe, engine, gameGatewayPort.ToString()));
        }
        try { await integration.Backup(created, Path.Combine(temp, "live.rs2backup")); throw new Exception("Live backup accepted"); }
        catch (IOException) { Check(true, "running-world backup rejected"); }
    } finally { await integration.Stop(created); }
    var backup = Path.Combine(temp, "world.rs2backup"); await integration.Backup(created, backup);
    var restored = await integration.Restore(backup);
    Check(restored.Id != created.Id && restored.RsaModulus == created.RsaModulus, "backup/restore retains RSA and creates separate world");
    var copied = await integration.Create(integration.Folder(created.Id), "Imported", false);
    Check(copied.RsaModulus == created.RsaModulus && File.Exists(Path.Combine(integration.Folder(created.Id), "connect-world.json")), "import preserves original world");
    Console.WriteLine("Integration fixture path (private, not for release): " + temp);
}
Console.WriteLine("All checks passed. These are software tests, not real-Xbox or internet acceptance tests.");
