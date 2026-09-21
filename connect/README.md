# RS2 Xbox Connect — direct connection preview

Host a RuneScape 2 world and join friends using one Windows app. **No rented relay, domain or service account is required.** Connect tries automatic router setup and reports unsupported routers, CGNAT and double NAT. Guests behind CGNAT can join a reachable host; there is no relay fallback when no friend can accept incoming connections.

1. Extract the Windows ZIP and run `INSTALL.cmd`. Approve Windows' prompt for the app's Private-network firewall rules. Portable use is also supported.
2. Install the included Connect-enabled Xbox folder once, or update an existing client while preserving its configuration. A PC installer cannot update your Xbox by itself.
3. Host: choose **Host a world → Automatic direct → Start connection**, then **Copy invitation code** for friends. A first world is created if needed; stopped portable worlds can be imported.
4. Guest: choose **Join a friend**, paste the invitation, and start.
5. Press Start on Xbox, approve its matching code and character inside Connect, then press Start again. Passwords stay on the PC. Additional Xboxes need separate characters.
6. Keep PCs awake. Use **Stop and save** when done.

Read the [complete host/guest guide](../docs/ONLINE-DIRECT.md) and [validation record](../docs/CONNECT-VALIDATION.md). Packaged guides are under `docs/`. Local software tests are not cross-household or real-Xbox acceptance. Original 64 MB/128 MB release assets are preserved.

The host needs a compatible UPnP router with public IPv4. Connect requests an expiring mapping; success does not prove external reachability until a friend connects. If unsupported, LAN play remains available and another friend can try hosting.

Existing clients can still use configuration export, preserving imported graphics/audio/controls. The earlier **Advanced frp relay** mode remains in source for existing setups. The direct package includes no frp executable. The older 0.1 developer package remains incomplete for that advanced mode because its Windows dependency was blocked during packaging; no security settings were changed to bypass the block.

## Build and test

```powershell
dotnet build connect/src/Connect.App -c Release
dotnet run --project connect/tests/Connect.Tests -c Release
dotnet run --project connect/tests/Connect.Direct.Tests -c Release
dotnet run --project connect/tests/Connect.Windows.Tests -c Release
```

`connect/scripts/Build-ConnectXbox.ps1` builds each memory profile in an isolated folder using your nxdk and local game cache. `connect/scripts/Build-Direct.ps1` packages the self-contained app, checksum-verified original clean server, both new Xbox folders, checksums, guides and notices. See script parameters for local input paths. It never downloads or executes frp. Original client, game, server and dependency credits remain applicable.
