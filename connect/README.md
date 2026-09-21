# RS2 Xbox Connect — private-world preview

**Current distribution:** the developer Windows artifact omits `frpc.exe` because Windows security blocked the official download on the build machine. It cannot establish online tunnels. The source and Linux relay are supplied for development/testing; use the existing LAN package or documented alternatives for play today. No security exclusions or bypasses are recommended. The complete Windows bundle remains pending.

Connect is a Windows companion for the native revision-225 Xbox client. It combines the existing portable server, a local TCP gateway, and frp private forwarding. Use a PC at each household and a public relay operated by someone you trust.

**Requirements:** Windows 10/11 x64 capable of running the bundled .NET 10 and Bun runtimes; an SSE4.2-capable CPU; an original Xbox with the existing game folder; and a Linux x64 relay with Docker Compose. The 64 MB and 128 MB clients use the same world. Original Xbox graphics/audio limitations are unchanged.

1. Extract the **complete** Windows ZIP into a writable folder. Run `RS2XboxConnect.exe`; no separate .NET, Bun or frp installation is needed.
2. The relay administrator follows [relay setup](../docs/ONLINE-RELAY.md). On GitHub start at [Play with friends](https://github.com/TommySanzCode/rs2-xbox/blob/main/docs/ONLINE.md). Packaged copies include these guides in `docs/`.
3. Host: create a world, import the private relay profile, select the LAN adapter, and start. Export a world invitation and share it privately with friends.
4. Friend: choose Join, import the invitation, select the LAN adapter, and start.
5. Each player enters their own character name/password and exports `config.ini`. Transfer it beside `default.xbe` using an existing FTP program. Launch the Xbox game and press Start.
6. Keep the host, relay, and each household's PC running. Stop and save before backups or updates.

New names register on first login. Reusing a name requires that world's existing password. Characters belong to the host's world; they are not official RuneScape accounts. Use a unique password. In this legacy server, passwords are case-insensitive.

Windows may need a Private-network firewall rule for TCP 43594. The app does not disable the firewall or automatically forward router ports. Detailed commands and troubleshooting are in the online guide.

Settings live in `%LOCALAPPDATA%\RS2XboxConnect`, restricted to the current Windows user and SYSTEM. Saved UI settings are protected with Windows DPAPI. Local server keys and databases necessarily remain available to the server in that private directory. Exported invitations, INI files and backups are private files: do not post them in issues.

**Preview validation:** see [the validation record](../docs/CONNECT-VALIDATION.md). Local automated checks do not establish cross-household or real-Xbox acceptance. This app is not a System Link tunnel and does not add online support to other Xbox games.

## Source build

Install a .NET 10 SDK and run from the repository root:

```powershell
dotnet build connect/src/Connect.App/Connect.App.csproj -c Release
dotnet run --project connect/tests/Connect.Tests -c Release
.\connect\scripts\Build-Connect.ps1 -OutputDirectory C:\Builds\RS2ConnectPreview
```

The packaging script downloads checksum-pinned frp and the original clean server release, and publishes a self-contained Windows app. Use a new output directory for each release. The server package remains separate from the Git source tree because it includes historical assets and dependencies with their own terms. Retain all notices.

`-AppOnly` creates the explicitly named **Developer-Windows** archive without attempting to download frp; it is useful for UI/config/server development, not online play. Do not rename it to imply a complete bundle.
