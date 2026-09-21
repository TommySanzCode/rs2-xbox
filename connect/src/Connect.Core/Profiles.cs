using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RS2XboxConnect;

public sealed record RelayProfile
{
    [JsonRequired] public int Version { get; init; } = 1;
    [JsonRequired] public string Host { get; init; } = "";
    [JsonRequired] public int Port { get; init; } = 7000;
    [JsonRequired] public string Token { get; init; } = "";
    [JsonRequired] public string CaPem { get; init; } = "";
    public void Validate()
    {
        if (Version != 1 || Uri.CheckHostName(Host) == UriHostNameType.Unknown || Host.Length > 253 || Host.Contains(':'))
            throw new InvalidDataException("Relay profile needs version 1 and an IPv4 address or DNS hostname.");
        if (Port is < 1 or > 65535 || !Regex.IsMatch(Token, "^[a-zA-Z0-9_-]{32,128}$"))
            throw new InvalidDataException("Relay port or authentication token is invalid.");
        if (CaPem.Length > 16384 || CaPem.Contains("PRIVATE KEY", StringComparison.Ordinal))
            throw new InvalidDataException("Relay trust must contain a public CA certificate only.");
        using var cert = X509Certificate2.CreateFromPem(CaPem);
        if (!cert.Extensions.OfType<X509BasicConstraintsExtension>().Any(x => x.CertificateAuthority))
            throw new InvalidDataException("Relay trust certificate is not a CA.");
        if (cert.NotAfter.ToUniversalTime() <= DateTime.UtcNow || cert.NotBefore.ToUniversalTime() > DateTime.UtcNow)
            throw new InvalidDataException("Relay CA is expired or not valid yet. Check the clock or ask the relay administrator.");
    }
}

public sealed record WorldInvite
{
    [JsonRequired] public int Version { get; init; } = 1;
    [JsonRequired] public int Revision { get; init; } = 225;
    [JsonRequired] public string WorldName { get; init; } = "My world";
    [JsonRequired] public string WorldId { get; init; } = Guid.NewGuid().ToString("N");
    [JsonRequired] public RelayProfile Relay { get; init; } = new();
    [JsonRequired] public string Secret { get; init; } = NewSecret();
    [JsonRequired] public string RsaExponent { get; init; } = "";
    [JsonRequired] public string RsaModulus { get; init; } = "";
    [JsonRequired] public bool Members { get; init; } = true;
    public static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public void Validate()
    {
        Relay.Validate();
        if (Version != 1 || Revision != 225 || !Regex.IsMatch(WorldId, "^[a-f0-9]{32}$") ||
            !Regex.IsMatch(Secret, "^[a-f0-9]{64}$") || WorldName.Length is < 1 or > 80 || WorldName.Any(char.IsControl))
            throw new InvalidDataException("Unsupported or malformed world invitation.");
        if (!Regex.IsMatch(RsaExponent, "^[a-fA-F0-9]{2,16}$") || !Regex.IsMatch(RsaModulus, "^[a-fA-F0-9]{256}$"))
            throw new InvalidDataException("Invitation must contain the revision-225 world's 1024-bit public RSA key.");
    }
}

public static class ProfileFiles
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static T Read<T>(string path)
    {
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Profile is too large.");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Empty profile.");
    }
    public static void Write<T>(string path, T value) => AtomicWrite(path, JsonSerializer.Serialize(value, Json));
    public static void AtomicWrite(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(temp, text, new System.Text.UTF8Encoding(false)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public static class XboxConfig
{
    public static void ValidateLogin(string name, string password)
    {
        if (!Regex.IsMatch(name, "^[a-z0-9]{1,12}$")) throw new ArgumentException("Character name: 1–12 lowercase letters or digits.");
        if (!Regex.IsMatch(password, "^[a-zA-Z0-9 _-]{1,20}$") || password.Trim() != password)
            throw new ArgumentException("Password: 1–20 letters, digits, spaces, underscores or hyphens; no leading/trailing spaces. Use a password unique to this private world.");
    }
    public static string Export(string template, WorldInvite world, string address, int port, string username, string password)
    {
        ValidateLogin(username, password);
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || ip.Equals(IPAddress.Any) || IPAddress.IsLoopback(ip))
            throw new ArgumentException("Choose the PC's LAN IPv4 address, not loopback.");
        if (port is < 1024 or > 65535) throw new ArgumentException("Gateway port must be 1024–65535.");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["socketip"] = address, ["portoff"] = (port - 43594).ToString(), ["nodeid"] = "1",
            ["username"] = username, ["password"] = password, ["remember_username"] = "1", ["remember_password"] = "1",
            ["rsa_exponent"] = world.RsaExponent, ["rsa_modulus"] = world.RsaModulus, ["members"] = world.Members ? "0" : "1"
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool unnamed = true;
        var lines = template.TrimStart('\uFEFF').Replace("\r", "").Split('\n').Select(line => {
            if (line.TrimStart().StartsWith('[')) unnamed = false;
            if (!unnamed) return line;
            var match = Regex.Match(line, @"^\s*([^#;=\s]+)\s*=");
            if (!match.Success || !values.TryGetValue(match.Groups[1].Value, out var value)) return line;
            var key = match.Groups[1].Value;
            return seen.Add(key) ? $"{key} = {value}" : "# Duplicate connection setting removed by Connect.";
        }).ToList();
        var insertion = lines.FindIndex(x => x.TrimStart().StartsWith('['));
        if (insertion < 0) insertion = lines.Count;
        lines.InsertRange(insertion, values.Where(p => !seen.Contains(p.Key)).Select(p => $"{p.Key} = {p.Value}"));
        return "# PRIVATE: contains character credentials. Never publish this file.\n" + string.Join('\n', lines) + "\n";
    }
}
