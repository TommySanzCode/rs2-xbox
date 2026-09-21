using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace RS2XboxConnect;

public sealed class RouterUnsupportedException(string message) : IOException(message);
public sealed class UpnpFault(int code) : IOException("Router rejected the request (UPnP " + code + ").") { public int Code { get; } = code; }

public sealed class UpnpRouter : IDisposable
{
    private readonly HttpClient http;
    private readonly Uri endpoint;
    private readonly string service;
    public IPAddress LocalAddress { get; }
    private UpnpRouter(HttpClient http, Uri endpoint, string service, IPAddress local) { this.http = http; this.endpoint = endpoint; this.service = service; LocalAddress = local; }
    public static XDocument ParseXml(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 262144 });
        return XDocument.Load(reader);
    }
    public static Uri RouterUri(Uri baseUri, string path, IPAddress router)
    {
        var uri = new Uri(baseUri, path);
        if (uri.Scheme != "http" || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || !IPAddress.TryParse(uri.Host, out var ip) || !ip.Equals(router))
            throw new InvalidDataException("Router discovery advertised a non-router URL.");
        return uri;
    }
    public static async Task<UpnpRouter> Discover(IPAddress local, IPAddress router, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var udp = new UdpClient(new IPEndPoint(local, 0)); udp.Ttl = 2;
        var group = new IPEndPoint(new IPAddress(new byte[] { 239, 255, 255, 250 }), 1900);
        var query = Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: " + group + "\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n");
        await udp.SendAsync(query, group, timeout.Token);
        // Also reach routers that suppress multicast discovery from Wi-Fi clients.
        await udp.SendAsync(query, new IPEndPoint(router, 1900), timeout.Token);
        var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(3) }) { Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 262144 };
        try {
            while (true) {
                var response = await udp.ReceiveAsync(timeout.Token);
                if (!response.RemoteEndPoint.Address.Equals(router) || response.Buffer.Length > 8192) continue;
                var location = Encoding.ASCII.GetString(response.Buffer).Split('\n').FirstOrDefault(l => l.StartsWith("location:", StringComparison.OrdinalIgnoreCase))?[9..].Trim();
                if (!Uri.TryCreate(location, UriKind.Absolute, out var advertised)) continue;
                var url = RouterUri(advertised, "", router);
                var document = ParseXml(await http.GetStringAsync(url, timeout.Token));
                var baseUrl = document.Descendants().FirstOrDefault(x => x.Name.LocalName == "URLBase")?.Value;
                if (!string.IsNullOrEmpty(baseUrl)) url = RouterUri(url, baseUrl, router);
                foreach (var element in document.Descendants().Where(x => x.Name.LocalName == "service")) {
                    string Field(string name) => element.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value ?? "";
                    var type = Field("serviceType");
                    if (type is "urn:schemas-upnp-org:service:WANIPConnection:1" or "urn:schemas-upnp-org:service:WANIPConnection:2" or "urn:schemas-upnp-org:service:WANPPPConnection:1")
                        return new(http, RouterUri(url, Field("controlURL"), router), type, local);
                }
            }
        } catch (OperationCanceledException) when (!token.IsCancellationRequested) {
            http.Dispose(); throw new RouterUnsupportedException("No compatible UPnP router answered. Automatic internet hosting is unavailable on this network. LAN play still works; another friend can try hosting.");
        } catch { http.Dispose(); throw; }
    }
    // Injectable HTTP transport allows complete mapping lifecycle tests without touching a home router.
    public static UpnpRouter ForTest(HttpMessageHandler handler) => new(new HttpClient(handler), new Uri("http://127.0.0.1/control"), "urn:schemas-upnp-org:service:WANIPConnection:1", IPAddress.Loopback);
    private async Task<XDocument> Call(string action, IReadOnlyDictionary<string, string> values, CancellationToken token)
    {
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        var body = new XDocument(new XElement(soap + "Envelope", new XAttribute(XNamespace.Xmlns + "s", soap),
            new XElement(soap + "Body", new XElement(XName.Get(action, service), values.Select(p => new XElement(p.Key, p.Value))))));
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent(body.ToString(), Encoding.UTF8, "text/xml") };
        request.Headers.Add("SOAPAction", "\"" + service + "#" + action + "\"");
        using var response = await http.SendAsync(request, token);
        var document = ParseXml(await response.Content.ReadAsStringAsync(token));
        var fault = document.Descendants().FirstOrDefault(x => x.Name.LocalName == "errorCode")?.Value;
        if (int.TryParse(fault, out var code)) throw new UpnpFault(code);
        response.EnsureSuccessStatusCode(); return document;
    }
    private static string Field(XDocument document, string name) => document.Descendants().FirstOrDefault(x => x.Name.LocalName == name)?.Value ?? "";
    private static Dictionary<string, string> Lookup(int port) => new() { ["NewRemoteHost"] = "", ["NewExternalPort"] = port.ToString(), ["NewProtocol"] = "TCP" };
    public async Task<IPAddress> ExternalAddress(CancellationToken token)
    {
        var response = await Call("GetExternalIPAddress", new Dictionary<string, string>(), token);
        if (!IPAddress.TryParse(Field(response, "NewExternalIPAddress"), out var ip) || !NetworkAddresses.PublicIPv4(ip))
            throw new RouterUnsupportedException("Your router reports a non-public address (often CGNAT or double NAT). Automatic direct hosting cannot work through this upstream NAT. Another friend with a public IPv4 connection can host; LAN play still works.");
        return ip;
    }
    public async Task<RouterLease> Map(int localPort, CancellationToken token)
    {
        var address = await ExternalAddress(token);
        for (int attempt = 0; attempt < 5; attempt++) {
            int external = RandomNumberGenerator.GetInt32(49152, 65536);
            try { await Call("GetSpecificPortMappingEntry", Lookup(external), token); continue; }
            catch (UpnpFault ex) when (ex.Code == 714) { }
            var lease = new RouterLease(this, address, external, localPort, "RS2Connect-" + Guid.NewGuid().ToString("N"));
            try { await Add(lease, token); return lease; }
            catch (UpnpFault ex) when (ex.Code == 718) { }
            catch (UpnpFault ex) when (ex.Code == 725) { throw new RouterUnsupportedException("This router only permits permanent mappings. Connect requires expiring mappings so an app crash cannot leave a permanent router rule. Automatic hosting is unsupported on this router."); }
        }
        throw new RouterUnsupportedException("The router could not allocate a free game tunnel port. Existing mappings were preserved.");
    }
    internal async Task Add(RouterLease lease, CancellationToken token)
    {
        var values = Lookup(lease.ExternalPort);
        values["NewInternalPort"] = lease.LocalPort.ToString(); values["NewInternalClient"] = LocalAddress.ToString(); values["NewEnabled"] = "1";
        values["NewPortMappingDescription"] = lease.Description; values["NewLeaseDuration"] = "3600";
        await Call("AddPortMapping", values, token);
        var confirmed = await Call("GetSpecificPortMappingEntry", Lookup(lease.ExternalPort), token);
        if (Field(confirmed, "NewInternalClient") != LocalAddress.ToString() || Field(confirmed, "NewInternalPort") != lease.LocalPort.ToString() || Field(confirmed, "NewPortMappingDescription") != lease.Description)
            throw new RouterUnsupportedException("The router did not confirm this app's mapping. Internet hosting is unavailable.");
        if (!int.TryParse(Field(confirmed, "NewLeaseDuration"), out var seconds) || seconds is < 1 or > 3600) {
            await Delete(lease, token);
            throw new RouterUnsupportedException("The router did not honor an expiring mapping. The rule was removed; automatic hosting is unsupported on this router.");
        }
    }
    internal async Task<bool> Owns(RouterLease lease, CancellationToken token)
    {
        try {
            var response = await Call("GetSpecificPortMappingEntry", Lookup(lease.ExternalPort), token);
            return Field(response, "NewInternalClient") == LocalAddress.ToString() && Field(response, "NewInternalPort") == lease.LocalPort.ToString() && Field(response, "NewPortMappingDescription") == lease.Description;
        } catch (UpnpFault ex) when (ex.Code == 714) { return false; }
    }
    internal Task Delete(RouterLease lease, CancellationToken token) => Call("DeletePortMapping", Lookup(lease.ExternalPort), token);
    public void Dispose() => http.Dispose();
}

public sealed class RouterLease(UpnpRouter router, IPAddress address, int externalPort, int localPort, string description) : IAsyncDisposable
{
    public IPAddress Address { get; } = address;
    public int ExternalPort { get; } = externalPort;
    public int LocalPort { get; } = localPort;
    public string Description { get; } = description;
    public DateTime RenewAt { get; private set; } = DateTime.UtcNow.AddMinutes(20);
    public async Task Renew(CancellationToken token)
    {
        if (!await router.Owns(this, token)) throw new RouterUnsupportedException("The router mapping was lost or changed. Stop and restart hosting, then share the new invitation.");
        if (!(await router.ExternalAddress(token)).Equals(Address)) throw new RouterUnsupportedException("The public address changed. Stop and restart hosting, then share a new invitation.");
        await router.Add(this, token); RenewAt = DateTime.UtcNow.AddMinutes(20);
    }
    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        // Never remove another program's mapping, including a port reassigned after a router restart.
        if (await router.Owns(this, timeout.Token)) await router.Delete(this, timeout.Token);
    }
}
