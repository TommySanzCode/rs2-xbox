# Connect without a rented relay or service account

The direct preview bundles the Windows app, portable world server, encrypted PC connection, and Connect-enabled Xbox clients. You need no VPS, domain, frp, VPN account or other tunneling application. Both Xbox memory profiles use the same world and saves on the hosting PC.

**Preview:** see [validation](CONNECT-VALIDATION.md). Local software checks do not establish internet reachability or successful play between real Xboxes. Original Preview 1 downloads remain unchanged.

## Requirements and limits

The host needs a UPnP Internet Gateway Device router with public IPv4 and an ISP permitting incoming TCP. Connect requests a one-hour mapping and renews it every twenty minutes. It removes only its own mapping on Stop. Routers accepting only permanent mappings are rejected. Connect never disables a firewall, changes router administration settings, enables a DMZ, or overwrites an existing mapping.

Guests only make outbound connections. A **guest behind CGNAT can join a reachable host**; a host behind CGNAT normally cannot receive this direct connection. Double NAT, Wi-Fi client isolation, router policy and ISP filtering can also prevent hosting. A successful mapping is not an external reachability test: the status says **awaiting a friend's external connection** until one arrives.

If unsupported, LAN play remains available and another friend can try hosting. This version deliberately has no relay fallback. A relay or third-party service is needed when no friend can accept an incoming connection.

Windows 10/11 x64 must support the bundled .NET 10/Bun runtime. Keep the host and each remote household's Connect PC awake. Use your trusted home LAN. Each Xbox needs a different character.

## One-time installation

1. Fully extract `RS2-Xbox-Connect-Direct-Windows.zip`. Run `INSTALL.cmd` and approve Windows' administrator prompt for the app's narrowly scoped **Private-network** firewall rules. This ordinary Windows approval cannot be silently bypassed. The installer creates a Start-menu shortcut and a versioned application folder under your Windows account.
2. Alternatively, run `RS2XboxConnect.exe` from the extracted folder and use **Set up Windows firewall** in the app. Mark only your trusted home network Private in Windows. Connect does not change this setting or enable broad Public-network access.
3. For a fresh Xbox installation, transfer the entire included `Xbox-clients/64-MB` or `Xbox-clients/128-MB` folder to a separate Xbox game folder using your usual FTP app. Launch its `default.xbe`. The 128 MB folder requires expanded hardware and a compatible BIOS; hardware validation remains pending. Its default is 480p; set `xbox_video = 720` only with a supported video setup.
4. For an existing installation, back up `default.xbe` and `config.ini`. Replace **only** the XBE with the matching Connect build and change **only `socketip = auto`** in the existing INI. Preserve display, controls, memory and audio options. Username/password fields can be blank in Connect mode. The supplied INI is a fresh-install preset, not a replacement for customized settings.

This Xbox update is necessary for discovery. Installing a PC app cannot change an Xbox executable by itself. No automatic FTP is performed. An old client cannot interpret `socketip = auto`; restore its matching old INI if reverting.

## Host

1. Choose **Host a world → Automatic direct**. Select an existing world, create one, or import a **stopped** portable server. Import copies accounts, saves and RSA identity without changing the original. Start creates a first world automatically if none exists.
2. Check the automatically selected PC adapter, especially with VPNs or multiple adapters. It must be on the Xbox's LAN. Keep the default game port unless it conflicts.
3. Click **Start connection**. The server binds to loopback; Connect starts the LAN gateways and attempts a temporary router mapping. If unsupported, the explanation appears and the world remains available locally.
4. When mapping succeeds, use **Copy invitation code** and share it privately through your normal messaging app. Connect does not send messages for you. Copy a fresh code after restarting hosting because the mapped port may change.
5. Press **Start** on the Xbox. Match its displayed code in Connect, enter the character name/password beneath the code selector, and click **Allow Xbox / update character**. Press Start on Xbox again.

New names register on first login. Existing characters require their original world password. These are private-world accounts, not official RuneScape accounts. Use a unique password. Repeat approval with different characters for additional Xboxes. Subsequent launches at the same LAN address reuse approval.

## Guest

1. Choose **Join a friend → Automatic direct**.
2. Paste the entire invitation into the code field, choose **Use invitation code**, and start. The app verifies the pinned certificate, world secret and game handshake before announcing success.
3. Press Start on Xbox. Match and approve its code in Connect with the desired character/password. Press Start again to play.
4. Leave Connect running. Additional Xboxes on the same household LAN can use this PC with separate approvals/characters. Run only one discovery-enabled Connect instance per LAN.

No central directory exists, so invitations are longer copy/paste codes, not short room numbers. They contain the host's public address, certificate fingerprint, world secret and public game key. Treat them as private credentials. Character passwords are not included.

After interruption, a fresh login opens a new connection; a lost game's TCP session cannot be resumed. Ask for a new code if the host restarted or changed address.

## Existing clients and configuration export

Existing 64 MB/128 MB clients still work through **Export Xbox config**. Enter their character credentials in the main setup fields and optionally import an existing INI to retain all other options. Export points the Xbox at this household's PC and sets the world's public RSA key. Transfer it manually. Changed PC addresses require another export. This route does not need the discovery update.

## Troubleshooting

| Message or symptom | Next step |
| --- | --- |
| No compatible UPnP router | Check the selected adapter. Discovery may be disabled or unsupported. Use LAN mode or have another friend host; Connect does not change router administration settings. |
| Non-public address / CGNAT / double NAT | A local mapping cannot traverse the upstream NAT. Another friend with a public IPv4 connection must host for this approach. |
| Permanent mappings only | Connect refuses to leave a permanent rule. Use another supported host/router. |
| Mapping ready but guest cannot connect | Check the host's Private firewall rules and ISP filtering. Test from another household: your router may lack NAT loopback to its own public address. |
| Host unreachable | Host may have stopped, slept or restarted on a new mapped port. Obtain a fresh invitation. |
| Certificate/invitation rejected | Re-copy the complete code and check the PC clock. Never bypass certificate verification. Replaced invitations intentionally stop working. |
| Xbox finds no PC | Start Connect; check adapter, cable, Private firewall, updated XBE and `socketip = auto`. Isolated guest Wi-Fi cannot discover the PC. |
| Multiple PCs found | Stop the other Connect connection on the LAN and press Start again. |
| Approval disappeared | Discovery expires after two minutes; press Start on Xbox again. |
| Wrong character/address changed | Update the Xbox approval. **Forget allowed Xboxes** clears approvals and stops existing sessions; then start and pair again. |
| Port conflict | Close the conflicting program or choose another legacy game port and rerun firewall setup. Connect reserves ports 43595–43597. |

## Stop, backups, revocation and removal

Use **Stop and save** after logging out. Connect closes sessions, removes its own router mapping, and uses the existing server save/shutdown mechanism. If the router does not confirm removal, the app reports it; the requested lease expires within one hour of renewal on conforming routers. A crash may also leave the temporary mapping until expiry.

Back up and restore only stopped worlds through the app. Backups include player data and private server keys. Restore creates a separate world. **Replace world invitation** changes the access secret, disconnects remote tunnels and rejects old codes; share the new code privately.

Closing an active app offers stop/save, keep running in the notification area, or cancel. Keep the PC awake if hosting continues.

To remove firewall rules, run the installed `scripts/Set-ConnectFirewall.ps1` with `-Program` pointing to the installed executable and `-Remove` in administrator PowerShell. Remove the versioned application folder and Start-menu shortcut. Worlds/settings in `%LOCALAPPDATA%\RS2XboxConnect` are intentionally retained; back them up before deleting them yourself.

## Network and privacy details

The internet tunnel uses .NET TLS 1.2/1.3, an invitation-pinned certificate, and a random 256-bit world secret. It forwards only to the loopback game engine. Management, web, login, friends and logger services remain private. A random high external TCP port maps to local TLS port 43595; LAN services are never mapped.

LAN services are legacy game TCP 43594 (configurable), discovery UDP 43596 and approved login TCP 43597. Discovery returns a public, session-local RSA key and no character credentials. The PC rewrites the existing revision-225 login packet locally, preserving cipher seeds and inserting the approved character. This retains protocol compatibility; it does not modernize the original game's LAN encryption. Address-based approval assumes a trusted household LAN, not protection from hostile LAN participants.

Saved credentials and host certificates use Windows-account protection. Exported legacy INIs and backups are sensitive. Diagnostics omit addresses, character names, passwords, invitations, key material and server logs. See [credits](../CREDITS.md) and [dependency notices](../connect/THIRD-PARTY.md).
