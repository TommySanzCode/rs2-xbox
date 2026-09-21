# Other ways to play online

These are alternatives to the self-hosted Connect relay. They all use the same revision-225 game server and characters. Start with [LAN setup](ONLINE.md#same-lan) to establish that your client/cache, keys and account work locally. These routes need external-network acceptance testing; see [validation](CONNECT-VALIDATION.md).

## Direct port forwarding

**Needed:** a running server PC, router access, and a publicly reachable IPv4 address. Guests need only their Xbox and home internet connection.

### Host

1. Start the original portable server and confirm LAN play. Reserve its LAN IPv4 address in the router's DHCP settings.
2. Compare the router's WAN/Internet IPv4 with the public address reported by your ISP/router. A WAN address in `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, or `100.64.0.0/10` suggests another NAT layer or CGNAT. If the ISP controls that layer, ask it about a public IPv4 or choose a tunnel. Forwarding your own router alone will not solve ISP CGNAT.
3. In the router, forward external **TCP 43594** to the server PC's LAN address, internal **TCP 43594**. Do not forward login, friends, logger, web or management ports. UDP is not needed for this game connection.
4. Add a Windows inbound rule for TCP 43594. Where practical, restrict its remote addresses to your friends' public addresses. A `LocalSubnet` rule used for LAN-only play will not admit internet clients.
5. Give friends the public IPv4 or a DNS hostname with a working IPv4 A record. Never give them your private LAN address as the internet destination. Give them the world's public RSA fields, without your character credentials.

### Guest

1. Retain your Xbox build's display/audio/control settings and set `socketip` to the host's public IPv4/hostname. Enter only the hostname/address, with no `http://`, slash or port suffix.
2. Set `portoff` to **external port minus 43594**. At the default external port, use `portoff=0`.
3. Set a distinct character name/password and the host world's `rsa_exponent` and `rsa_modulus`. Transfer the INI beside `default.xbe`, restart the game and press Start.
4. Test from a genuinely separate internet connection. Testing a public address from inside the host's house depends on router NAT-loopback support and can fail even when external access works.

The public endpoint accepts game connections from permitted internet sources. The Xbox's legacy game protocol is not a general encrypted VPN; choose Connect/Tailscale for encrypted PC-to-PC transport. Keep the server updated and avoid reusing passwords.

**Stop/cleanup:** use Stop-Server, remove the router mapping and the dedicated Windows firewall rule, and stop any dynamic-DNS updater. A changing public address requires updating friends or using maintained dynamic DNS.

## Tailscale with a local Xbox gateway

**Needed:** Tailscale on the host PC and one PC in each friend's household. Tailscale must permit the guest PC to reach the host PC's game port. The original Xbox does not run Tailscale.

1. Install Tailscale from [its official download](https://tailscale.com/download) on the participating PCs. Sign in using your own accounts.
2. The host shares the server PC using [Tailscale machine sharing](https://tailscale.com/docs/features/sharing), or an administrator places the PCs in a tailnet with an appropriate grant for TCP 43594. Each friend accepts the host's share. Check current plan terms rather than assuming a fixed free-user allowance.
3. Start the original portable server on the host PC. Permit inbound TCP 43594 on its Tailscale interface/source addresses as needed. Do not share the whole home subnet for this recipe.
4. On each friend's PC, first check `Test-NetConnection HOST_TAILSCALE_IP -Port 43594`. Replace the placeholder with the shared PC's Tailscale IPv4. This must succeed before adding the Xbox gateway.
5. On the friend's PC, use Windows' built-in TCP port proxy. In **administrator PowerShell**, substitute the actual local PC LAN address and host's Tailscale address:

   ```powershell
   netsh interface portproxy add v4tov4 listenaddress=192.0.2.10 listenport=43594 connectaddress=198.51.100.20 connectport=43594
   New-NetFirewallRule -DisplayName 'RS2 Tailscale Xbox Gateway' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 43594 -LocalAddress 192.0.2.10 -Profile Private -RemoteAddress LocalSubnet
   netsh interface portproxy show v4tov4
   ```

   `192.0.2.10` and `198.51.100.20` above are placeholders, not project endpoints. The Windows IP Helper service must be running for portproxy. Keep IPv6 support enabled on Windows; portproxy depends on it even for this IPv4 mapping. Use Microsoft's [portproxy reference](https://learn.microsoft.com/en-us/windows-server/networking/technologies/netsh/netsh-interface-portproxy).
6. Configure the Xbox with **the friend's local PC LAN address**, `portoff=0`, the host's public RSA key fields, and a distinct character. The Xbox cannot reach the Tailscale address merely because another PC has Tailscale installed.
7. Keep Tailscale and the PC running. Check the [sharing documentation](https://tailscale.com/docs/features/sharing) when troubleshooting permissions: sharing a PC does not automatically share its LAN/subnet routes.

**Cleanup:** stop the game/server as appropriate, remove the matching proxy and firewall rule, and revoke the machine share if no longer needed:

```powershell
netsh interface portproxy delete v4tov4 listenaddress=192.0.2.10 listenport=43594
Remove-NetFirewallRule -DisplayName 'RS2 Tailscale Xbox Gateway'
```

This route uses portproxy instead of Connect's frp mode; do not bind both to the same LAN endpoint. PC address changes require updating the proxy and Xbox config. Overlay connectivity can itself use a relay, so latency varies.

## playit.gg managed TCP tunnel

**Needed:** the server host PC, a playit account/agent, and a plan supporting a **custom TCP tunnel**. Friends can connect directly to the allocated public endpoint without a local companion PC.

As checked on 21 September 2026, [playit.gg's service page](https://playit.gg/) marks custom TCP/UDP under **Playit Premium**. Check [current Premium details](https://playit.gg/support/playit-premium/) before paying. This guide does not promise a free custom-TCP allocation or a fixed price.

1. Establish local play using the original portable server.
2. Install the official playit agent and link it to the host's account using the vendor's instructions. Keep the agent running.
3. Create a **custom TCP** tunnel targeting `127.0.0.1:43594` on the host PC. Do not select a different game's tunnel type or enable PROXY protocol: the unmodified RS2 game listener expects revision-225 bytes immediately.
4. Record the allocated public **hostname/address and external port**. The external port can differ from 43594; leave the local server at its original port.
5. Give friends the endpoint and public RSA fields, without your password. For example, an allocated external port of 50000 means:

   ```ini
   socketip = your-allocated-hostname.example
   portoff = 6406
   ```

   Replace the example hostname with the real allocated hostname; `50000 - 43594 = 6406`. Do not put the port into `socketip`. The endpoint must resolve to usable IPv4 for the Xbox.
6. Each friend adds their own character credentials, preserves build-specific graphics/audio settings, and transfers the INI. Test from another household.

If login fails despite a reachable tunnel, verify the local target, external-port arithmetic, IPv4 DNS, matching RSA/cache and character credentials. Native game traffic at the public endpoint uses its existing protocol; do not assume the Xbox-to-service path is a VPN.

**Cleanup:** stop the server and agent, disable/delete the tunnel in your account, and review any subscription separately. A new allocation may require new Xbox configs. Third-party service terms and availability can change.

## Hosting the whole world on a VPS

For the existing ready-to-run package, choose a **Windows x64 VPS** meeting the Bun/.NET requirements. This avoids treating the bundled Windows runtime as a Linux executable. Linux game-server deployment is an upstream-source exercise, separate from the Linux **relay** package.

1. Create and secure a Windows VPS using your provider's instructions. Account/billing setup is performed by you; the project supplies no hosting account.
2. Download and extract the original portable server. Use Options, then Start to initialize its database and RSA keys. Keep web management, login, friends, and logger services on loopback as configured by the package.
3. Permit only the game TCP port through the provider and Windows firewalls for players; restrict administrative access separately. Use a stable public IPv4 or IPv4 DNS hostname.
4. Configure each Xbox with the VPS public endpoint, correct `portoff`, public RSA fields, and its own character. The server-generated LAN config may need its `socketip` changed to the public endpoint.
5. Test from a separate network, then test clean Stop/Start and character persistence. Keep the host process running when disconnecting from Remote Desktop; signing out terminates ordinary user-session processes. Do not assume this preview installs a persistent Windows service.
6. For backups or updates, stop the engine cleanly and copy its database, save directories and RSA keys together. Store backups outside the VPS and privately. Validate a restore into a separate stopped directory before relying on it.

For a **Linux** game host, follow the pinned [Lost City Engine-TS source](https://github.com/LostCityRS/Engine-TS/tree/e1dea19f256c7ff1a89d47024c811c755ad2184d) and matching Content build instructions. Install a Linux Bun runtime and install/build dependencies for Linux; do not copy Windows `node_modules` or `bun.exe`. Reproduce the private service binds and SQLite/world settings from the portable package. This repository does not yet ship a tested Linux world-server ZIP or a turnkey Linux service recipe.

**Cleanup:** save/stop, verify a private offline backup, remove firewall exposure, and retire the VPS through your provider when finished. Relay shutdown and world-server shutdown are different operations.
