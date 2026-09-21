# Play with friends

This native Xbox client connects to a **RuneScape 2 revision-225 server**. Every friend must join the same world to meet, chat, and trade. The 64 MB and 128 MB builds use the same protocol, characters and saves. They do not connect to official OSRS worlds.

## Choose a method

| Method | PCs needed | Internet requirement | Best fit |
| --- | --- | --- | --- |
| [Same LAN](#same-lan) | One server PC | None during play once installed | Friends in the same home |
| [Connect automatic direct](ONLINE-DIRECT.md) | Host PC and one PC per other household | Host with public IPv4 and compatible UPnP router | One app; no rented relay or service account |
| [Connect advanced relay](ONLINE-CONNECT.md) | Host PC and one PC per other household | A trusted operator's Linux relay | Advanced existing setups; Windows frp package remains incomplete |
| [Direct forwarding](ONLINE-ALTERNATIVES.md#direct-port-forwarding) | Host PC only | Public IPv4 and router control | Host comfortable configuring a router |
| [Tailscale](ONLINE-ALTERNATIVES.md#tailscale-with-a-local-xbox-gateway) | Host PC and one PC per household | Tailscale accounts and connection | Existing Tailscale users |
| [Managed TCP tunnel](ONLINE-ALTERNATIVES.md#playitgg-managed-tcp-tunnel) | Host PC only | A service plan supporting custom TCP | Avoid running your own relay |
| [VPS world](ONLINE-ALTERNATIVES.md#hosting-the-whole-world-on-a-vps) | A rented server | VPS administration and ongoing cost | A world available while home PCs are off |

Read [validation status](CONNECT-VALIDATION.md) before choosing. Documented alternatives are not automatically tested configurations. Existing Xbox release files remain available unchanged.

**XLink Kai comparison:** Connect provides a similar PC-assisted setup experience, but it forwards this game's TCP connection. It does not implement Xbox System Link, discover other games, or require packet-capture drivers.

## Same LAN

### Host

1. Download the original portable server ZIP and the appropriate Xbox ZIP from [Releases](https://github.com/TommySanzCode/rs2-xbox/releases). Extract the full folders; do not run programs inside ZIPs.
2. Run `Options.cmd`. Leave `game_port=43594`, choose members/XP settings, and enter a character name and a password used only for this world. `server_address=auto` selects the PC address; set it explicitly if the PC has multiple adapters.
3. Run `Start-Server.cmd`. Wait for the ready message and generated `Xbox-config.ini`.
4. Find the PC's IPv4 address with `ipconfig`. Use the Ethernet/Wi-Fi adapter attached to the Xbox's router. Do not use `127.0.0.1`, the router address, a disconnected adapter, or an internet-facing address for LAN play.
5. Allow the game's TCP port on the Windows **Private** network. In an administrator PowerShell window, this example limits incoming connections to the local subnet:

   ```powershell
   New-NetFirewallRule -DisplayName 'RS2 Xbox LAN' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 43594 -Profile Private -RemoteAddress LocalSubnet
   ```

6. Give each friend a copy of the generated config **with the username and password removed**. It must retain the public `rsa_exponent` and `rsa_modulus`, which match this world's server. Never share `private.pem`.

### Each player

1. Connect the Xbox to the same router, preferably Ethernet. Guest Wi-Fi/client isolation can prevent devices from communicating.
2. Edit the copied config: `socketip` is the host PC address, `portoff=0` for port 43594, and each person sets their own distinct username/password. A new name registers automatically on first login; an existing name requires its existing password.
3. Keep the graphics/audio/controls from your Xbox build. When copying networking settings into an existing 128 MB config, keep `lowmem`, `xbox_video`, `xbox_audio`, and `xbox_music` intact.
4. Back up the Xbox's old config, transfer the new file beside `default.xbe` as **config.ini**, launch the game, and press Start.
5. Meet in the same location. Tutorial Island progression may need completing before everyone can meet in the main world.

### Verify, stop and clean up

From another Windows PC, `Test-NetConnection HOST_PC_ADDRESS -Port 43594` checks TCP reachability. Substitute the actual address without publishing it in a support request. A successful TCP check does not verify matching cache, RSA keys or character credentials.

Stop the server with `Stop-Server.cmd` and wait for its clean shutdown message before moving or backing up the folder. Keep the database, player saves and RSA keys together. Do not copy a running SQLite database as a backup.

Reserve the host PC's address in the router to prevent address changes. Remove the firewall rule when no longer hosting:

```powershell
Remove-NetFirewallRule -DisplayName 'RS2 Xbox LAN'
```

## Common problems

- **Cannot connect:** server stopped, wrong PC adapter, Windows firewall, changed DHCP address, wrong port, or Wi-Fi isolation. Check local reachability first.
- **Login rejected:** wrong character password, already logged in, different revision/cache, or mismatched RSA fields. Use this world's public key and your own character.
- **Everyone gets the same character:** each Xbox must have a different username; do not distribute one configured account to the group.
- **Works locally but not over the internet:** follow a complete internet method below. A private LAN address cannot be used directly by another household.
- **128 MB client freezes/audio fails:** networking does not resolve memory/BIOS/audio issues. See [128 MB notes](XBOX-128.md).

Never post configs, invitations, backups, private keys or raw server logs publicly. Connect's diagnostic export omits them.
