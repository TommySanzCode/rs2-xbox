using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace RS2XboxConnect;

public partial class MainWindow : Window
{
    private readonly string assets = AppContext.BaseDirectory;
    private readonly string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RS2XboxConnect");
    private readonly WorldStore store;
    private Settings settings = new();
    private Gateway? gateway;
    private FrpProcess? tunnel;
    private WorldInfo? runningWorld;
    private int visitorPort;
    private bool busy, closing, checking, initialized;
    private string activeAddress = "";
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly System.Windows.Forms.NotifyIcon tray = new() { Text = "RS2 Xbox Connect", Icon = System.Drawing.SystemIcons.Application };
    private string SettingsPath => Path.Combine(data, "settings.protected");
    private bool Active => gateway != null || tunnel != null || runningWorld != null;
    private bool Hosting => Mode.SelectedIndex == 0;
    private WorldInfo SelectedWorld => Worlds.SelectedItem as WorldInfo ?? throw new InvalidOperationException("Create or select a world first.");
    private sealed record Adapter(string Address, string Label);

    public MainWindow()
    {
        InitializeComponent();
        PrivateSettings.RestrictDirectory(data);
        foreach (var name in new[] { "frpc.toml", "ca.crt" }) {
            var stale = Path.Combine(data, "tunnel", name); if (File.Exists(stale)) File.Delete(stale);
        }
        store = new WorldStore(Path.Combine(data, "worlds"), Path.Combine(assets, "scripts"));
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); tray.Visible = false; });
        timer.Tick += async (_, _) => await CheckStatus();
    }
    private void LoadedWindow(object sender, RoutedEventArgs e)
    {
        try {
            settings = PrivateSettings.Load(SettingsPath);
            Mode.SelectedIndex = settings.Mode == "join" ? 1 : 0;
            Username.Text = settings.Username; Password.Password = settings.Password; Port.Text = settings.Port.ToString(); Preset.SelectedIndex = Math.Clamp(settings.Preset, 0, 2);
            ReloadWorlds(); LoadAdapters(); Labels(); initialized = true; Controls(); timer.Start();
            ReloadJoined();
            Notice.Text = "Ready. Preview: cross-household and real-Xbox acceptance tests are still pending.";
        } catch (Exception ex) { Error(ex); }
    }
    private void Labels()
    {
        RelayLabel.Text = settings.Relay == null ? "No relay profile selected." : $"Relay profile imported (port {settings.Relay.Port}).";
        InviteLabel.Text = settings.Invitation == null ? "No invitation selected." : "World: " + settings.Invitation.WorldName;
        TemplateLabel.Text = settings.Template.Length == 0 ? "Using the selected preset." : "Existing config loaded; display, audio and controls will be preserved.";
        HostPanel.Visibility = Hosting ? Visibility.Visible : Visibility.Collapsed;
        JoinPanel.Visibility = Hosting ? Visibility.Collapsed : Visibility.Visible;
    }
    private void ReloadWorlds(string? select = null)
    {
        var items = store.List().OrderBy(x => x.Name).ToList(); Worlds.ItemsSource = items;
        Worlds.SelectedItem = items.FirstOrDefault(x => x.Id == (select ?? settings.WorldId)) ?? items.FirstOrDefault();
    }
    private void ReloadJoined()
    {
        JoinedWorlds.ItemsSource = settings.JoinedInvitations.Values.OrderBy(x => x.WorldName).ToList();
        JoinedWorlds.SelectedItem = settings.JoinedInvitations.Values.FirstOrDefault(x => x.WorldId == settings.Invitation?.WorldId);
    }
    private void JoinedWorldChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initialized && JoinedWorlds.SelectedItem is WorldInvite invite) { settings.Invitation = invite; Labels(); }
    }
    private void LoadAdapters()
    {
        var selected = (Adapters.SelectedItem as Adapter)?.Address ?? settings.Address;
        var items = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254."))
                .Select(a => new Adapter(a.Address.ToString(), n.Name + " — " + a.Address))).ToList();
        Adapters.ItemsSource = items;
        Adapters.SelectedItem = items.FirstOrDefault(a => a.Address == selected) ?? (items.Count == 1 ? items[0] : null);
        if (selected.Length > 0 && !items.Any(a => a.Address == selected)) Notice.Text = "The saved PC address has changed. Select the correct adapter and export a new Xbox config.";
    }
    private void Capture()
    {
        settings.Mode = Hosting ? "host" : "join"; settings.WorldId = (Worlds.SelectedItem as WorldInfo)?.Id ?? "";
        settings.Address = (Adapters.SelectedItem as Adapter)?.Address ?? settings.Address;
        settings.Username = Username.Text.Trim().ToLowerInvariant(); settings.Password = Password.Password;
        settings.Preset = Preset.SelectedIndex;
        if (!int.TryParse(Port.Text, out var port) || port is < 1024 or > 65535) throw new ArgumentException("Local port must be between 1024 and 65535.");
        settings.Port = port;
    }
    private void Save() { Capture(); PrivateSettings.Save(SettingsPath, settings); }
    private void Controls()
    {
        SetupPanel.IsEnabled = !busy && !Active; StartButton.IsEnabled = !busy && !Active;
        StopButton.IsEnabled = !busy && (Active || (Hosting && Worlds.SelectedItem is WorldInfo)); ConfigButton.IsEnabled = !busy;
        BackupButton.IsEnabled = !busy && !Active && Hosting; RestoreButton.IsEnabled = !busy && !Active;
        RotateButton.IsEnabled = !busy && Hosting;
    }
    private async Task Work(Func<Task> action)
    {
        if (busy) return; busy = true; Controls();
        try { await action(); }
        catch (Exception ex) { Error(ex); }
        finally { busy = false; Controls(); }
    }
    private void Error(Exception ex)
    {
        Notice.Text = "Action failed. See the message for the next step.";
        MessageBox.Show(this, ex.Message, "RS2 Xbox Connect", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private static string? Open(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
    private static string? Output(string title, string filter, string filename)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = filename };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
    private void ModeChanged(object sender, SelectionChangedEventArgs e) { if (initialized) { Labels(); Controls(); } }
    private void WorldChanged(object sender, SelectionChangedEventArgs e) { if (initialized) { Labels(); Controls(); } }
    private void RefreshAdapters(object sender, RoutedEventArgs e) => LoadAdapters();
    private async void CreateWorld(object sender, RoutedEventArgs e) => await Work(async () => {
        Notice.Text = "Creating a private world copy. This can take a few minutes…";
        var world = await store.Create(Path.Combine(assets, "server-template"), WorldName.Text.Trim(), true);
        ReloadWorlds(world.Id); Save(); Notice.Text = "World created. Import a relay profile, then start the connection.";
    });
    private async void ImportWorld(object sender, RoutedEventArgs e) => await Work(async () => {
        var dialog = new OpenFolderDialog { Title = "Select the stopped portable server folder containing Server225 and scripts" };
        if (dialog.ShowDialog() != true) return;
        Notice.Text = "Copying stopped server; the original will remain unchanged…";
        var world = await store.Create(dialog.FolderName, WorldName.Text.Trim(), false);
        ReloadWorlds(world.Id); Save(); Notice.Text = "World imported with its existing characters, saves and keys.";
    });
    private async void ImportRelay(object sender, RoutedEventArgs e) => await Work(() => {
        var path = Open("Import private relay profile", "Relay profiles|*.rs2relay"); if (path == null) return Task.CompletedTask;
        var relay = ProfileFiles.Read<RelayProfile>(path); relay.Validate(); settings.Relay = relay; settings.HostInvitation = null;
        Save(); Labels(); Notice.Text = "Relay imported. Previously exported invitations need replacing if the relay changed."; return Task.CompletedTask;
    });
    private WorldInvite CurrentInvite()
    {
        if (!Hosting) { var invite = settings.Invitation ?? throw new InvalidOperationException("Import your friend's invitation first."); invite.Validate(); return invite; }
        var world = SelectedWorld; var relay = settings.Relay ?? throw new InvalidOperationException("Import a relay profile first.");
        settings.HostInvitation = settings.HostedInvitations.GetValueOrDefault(world.Id);
        if (settings.HostInvitation == null || settings.HostInvitation.WorldId != world.Id || settings.HostInvitation.Relay != relay)
            settings.HostInvitation = new WorldInvite { WorldName = world.Name, WorldId = world.Id, Relay = relay, RsaExponent = world.RsaExponent, RsaModulus = world.RsaModulus, Members = world.Members };
        settings.HostInvitation.Validate(); settings.HostedInvitations[world.Id] = settings.HostInvitation; return settings.HostInvitation;
    }
    private async void ExportInvitation(object sender, RoutedEventArgs e) => await Work(() => {
        var invite = CurrentInvite(); Save(); var path = Output("Share privately with friends", "World invitations|*.rs2invite", "my-world.rs2invite");
        if (path != null) { ProfileFiles.Write(path, invite); Notice.Text = "Invitation exported. Share privately; it grants group access."; }
        return Task.CompletedTask;
    });
    private async void ImportInvitation(object sender, RoutedEventArgs e) => await Work(() => {
        var path = Open("Import friend's private invitation", "World invitations|*.rs2invite"); if (path == null) return Task.CompletedTask;
        var invite = ProfileFiles.Read<WorldInvite>(path); invite.Validate(); settings.Invitation = invite; settings.JoinedInvitations[invite.WorldId] = invite; ReloadJoined(); Mode.SelectedIndex = 1;
        Save(); Labels(); Notice.Text = "Invitation imported. Select your LAN adapter and start the connection."; return Task.CompletedTask;
    });
    private async void ImportTemplate(object sender, RoutedEventArgs e) => await Work(() => {
        var path = Open("Choose existing Xbox config to preserve", "Xbox config|*.ini");
        if (path != null) { if (new FileInfo(path).Length > 65536) throw new IOException("Config is too large."); settings.Template = File.ReadAllText(path); Save(); Labels(); }
        return Task.CompletedTask;
    });
    private void ClearTemplate(object sender, RoutedEventArgs e) { settings.Template = ""; Labels(); }
    private async void ExportConfig(object sender, RoutedEventArgs e) => await Work(() => {
        Capture(); var invite = CurrentInvite();
        var address = (Adapters.SelectedItem as Adapter)?.Address ?? throw new InvalidOperationException("Select the adapter on the Xbox's LAN.");
        var template = settings.Template.Length > 0 ? settings.Template : File.ReadAllText(Path.Combine(assets, "templates", settings.Preset == 0 ? "xbox-config.example.ini" : "xbox-128-config.example.ini"));
        if (settings.Template.Length == 0 && settings.Preset == 2) template = template.Replace("xbox_video = 480", "xbox_video = 720");
        var config = XboxConfig.Export(template, invite, address, settings.Port, settings.Username, settings.Password);
        var path = Output("Export private Xbox config", "INI file|*.ini", "config.ini");
        if (path != null) { if (File.Exists(path)) File.Copy(path, path + ".backup-" + DateTime.Now.ToString("yyyyMMddHHmmss"), false); ProfileFiles.AtomicWrite(path, config); Save(); Notice.Text = "Config exported. Transfer beside default.xbe, then press Start in the game."; }
        return Task.CompletedTask;
    });
    private async void StartClick(object sender, RoutedEventArgs e) => await Work(async () => {
        if (!File.Exists(Path.Combine(assets, "frp", "frpc.exe"))) throw new IOException("This developer build does not contain the Windows frp client, so online tunnels are unavailable. See FRP-NOT-BUNDLED.txt and the release validation notes. The complete bundle remains blocked; do not disable security protection.");
        Capture(); var invite = CurrentInvite();
        var address = (Adapters.SelectedItem as Adapter)?.Address ?? throw new InvalidOperationException("Select a LAN adapter first.");
        Save(); Notice.Text = "Starting world and connection…";
        try {
            if (Hosting) { runningWorld = SelectedWorld; await store.Start(runningWorld); }
            visitorPort = Gateway.FreeLoopbackPort();
            tunnel = new FrpProcess(Path.Combine(assets, "frp", "frpc.exe"), Path.Combine(data, "tunnel"));
            tunnel.Start(invite, Hosting, runningWorld?.EnginePort ?? 43594, visitorPort);
            gateway = new Gateway(IPAddress.Parse(address), settings.Port, "127.0.0.1", Hosting ? runningWorld!.EnginePort : visitorPort);
            gateway.Start(); activeAddress = address;
            Notice.Text = "Connection started. Allow the game port on your Windows Private network; see Help. Keep the PC awake.";
        } catch { await StopConnection(); throw; }
    });
    private async Task StopConnection()
    {
        if (gateway != null) { await gateway.DisposeAsync(); gateway = null; }
        if (tunnel != null) { await tunnel.DisposeAsync(); tunnel = null; }
        if (runningWorld != null) { await store.Stop(runningWorld); runningWorld = null; }
        ServerStatus.Text = RelayStatus.Text = XboxStatus.Text = "Stopped";
        Notice.Text = "Stopped. The host's world has completed its normal save/shutdown sequence.";
    }
    private async void StopClick(object sender, RoutedEventArgs e) => await Work(async () => {
        if (runningWorld == null && Hosting && Worlds.SelectedItem is WorldInfo selected) await store.Stop(selected);
        await StopConnection();
    });
    private async void RotateInvitation(object sender, RoutedEventArgs e) => await Work(async () => {
        var invite = CurrentInvite();
        if (MessageBox.Show(this, "Replace the world access secret? Connected friends will be disconnected and need a new invitation.", "Replace invitation", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        settings.HostInvitation = invite with { Secret = WorldInvite.NewSecret() }; settings.HostedInvitations[invite.WorldId] = settings.HostInvitation; Save();
        if (tunnel != null) {
            await tunnel.DisposeAsync(); tunnel = new FrpProcess(Path.Combine(assets, "frp", "frpc.exe"), Path.Combine(data, "tunnel"));
            tunnel.Start(settings.HostInvitation, true, runningWorld!.EnginePort, visitorPort);
        }
        Notice.Text = "World secret replaced. Export and share the new invitation. Relay administrator credentials have not changed.";
    });
    private async void BackupWorld(object sender, RoutedEventArgs e) => await Work(async () => {
        var world = SelectedWorld; var path = Output("Private backup: includes characters and server keys", "World backup|*.rs2backup", "world-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".rs2backup");
        if (path == null) return; Notice.Text = "Backing up stopped world…"; await store.Backup(world, path); Notice.Text = "Backup complete. Store privately; it contains server keys and player data.";
    });
    private async void RestoreWorld(object sender, RoutedEventArgs e) => await Work(async () => {
        var path = Open("Restore your own trusted world backup into a new world", "World backup|*.rs2backup"); if (path == null) return;
        Notice.Text = "Restoring a new world copy…"; var world = await store.Restore(path); Mode.SelectedIndex = 0; ReloadWorlds(world.Id); Save(); Notice.Text = "Backup restored as a separate world. Export a new invitation.";
    });
    private async void SaveClick(object sender, RoutedEventArgs e) => await Work(() => { Save(); Notice.Text = "Settings saved for this Windows account."; return Task.CompletedTask; });
    private async void ExportDiagnostics(object sender, RoutedEventArgs e) => await Work(() => {
        var path = Output("Export redacted diagnostics", "Text file|*.txt", "Connect-diagnostics.txt");
        if (path != null) File.WriteAllText(path, "RS2 Xbox Connect 0.1.0 preview\n" +
            $"Mode: {(Hosting ? "host" : "join")}\nServer active: {runningWorld != null}\nTunnel process active: {tunnel?.Alive == true}\nGateway running: {gateway?.Running == true}\nActive sockets: {gateway?.ActiveConnections ?? 0}\n" +
            "Addresses, paths, character names, credentials, certificates, invitations and server logs are intentionally excluded.\n");
        return Task.CompletedTask;
    });
    private void HelpClick(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://github.com/TommySanzCode/rs2-xbox/blob/main/docs/ONLINE.md") { UseShellExecute = true });
    private async Task CheckStatus()
    {
        if (busy || checking || !Active) return; checking = true;
        try {
            bool addressExists = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up).Any(n => n.GetIPProperties().UnicastAddresses.Any(a => a.Address.ToString() == activeAddress));
            AddressStatus.Text = addressExists ? "Xbox endpoint: " + activeAddress + ":" + settings.Port : "PC address changed. Stop, refresh adapters, and export a new Xbox config.";
            var engine = await Gateway.CheckGame("127.0.0.1", runningWorld?.EnginePort ?? visitorPort);
            ServerStatus.Text = engine ? "Ready — game handshake received" : "Unavailable — check host and tunnel";
            bool relay = Hosting ? tunnel != null && await tunnel.CheckHost() : engine;
            RelayStatus.Text = relay ? "Connected" : tunnel?.Alive == true ? "Reconnecting — verify relay, profile and host" : "Tunnel process stopped — stop and restart the connection";
            XboxStatus.Text = gateway?.Running == true ? $"Listening · {gateway.ActiveConnections} socket(s) connected" : "Stopped";
        } catch { Notice.Text = "Status check failed. Stop and restart the connection; export diagnostics if it persists."; }
        finally { checking = false; }
    }
    private async void ClosingWindow(object? sender, CancelEventArgs e)
    {
        if (closing) return;
        if (busy) { e.Cancel = true; return; }
        try { Save(); } catch (Exception ex) { Error(ex); e.Cancel = true; return; }
        if (Active) {
            e.Cancel = true;
            var choice = MessageBox.Show(this, "Yes: stop and save, then exit.\nNo: keep running in the notification area.\nCancel: return to Connect.", "Connection is active", MessageBoxButton.YesNoCancel);
            if (choice == MessageBoxResult.No) { tray.Visible = true; Hide(); return; }
            if (choice != MessageBoxResult.Yes) return;
            await Work(StopConnection); if (Active) return;
            closing = true; tray.Dispose(); timer.Stop(); Close();
        } else { closing = true; tray.Dispose(); timer.Stop(); }
    }
}
