using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using RS2XboxConnect;

if (args.Length != 2) throw new ArgumentException("Pass a CLEAN server-template folder and the Connect scripts folder. This test creates its own disposable world.");
var root = Path.Combine(Path.GetTempPath(), "RS2DirectGameTest-" + Guid.NewGuid().ToString("N"));
var store = new WorldStore(Path.Combine(root, "worlds"), Path.GetFullPath(args[1]));
Console.WriteLine("Creating isolated world for direct-tunnel/auto-login integration…");
var world = await store.Create(Path.GetFullPath(args[0]), "Direct integration", true);
var identity = DirectIdentity.Create(); using var certificate = identity.OpenCertificate();
var accounts = Enumerable.Range(2,8).ToDictionary(i=>"127.0.0."+i,i=>new XboxCharacter("cd"+Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))+i,Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8))));
await store.Start(world);
try {
    await using var host = DirectTunnel.Host(IPAddress.Loopback, 0, certificate, identity.Secret, world.EnginePort); host.Start();
    var invitation = identity.Invite(world,IPAddress.Loopback,host.Port);
    await using var bridge = new XboxDiscovery(IPAddress.Loopback,new IPAddress(new byte[]{255,0,0,0}),world,
        token=>DirectTunnel.Connect(invitation,token,localTest:true), ip=>accounts.GetValueOrDefault(ip));
    bridge.Start();
    var publicKey = Path.Combine(root,"bridge-public.json");
    File.WriteAllText(publicKey,JsonSerializer.Serialize(new { n=Convert.ToBase64String(Convert.FromHexString(bridge.Modulus)),e=Convert.ToBase64String(Convert.FromHexString(bridge.Exponent)) }));
    var engine = Path.Combine(store.Folder(world.Id),"Server225","engine");
    var bun = Path.Combine(store.Folder(world.Id),"Server225","runtime","bun-windows-x64","bun.exe");
    var probe = Path.GetFullPath(Path.Combine(args[1],"..","tests","game_login.mjs"));
    Console.Write(await WorldStore.Run(bun,engine,"run",probe,engine,XboxDiscovery.LoginPort.ToString(),publicKey));
} finally { await store.Stop(world); }
Console.WriteLine("PASS: real revision-225 login/logout/relogin through Xbox credential bridge and Windows direct TLS tunnel; world saved and stopped.");
Console.WriteLine("Fixture retained privately for save inspection: " + root);
