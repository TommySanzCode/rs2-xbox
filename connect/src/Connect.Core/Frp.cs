using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RS2XboxConnect;

public static class FrpConfig
{
    private static string Q(string value) => JsonSerializer.Serialize(value);
    public static string Create(WorldInvite world, bool host, string caPath, int enginePort, int visitorPort, int adminPort, string adminPassword)
    {
        world.Validate();
        var common = $"""
serverAddr = {Q(world.Relay.Host)}
serverPort = {world.Relay.Port}
user = "rs2"
auth.method = "token"
auth.token = {Q(world.Relay.Token)}
auth.additionalScopes = ["HeartBeats", "NewWorkConns"]
loginFailExit = false
transport.protocol = "tcp"
transport.tls.enable = true
transport.tls.trustedCaFile = {Q(caPath)}
transport.tls.serverName = {Q(world.Relay.Host)}
webServer.addr = "127.0.0.1"
webServer.port = {adminPort}
webServer.user = "connect"
webServer.password = {Q(adminPassword)}
log.to = "console"
log.level = "warn"

""";
        return common + (host ? $"""

[[proxies]]
name = "world-{world.WorldId}"
type = "stcp"
secretKey = {Q(world.Secret)}
localIP = "127.0.0.1"
localPort = {enginePort}
""" : $"""

[[visitors]]
name = "visitor-{world.WorldId}"
type = "stcp"
serverName = "world-{world.WorldId}"
secretKey = {Q(world.Secret)}
bindAddr = "127.0.0.1"
bindPort = {visitorPort}
""");
    }
}

public sealed class FrpProcess : IAsyncDisposable
{
    private Process? process;
    private ChildJob? job;
    private readonly string directory;
    private readonly string executable;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private int adminPort;
    public string Status { get; private set; } = "Stopped";
    public bool Alive => process is { HasExited: false };
    public FrpProcess(string executable, string directory) { this.executable = executable; this.directory = directory; }
    public void Start(WorldInvite world, bool host, int enginePort, int visitorPort)
    {
        if (process != null) throw new InvalidOperationException("Tunnel already started.");
        Directory.CreateDirectory(directory);
        var ca = Path.Combine(directory, "ca.crt");
        var config = Path.Combine(directory, "frpc.toml");
        adminPort = Gateway.FreeLoopbackPort();
        var password = WorldInvite.NewSecret();
        File.WriteAllText(ca, world.Relay.CaPem);
        File.WriteAllText(config, FrpConfig.Create(world, host, ca, enginePort, visitorPort, adminPort, password));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("connect:" + password)));
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        info.ArgumentList.Add("-c"); info.ArgumentList.Add(config);
        process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, _) => { }; // Do not persist upstream logs containing endpoints/secrets.
        process.ErrorDataReceived += (_, _) => { };
        process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
        try { job = ChildJob.Attach(process); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        Status = "Connecting";
    }
    public async Task<bool> CheckHost()
    {
        if (!Alive) { Status = "Tunnel process stopped"; return false; }
        try {
            var text = await http.GetStringAsync($"http://127.0.0.1:{adminPort}/api/status");
            using var json = JsonDocument.Parse(text);
            bool up = json.RootElement.TryGetProperty("stcp", out var proxies) && proxies.EnumerateArray().Any(p => p.GetProperty("status").GetString() == "running");
            Status = up ? "Connected" : "Reconnecting — check relay, certificate and credentials";
            return up;
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException) {
            Status = "Reconnecting — relay unavailable"; return false;
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (process != null) {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            process.Dispose(); process = null;
        }
        http.Dispose();
        job?.Dispose(); job = null;
        // Runtime secrets exist only in this user-private directory while frpc runs.
        foreach (var file in new[] { "frpc.toml", "ca.crt" }) {
            var path = Path.Combine(directory, file); if (File.Exists(path)) File.Delete(path);
        }
        Status = "Stopped";
    }
}
