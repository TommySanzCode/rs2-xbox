using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RS2XboxConnect;

public sealed record DirectInvite
{
    [JsonRequired] public int Version { get; init; } = 2;
    [JsonRequired] public int Revision { get; init; } = 225;
    [JsonRequired] public string WorldName { get; init; } = "";
    [JsonRequired] public string WorldId { get; init; } = "";
    [JsonRequired] public string Host { get; init; } = "";
    [JsonRequired] public int Port { get; init; }
    [JsonRequired] public string CertificateSha256 { get; init; } = "";
    [JsonRequired] public string Secret { get; init; } = "";
    [JsonRequired] public string RsaExponent { get; init; } = "";
    [JsonRequired] public string RsaModulus { get; init; } = "";
    [JsonRequired] public bool Members { get; init; }
    public void Validate()
    {
        if (Version != 2 || Revision != 225 || !Guid.TryParseExact(WorldId, "N", out _) ||
            WorldName.Length is < 1 or > 80 || WorldName.Any(char.IsControl) ||
            !IPAddress.TryParse(Host, out var ip) || !NetworkAddresses.PublicIPv4(ip) || Port is < 1024 or > 65535 ||
            !Regex.IsMatch(CertificateSha256, "^[a-f0-9]{64}$") || !Regex.IsMatch(Secret, "^[a-f0-9]{64}$") ||
            !Regex.IsMatch(RsaExponent, "^[a-fA-F0-9]{2,16}$") || !Regex.IsMatch(RsaModulus, "^[a-fA-F0-9]{256}$"))
            throw new InvalidDataException("Invalid direct invitation. Ask the host to copy a fresh invitation after starting their world.");
    }
    public string ToCode()
    {
        Validate();
        return "RS2D2-" + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this, new JsonSerializerOptions(ProfileFiles.Json) { WriteIndented = false })).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
    public static DirectInvite FromCode(string code)
    {
        code = code.Trim();
        if (code.Length > 4096 || !code.StartsWith("RS2D2-", StringComparison.Ordinal)) throw new InvalidDataException("Paste the complete RS2D2 invitation code from your friend.");
        var value = code[6..].Replace('-', '+').Replace('_', '/');
        value = value.PadRight((value.Length + 3) / 4 * 4, '=');
        var invite = JsonSerializer.Deserialize<DirectInvite>(Convert.FromBase64String(value), ProfileFiles.Json) ?? throw new InvalidDataException("Empty invitation.");
        invite.Validate(); return invite;
    }
    // Config export uses only public game identity; no fabricated relay credentials.
    public WorldInfo World => new(WorldId, WorldName, RsaExponent, RsaModulus, Members, 43594);
}

public sealed record DirectIdentity(string Certificate, string Secret)
{
    public static DirectIdentity Create()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=RS2 Xbox Connect", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(2));
        return new(Convert.ToBase64String(certificate.Export(X509ContentType.Pfx)), WorldInvite.NewSecret());
    }
    // Schannel requires a Windows user key container for server TLS. Omitting
    // PersistKeySet lets disposal remove the temporary container; the saved PFX
    // lives only inside the app's DPAPI-protected settings.
    public X509Certificate2 OpenCertificate() => X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(Certificate), null,
        OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet);
    public DirectInvite Invite(WorldInfo world, IPAddress address, int port)
    {
        using var certificate = OpenCertificate();
        return new() { WorldName = world.Name, WorldId = world.Id, Host = address.ToString(), Port = port,
            CertificateSha256 = Convert.ToHexStringLower(SHA256.HashData(certificate.RawData)), Secret = Secret,
            RsaExponent = world.RsaExponent, RsaModulus = world.RsaModulus, Members = world.Members };
    }
}

public static class NetworkAddresses
{
    public static bool PublicIPv4(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        if (b.Length != 4) return false;
        return !(b[0] is 0 or 10 or 127 || b[0] >= 224 || b[0] == 100 && b[1] is >= 64 and <= 127 ||
            b[0] == 169 && b[1] == 254 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168 ||
            b[0] == 192 && b[1] == 0 && b[2] is 0 or 2 || b[0] == 192 && b[1] == 88 && b[2] == 99 ||
            b[0] == 198 && b[1] is 18 or 19 || b[0] == 198 && b[1] == 51 && b[2] == 100 ||
            b[0] == 203 && b[1] == 0 && b[2] == 113);
    }
    public static bool SameSubnet(IPAddress left, IPAddress right, IPAddress mask)
    {
        var a = left.GetAddressBytes(); var b = right.GetAddressBytes(); var m = mask.GetAddressBytes();
        return a.Length == 4 && b.Length == 4 && m.Length == 4 && Enumerable.Range(0, 4).All(i => (a[i] & m[i]) == (b[i] & m[i]));
    }
}
