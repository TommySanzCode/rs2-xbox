using System.Diagnostics;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace RS2XboxConnect;

public sealed record WorldInfo(string Id, string Name, string RsaExponent, string RsaModulus, bool Members, int EnginePort);

public sealed class WorldStore
{
    public string Root { get; }
    private readonly string scripts;
    public WorldStore(string root, string scripts) { Root = Path.GetFullPath(root); this.scripts = scripts; Directory.CreateDirectory(Root); }
    public IEnumerable<WorldInfo> List() => Directory.EnumerateDirectories(Root)
        .Where(d => File.Exists(Path.Combine(d, "connect-world.json")))
        .Select(d => ProfileFiles.Read<WorldInfo>(Path.Combine(d, "connect-world.json")));
    public string Folder(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid world ID.");
        return Path.Combine(Root, id);
    }
    public static async Task<string> Run(string executable, string workingDirectory, params string[] args)
    {
        var info = new ProcessStartInfo(executable) { WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        var stdout = new ConcurrentQueue<string>(); var stderr = new ConcurrentQueue<string>();
        var outEnded = new TaskCompletionSource(); var errEnded = new TaskCompletionSource();
        process.OutputDataReceived += (_, e) => { if (e.Data == null) outEnded.TrySetResult(); else stdout.Enqueue(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data == null) errEnded.TrySetResult(); else stderr.Enqueue(e.Data); };
        process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
        // PowerShell Start-Process may leave inherited pipe handles in Bun even
        // with Bun's own output redirected. Wait for the helper PID, not pipe EOF.
        while (!process.HasExited) await Task.Delay(100);
        await Task.WhenAny(Task.WhenAll(outEnded.Task, errEnded.Task), Task.Delay(500));
        process.CancelOutputRead(); process.CancelErrorRead();
        var result = string.Join(Environment.NewLine, stdout) + Environment.NewLine;
        var error = string.Join(Environment.NewLine, stderr);
        if (process.ExitCode != 0) throw new IOException("Server helper failed. " + error.Trim());
        return result;
    }
    public static string PowerShell => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
    public async Task AssertStopped(string root, bool lockHeld = false)
    {
        var args = new List<string> { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(scripts, "Assert-Stopped.ps1"), "-Root", root };
        if (lockHeld) args.Add("-LockHeld");
        await Run(PowerShell, root, args.ToArray());
    }
    public async Task<WorldInfo> Create(string source, string name, bool fresh)
    {
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl)) throw new ArgumentException("World name must be 1–80 characters.");
        source = Path.GetFullPath(source);
        if (!File.Exists(Path.Combine(source, "Server225", "engine", "src", "app.ts"))) throw new IOException("Choose the portable server's outer folder containing Server225 and scripts.");
        await AssertStopped(source);
        var id = Guid.NewGuid().ToString("N"); var destination = Folder(id);
        // Hold the launcher's lock while copying so a cooperating launcher cannot start the source mid-backup.
        using (var locked = Lock(source)) { await AssertStopped(source, true); await Task.Run(() => CopyTree(source, destination)); }
        if (fresh) {
            var options = "server_address=127.0.0.1\ngame_port=43594\nmembers=1\nxp_rate=1\naccount_username=\naccount_password=\n";
            File.WriteAllText(Path.Combine(destination, "options.ini"), options);
            await Run(PowerShell, destination, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(destination, "scripts", "Configure-Server.ps1"));
        }
        return await Prepare(destination, id, name);
    }
    private async Task<WorldInfo> Prepare(string destination, string id, string name)
    {
        await AssertStopped(destination);
        using var locked = Lock(destination);
        await AssertStopped(destination, true);
        var engine = Path.Combine(destination, "Server225", "engine");
        var envPath = Path.Combine(engine, ".env");
        if (!File.Exists(envPath)) throw new IOException("The imported server has not been configured. Start and stop it once before importing.");
        var env = ParseEnvironment(File.ReadAllText(envPath));
        if (!env.TryGetValue("ENGINE_REVISION", out var revision) || revision != "225" || env.GetValueOrDefault("DB_BACKEND") != "sqlite")
            throw new InvalidDataException("Import requires the revision-225 portable SQLite server.");
        var tcp = Path.Combine(engine, "src", "server", "tcp", "TcpServer.ts");
        var code = File.ReadAllText(tcp);
        const string original = "this.tcp.listen(Environment.NODE_PORT, '0.0.0.0', () => {});";
        const string updated = "this.tcp.listen(Environment.NODE_PORT, process.env.NODE_BIND_HOST ?? '0.0.0.0', () => {});";
        if (!code.Contains(original) && !code.Contains(updated)) throw new IOException("Server TCP source does not match the supported portable package.");
        File.WriteAllText(tcp, code.Replace(original, updated));
        foreach (var bindName in new[] { "NODE_BIND_HOST", "WEB_HOST", "WEB_MANAGEMENT_HOST", "LOGIN_HOST", "LOGIN_BIND_HOST", "FRIEND_HOST", "FRIEND_BIND_HOST", "LOGGER_HOST", "LOGGER_BIND_HOST" }) env[bindName] = "127.0.0.1";
        env["NODE_MAX_PLAYERS"] = "16"; env["NODE_MAX_CONNECTED"] = "64";
        env["NODE_RATELIMIT_ADDRESS_LOGIN"] = "120"; env["NODE_RATELIMIT_DEVICE_LOGIN"] = "40";
        env["WEBSITE_REGISTRATION"] = "false";
        // Fix service ports within Connect copies. Original source stays untouched.
        foreach (var pair in new Dictionary<string,string> { ["NODE_PORT"]="43594", ["LOGIN_PORT"]="43500", ["FRIEND_PORT"]="45099", ["LOGGER_PORT"]="43501", ["WEB_PORT"]="8888", ["WEB_MANAGEMENT_PORT"]="8898" }) env[pair.Key] = pair.Value;
        ProfileFiles.AtomicWrite(envPath, string.Join('\n', env.Select(p => $"{p.Key}={p.Value}")) + "\n");
        using var rsa = RSA.Create(); rsa.ImportFromPem(File.ReadAllText(Path.Combine(engine, "data", "config", "private.pem")));
        using var pub = RSA.Create(); pub.ImportFromPem(File.ReadAllText(Path.Combine(engine, "data", "config", "public.pem")));
        if (!rsa.ExportSubjectPublicKeyInfo().SequenceEqual(pub.ExportSubjectPublicKeyInfo())) throw new IOException("Imported RSA keys do not match.");
        var key = rsa.ExportParameters(false);
        if (key.Modulus?.Length != 128) throw new IOException("Expected a 1024-bit game-server RSA key.");
        var world = new WorldInfo(id, name, Convert.ToHexString(key.Exponent!).ToLowerInvariant(), Convert.ToHexString(key.Modulus).ToLowerInvariant(), env.GetValueOrDefault("NODE_MEMBERS") == "true", 43594);
        // The GUI stores login settings under DPAPI. Remove setup's plaintext credential copies.
        foreach (var file in new[] { "Xbox-config.ini", Path.Combine("Server225", "runtime", "setup.local.json") }) {
            var path = Path.Combine(destination, file); if (File.Exists(path)) File.Delete(path);
        }
        File.WriteAllText(Path.Combine(destination, "options.ini"), "# Managed by RS2 Xbox Connect. Do not run Configure-Server in this copy.\n");
        ProfileFiles.Write(Path.Combine(destination, "connect-world.json"), world);
        return world;
    }
    private static Dictionary<string, string> ParseEnvironment(string content) => content.Split('\n')
        .Select(x => x.Trim()).Where(x => !x.StartsWith('#') && x.Contains('='))
        .Select(x => x.Split('=', 2)).ToDictionary(x => x[0].Trim(), x => x[1].Trim());
    private static FileStream Lock(string root)
    {
        var runtime = Path.Combine(root, "Server225", "runtime"); Directory.CreateDirectory(runtime);
        return new FileStream(Path.Combine(runtime, "server-control.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public async Task Start(WorldInfo world) => await Run(PowerShell, Folder(world.Id), "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(Folder(world.Id), "scripts", "Start-RS2-Server.ps1"), "-WaitSeconds", "120");
    public async Task Stop(WorldInfo world) => await Run(PowerShell, Folder(world.Id), "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(Folder(world.Id), "scripts", "Stop-RS2-Server.ps1"), "-WaitSeconds", "120");
    public async Task Backup(WorldInfo world, string archive)
    {
        var folder = Folder(world.Id); await AssertStopped(folder);
        using var locked = Lock(folder);
        await AssertStopped(folder, true);
        if (File.Exists(archive)) throw new IOException("Choose a new backup filename; existing backups are never overwritten.");
        await Task.Run(() => {
            using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
            foreach (var file in SafeFiles(folder)) zip.CreateEntryFromFile(file, Path.GetRelativePath(folder, file).Replace('\\', '/'), CompressionLevel.Fastest);
        });
    }
    public async Task<WorldInfo> Restore(string archive)
    {
        var id = Guid.NewGuid().ToString("N"); var destination = Folder(id);
        await Task.Run(() => {
            using var zip = ZipFile.OpenRead(archive);
            if (zip.Entries.Count > 150000 || zip.Entries.Sum(x => x.Length) > 8L * 1024 * 1024 * 1024) throw new IOException("Backup exceeds size limits.");
            foreach (var entry in zip.Entries) {
                var path = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                if (!path.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || entry.FullName.Contains(':'))
                    throw new IOException("Backup contains an unsafe path.");
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(path); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!); entry.ExtractToFile(path);
            }
        });
        var info = ProfileFiles.Read<WorldInfo>(Path.Combine(destination, "connect-world.json"));
        return await Prepare(destination, id, info.Name + " (restored)");
    }
    private static bool Skip(string path) => Path.GetFileName(path) is "server-control.lock" or "server-process.json" || path.EndsWith(".log") || path.EndsWith(".stop-request") || path.EndsWith(".status.json");
    private static IEnumerable<string> SafeFiles(string root)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(root)) {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Server folder contains a link. Use an extracted official portable package.");
            if (Directory.Exists(path)) { foreach (var file in SafeFiles(path)) yield return file; }
            else if (!Skip(path)) yield return path;
        }
    }
    public static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in SafeFiles(source)) {
            var output = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.Copy(file, output, false);
        }
    }
}
