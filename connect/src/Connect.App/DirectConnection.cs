using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace RS2XboxConnect;

public partial class MainWindow
{
    private const int TunnelPort = 43595;
    private TcpService? directHost, directGuest;
    private X509Certificate2? directCertificate;
    private DirectInvite? directInvitation;
    private UpnpRouter? router;
    private RouterLease? lease;
    private XboxDiscovery? discovery;
    private readonly ConcurrentDictionary<string, XboxCharacter> xboxCharacters = new();
    private string directStatus = "Stopped";
    private bool mappingHealthy;
    private int connectionEpoch;
    private DateTime nextGuestProbe;
    private bool guestReady;
    private bool AdvancedRelay => Transport.SelectedIndex == 2;
    private WorldInfo DirectWorld() => Hosting ? SelectedWorld : (settings.DirectInvitation ?? throw new InvalidOperationException("Paste your friend's invitation first.")).World;
    private async void UseDirectCode(object sender, RoutedEventArgs e) => await Work(() => {
        settings.DirectInvitation = DirectInvite.FromCode(InvitationCode.Text); InvitationCode.Clear(); Save(); Labels();
        Notice.Text = "Invitation saved privately. Start the connection, then launch your Xbox client."; return Task.CompletedTask;
    });
    private async void CopyDirectCode(object sender, RoutedEventArgs e) => await Work(() => {
        if (directInvitation == null || lease == null || !mappingHealthy) throw new InvalidOperationException("Start a world with a supported router first.");
        Clipboard.SetText(directInvitation.ToCode());
        Notice.Text = "Private invitation copied. It includes your public network address and world access secret. Share only with friends; never post it publicly.";
        return Task.CompletedTask;
    });
    private async Task StartDirect()
    {
        connectionEpoch++;
        nextGuestProbe = DateTime.MinValue;
        Capture();
        if (!Hosting && Transport.SelectedIndex == 1) throw new InvalidOperationException("For same-LAN play, run Connect on the host PC and launch each Xbox on that LAN. Guests do not need another Connect instance.");
        if (!Hosting && !string.IsNullOrWhiteSpace(InvitationCode.Text)) {
            settings.DirectInvitation = DirectInvite.FromCode(InvitationCode.Text); InvitationCode.Clear();
        }
        if (Hosting && Worlds.SelectedItem == null) {
            Notice.Text = "Creating your first world. This may take a few minutes…";
            var created = await store.Create(Path.Combine(assets, "server-template"), WorldName.Text.Trim(), true); ReloadWorlds(created.Id);
        }
        var world = DirectWorld();
        var adapter = Adapters.SelectedItem as Adapter ?? throw new InvalidOperationException("Select the network adapter connected to your Xbox.");
        var address = IPAddress.Parse(adapter.Address); activeAddress = adapter.Address;
        if (settings.Port is TunnelPort or XboxDiscovery.DiscoveryPort or XboxDiscovery.LoginPort) throw new ArgumentException("Game port conflicts with Connect's reserved ports 43595–43597.");
        Save(); Notice.Text = "Starting your world and local Xbox connection…";
        try {
            Func<CancellationToken, Task<Stream>> upstream;
            if (Hosting) {
                runningWorld = world; await store.Start(world);
                upstream = token => DirectTunnel.Loopback(world.EnginePort, token);
                gateway = new Gateway(address, settings.Port, "127.0.0.1", world.EnginePort); gateway.Start();
            } else {
                var invite = settings.DirectInvitation!; invite.Validate();
                upstream = token => DirectTunnel.Connect(invite, token);
                // Test the complete authenticated route before announcing a working gateway.
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                using (var probe = await upstream(timeout.Token)) await probe.ReadExactlyAsync(new byte[8], timeout.Token);
                directGuest = new TcpService(address, settings.Port, async (client, token) => {
                    using var remote = await upstream(token); await DirectTunnel.Bridge(client.GetStream(), remote, token);
                });
                directGuest.Start(); directStatus = "Connected directly to your friend's PC";
            }
            discovery = new XboxDiscovery(address, IPAddress.Parse(adapter.Mask), world, upstream, ip => xboxCharacters.GetValueOrDefault(world.Id + ":" + ip));
            discovery.Start();
            PairUsername.Text = settings.Username; PairPassword.Password = settings.Password;
            if (Hosting && Transport.SelectedIndex == 0) {
                try {
                    if (adapter.Router.Length == 0) throw new RouterUnsupportedException("This adapter has no IPv4 gateway. Choose the Xbox's home-network adapter. LAN play is available.");
                    Notice.Text = "Checking router support and requesting a temporary internet mapping…";
                    router = await UpnpRouter.Discover(address, IPAddress.Parse(adapter.Router), CancellationToken.None);
                    var identity = Identity(world.Id); directCertificate = identity.OpenCertificate();
                    directHost = DirectTunnel.Host(address, TunnelPort, directCertificate, identity.Secret, world.EnginePort); directHost.Start();
                    lease = await router.Map(TunnelPort, CancellationToken.None);
                    directInvitation = identity.Invite(world, lease.Address, lease.ExternalPort); directInvitation.Validate();
                    mappingHealthy = true; Save();
                    directStatus = "Router mapping ready — awaiting a friend's external connection";
                    Notice.Text = "Copy your invitation code for friends. Launch the Connect-enabled Xbox and approve its code here. External reachability is confirmed only when a friend connects.";
                } catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException or OperationCanceledException) {
                    directStatus = "Internet hosting unavailable — LAN is ready";
                    Notice.Text = ex.Message + " Your world remains running for local play.";
                }
            } else if (Hosting) { directStatus = "Same LAN only"; Notice.Text = "LAN world ready. Launch the Connect-enabled Xbox, then approve its code here."; }
            else Notice.Text = "Connected. Launch the Connect-enabled Xbox, approve its matching code, then press Start again.";
            RelayStatus.Text = directStatus; Labels();
        } catch { await StopConnection(); throw; }
    }
    private DirectIdentity Identity(string worldId)
    {
        if (settings.DirectIdentities.TryGetValue(worldId, out var identity)) {
            using var certificate = identity.OpenCertificate();
            if (certificate.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(7)) return identity;
        }
        identity = DirectIdentity.Create(); settings.DirectIdentities[worldId] = identity; return identity;
    }
    private async Task<string> StopDirect()
    {
        connectionEpoch++;
        string note = "";
        if (discovery != null) { await discovery.DisposeAsync(); discovery = null; }
        if (directGuest != null) { await directGuest.DisposeAsync(); directGuest = null; }
        if (directHost != null) { await directHost.DisposeAsync(); directHost = null; }
        if (lease != null) {
            try { await lease.DisposeAsync(); }
            catch { note = " The router did not confirm mapping removal. The requested lease expires within one hour of its last renewal."; }
            lease = null;
        }
        router?.Dispose(); router = null; directCertificate?.Dispose(); directCertificate = null;
        directInvitation = null; mappingHealthy = false; directStatus = "Stopped"; FoundXboxes.ItemsSource = null;
        return note;
    }
    private async Task RotateDirect()
    {
        var world = SelectedWorld;
        if (MessageBox.Show(this, "Replace the invitation and disconnect current remote players? Share the new code with friends afterwards.", "Replace invitation", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        var identity = Identity(world.Id) with { Secret = WorldInvite.NewSecret() }; settings.DirectIdentities[world.Id] = identity;
        if (directHost != null) {
            await directHost.DisposeAsync();
            directHost = DirectTunnel.Host(IPAddress.Parse(activeAddress), TunnelPort, directCertificate!, identity.Secret, world.EnginePort); directHost.Start();
        }
        if (lease != null) directInvitation = identity.Invite(world, lease.Address, lease.ExternalPort);
        Save(); Notice.Text = "Old invitations no longer work. Copy the new invitation for friends.";
    }
    private async void PairXbox(object sender, RoutedEventArgs e) => await Work(() => {
        var found = FoundXboxes.SelectedItem as FoundXbox ?? throw new InvalidOperationException("Press Start on the Connect-enabled Xbox, then select its matching code here.");
        if (DateTime.UtcNow - found.Seen > TimeSpan.FromMinutes(2) || discovery == null) throw new InvalidOperationException("This discovery expired. Press Start on the Xbox again.");
        var name = PairUsername.Text.Trim().ToLowerInvariant(); var password = PairPassword.Password; XboxConfig.ValidateLogin(name, password);
        var key = DirectWorld().Id + ":" + found.Address;
        settings.XboxCharacters[key] = new(name, password); xboxCharacters[key] = settings.XboxCharacters[key]; Save();
        Notice.Text = "Xbox approved for that character. Press Start on the Xbox again. Use a different character for each additional Xbox.";
        return Task.CompletedTask;
    });
    private async void ForgetXboxes(object sender, RoutedEventArgs e) => await Work(async () => {
        settings.XboxCharacters.Clear(); xboxCharacters.Clear(); Save();
        // Stop existing bridge sessions as well as denying new logins.
        if (discovery != null) { await StopConnection(); }
        Notice.Text = "Xbox approvals cleared. Start again and approve each Xbox you want to use.";
    });
    private async void SetupFirewall(object sender, RoutedEventArgs e) => await Work(async () => {
        Capture();
        var script = Path.Combine(assets, "scripts", "Set-ConnectFirewall.ps1");
        if (!File.Exists(script)) throw new IOException("Use the packaged app to install its Private-network firewall rules.");
        using var process = Process.Start(new ProcessStartInfo(WorldStore.PowerShell) {
            UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -Program \"" + Environment.ProcessPath + "\" -GamePort " + settings.Port
        });
        Notice.Text = "Windows will request administrator approval to add only Connect's Private-network rules. Public-network access is not enabled.";
        if (process != null) { await process.WaitForExitAsync(); if (process.ExitCode != 0) throw new IOException("Firewall setup did not complete. Check administrator approval and use the installed executable."); }
        Notice.Text = "Connect's Private-network firewall rules are installed.";
    });
    private async Task CheckDirectStatus()
    {
        var epoch = connectionEpoch;
        bool addressExists = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up).Any(n => n.GetIPProperties().UnicastAddresses.Any(a => a.Address.ToString() == activeAddress));
        if (!addressExists) {
            mappingHealthy = false; directStatus = "PC address changed — stop and restart the connection"; CopyCodeButton.IsEnabled = false;
        }
        if (lease != null && mappingHealthy && DateTime.UtcNow >= lease.RenewAt) {
            try { await lease.Renew(CancellationToken.None); }
            catch { if (epoch != connectionEpoch) return; mappingHealthy = false; directStatus = "Router mapping or public address changed — restart and share a new code"; CopyCodeButton.IsEnabled = false; }
        }
        bool ready;
        if (Hosting) ready = await Gateway.CheckGame("127.0.0.1", runningWorld?.EnginePort ?? 43594);
        else if (DateTime.UtcNow >= nextGuestProbe) {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            try { using var probe = await DirectTunnel.Connect(settings.DirectInvitation!, timeout.Token); await probe.ReadExactlyAsync(new byte[8], timeout.Token); ready = true; }
            catch { ready = false; }
            guestReady = ready; nextGuestProbe = DateTime.UtcNow.AddSeconds(30);
            directStatus = ready ? "Connected directly to the host" : "Host unreachable — waiting; a lost game session needs a fresh login";
        } else ready = guestReady;
        if (epoch != connectionEpoch) return;
        if (Hosting && mappingHealthy && directHost?.AuthenticatedConnections > 0) directStatus = "An invited PC reached this world successfully";
        ServerStatus.Text = ready ? "Ready — game handshake received" : "World unavailable";
        RelayStatus.Text = directStatus;
        XboxStatus.Text = discovery == null ? "Stopped" : $"Discovery ready · {discovery.Connections} Xbox session(s)";
        AddressStatus.Text = addressExists ? "Local connection: " + activeAddress : "Select the updated LAN adapter after stopping.";
        var selection = (FoundXboxes.SelectedItem as FoundXbox)?.Address;
        FoundXboxes.ItemsSource = discovery?.Found;
        FoundXboxes.SelectedItem = discovery?.Found.FirstOrDefault(x => x.Address == selection) ?? discovery?.Found.FirstOrDefault();
    }
}
