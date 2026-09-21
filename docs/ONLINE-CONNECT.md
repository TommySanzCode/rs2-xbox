# RS2 Xbox Connect: host and guest instructions

**Current release limitation:** the Windows developer ZIP does not include frp and cannot start online connections. Windows security blocked that dependency during packaging. The workflow below documents the complete implementation for builders/testers; a ready-to-play Windows bundle is pending. For playing now, use [LAN or another documented method](ONLINE.md).

Connect is a private-group preview. See [what has actually been tested](CONNECT-VALIDATION.md). Each household needs an awake Windows PC on the same LAN as its Xbox. No VPN installation on the Xbox is needed. The public relay and world host can be operated by different people.

## One-time relay setup

Ask a trusted group member to complete [Linux relay setup](ONLINE-RELAY.md). They privately supply the host with a `.rs2relay` file. This is a self-hosted service: the project does not supply a public relay or pay VPS bills.

## Host a new world

1. Download and extract **RS2-Xbox-Connect-Windows.zip**. Keep every extracted folder with the executable. Run `RS2XboxConnect.exe`.
2. Choose **Host a world**, enter a world name, then **Create world**. Connect makes a separate copy under `%LOCALAPPDATA%\RS2XboxConnect\worlds`; it does not modify an existing portable server or Xbox game folder.
3. To keep an existing world instead, run that server's Stop command and wait for confirmation, then choose **Import stopped server**. Select the outer folder containing `Server225`, `scripts`, and `options.ini`. The supported format is the released revision-225 SQLite portable server. Its accounts, player saves and matching RSA keys are copied together. The original stays intact.
4. Choose **Import relay profile** and select the private `.rs2relay` file.
5. Select the PC network adapter on the Xbox's LAN. If several adapters appear, compare with `ipconfig`. Keep local game port 43594 unless it conflicts with another listener.
6. Enter your own character name and world password. New names register when the Xbox first logs in. Existing names require the original world password.
7. Click **Start connection**. The host starts the game server and outbound relay tunnel. Allow the local TCP game port through the Windows Private-network firewall using the command in [LAN setup](ONLINE.md#same-lan). This app does not automatically change firewall rules.
8. Wait for **World server: Ready** and **Internet relay: Connected**. The local gateway should say Listening.
9. Choose **Export invitation**. Send this `.rs2invite` file privately to friends. It contains group access credentials and public game-server keys, but no character passwords, saves or private RSA key.
10. Export your Xbox config using the instructions below.

The host must keep Connect running, the PC awake, and the internet connection available. Closing an active window offers **stop/save**, **keep running in the notification area**, or **cancel**. Double-click the tray icon to reopen it. Windows logout, reboot and power loss are not substitutes for Stop and save.

## Join a friend

1. Extract the same Windows app ZIP on your own PC. Choose **Join a friend** and **Import friend's invitation**.
2. Select your **local** PC adapter on your Xbox's LAN. Your Xbox connects to your PC, not to your friend's private address or the relay address.
3. Enter your own character name/password. Names are 1–12 lowercase letters or digits. Passwords are 1–20 allowed characters and case-insensitive in the legacy server; choose one used nowhere else.
4. Start the connection. Permit the local game TCP port through the Windows Private-network firewall. The game handshake becomes Ready only when the tunnel reaches the host's game server.
5. Export and transfer your Xbox config. Multiple Xboxes in the same household can use the same PC gateway, with a separate character config for each Xbox. Set and export each player's credentials in turn; changing exported credentials does not change connected players' server accounts.

## Export and transfer Xbox settings

1. Choose **64 MB**, **128 MB 480p**, or **128 MB 720p** to match the game folder. Alternatively, choose **Preserve an existing config** to retain your exact controls and display/audio settings.
2. Click **Export Xbox config** and save `config.ini` privately. If replacing a local file, Connect first makes a timestamped backup.
3. Use your existing FTP program to back up and replace `config.ini` beside the appropriate Xbox `default.xbe`. Connect does not transfer files. No new XBE or cache is required for this companion.
4. Start/restart the Xbox game and press Start to submit the configured login. Different characters can meet, chat and trade in the same world.

If the PC address changes, stop, refresh adapters, select the new address and export again. A router DHCP reservation avoids repeating this. Do not change the Xbox's default gateway/DNS to the companion PC.

## Invitations, backups and updates

- **Replace world invitation** changes the world access secret and disconnects remote sessions. Export and distribute the replacement. It does not revoke the group's relay authentication token; the relay operator rotates that separately when someone should lose relay access.
- Treat the group as trusted: the relay credentials are shared and this preview is not a public multi-tenant hosting service. TLS protects each internet leg; the relay operator can inspect relayed traffic.
- Use **Stop and save** before **Back up world**. The backup includes server code, dependencies, database, saves and keys. Store it privately; it is not encrypted. The character password is hashed in the database, but keys and other world files are included.
- **Restore backup** creates a separate world and does not overwrite the original. Only restore your own trusted backups: they contain runnable server code. Export a new invitation after restoring.
- Before updating, stop/save and back up. Extract the new app into a separate folder. Settings/worlds remain in LocalAppData. Keep the prior app for rollback; do not run two versions together.

## Troubleshooting

| Symptom | Action |
| --- | --- |
| Relay reconnecting | Confirm the relay is running, port reachable and its endpoint correct. Reimport a current relay profile/invitation after token rotation. Check certificate expiry and both PCs' clocks. |
| Host Ready, guest unavailable | Check the guest invitation secret and whether the host's STCP tunnel is registered. Restart both connections after replacing an invitation. |
| Ready but Xbox cannot connect | Check adapter, local port, exported INI, Windows Private firewall rule and guest-Wi-Fi isolation. |
| Port already in use | Stop the conflicting program or choose another gateway port and re-export. Host engine/service ports also need to be free; stop the original portable server when using its imported copy. |
| Server startup/shutdown timeout | The engine may still be running. Keep Connect open, retry Stop and inspect private local logs. Never force-kill it as a routine fix. |
| Connection interrupted | The tunnel retries automatically. An existing game TCP session cannot be resumed by Connect; log in again after Ready returns. |
| Backup refused | Stop the selected world and any orphaned launcher first. A running database must not be copied. |

**Export diagnostics** produces a small allowlisted report excluding addresses, paths, usernames, credentials and logs. Share that report and a description of the visible status. Do not attach private profile/config files.
