# Self-hosted Linux relay

This guide is for the group's relay administrator. Friends use the Windows app; only the administrator needs a VPS. One relay is intended for one trusted group. It relays game traffic; characters and saves stay on the host PC.

## Requirements

- A Linux **x86-64** VPS with a public address, Docker Engine and the Docker Compose plugin. ARM deployment is not included in this preview.
- Permission to open one inbound TCP port, 7000 by default, in both the VPS provider firewall and the operating-system firewall.
- A stable DNS name pointing to the VPS or a stable public IPv4 address. Use the same value during setup; it becomes part of the TLS certificate.
- A private place to keep the relay's keys and access profile. The operator pays any VPS/bandwidth costs and must maintain the system.

Use [Docker's official installation guide](https://docs.docker.com/engine/install/). Install OS updates and keep SSH restricted according to your provider's guidance. No purchase or VPS account is supplied by this project.

## Install

1. Download **RS2-Xbox-Connect-Relay.zip**, check it against the release's `SHA256SUMS.txt`, and extract it into a directory owned by your normal administrator account.
2. Open a shell in that directory. For example:

   ```sh
   chmod +x setup.sh admin.sh
   ./setup.sh relay.example.com 7000
   docker compose up -d
   docker compose ps
   ```

   Replace `relay.example.com` with your DNS name/public IPv4. The example domain is a placeholder. `setup.sh` builds the pinned frp binary into an image, creates a private CA and server certificate, and generates random relay authentication. It refuses to silently replace existing keys.

3. Allow inbound **TCP 7000**, or the custom public port you selected. Only that port should be publicly exposed. Leave game port 43594, the policy port 10080, management dashboards, and the host's internal services unexposed. Home users need outbound access to the relay, not home port forwarding.
4. From a Windows PC, run `Test-NetConnection relay.example.com -Port 7000`. Successful TCP reachability is the first check; importing the profile into Connect verifies TLS and authentication.
5. Copy `data/relay.rs2relay` securely to the game-world host, for example through SFTP. The host imports it in Connect, creates the world invitation, and privately shares that with friends.

**Never serve the data directory over HTTP or put it in GitHub.** The profile contains relay authentication and the public CA. The directory also holds `ca.key` and `server.key`, which must stay with the relay administrator. Friends receive invitations, never these private keys.

## Operation

```sh
docker compose ps            # container status
docker compose logs --tail=50
docker compose stop         # stop relaying; game TCP sessions disconnect
docker compose up -d        # start again
```

Logs can contain endpoint information. Review locally and redact before sharing. The image runs with the invoking user's UID, drops capabilities, uses a read-only root filesystem, and exposes no dashboard. A local policy service rejects proxy types other than the app's STCP world proxies.

The private CA lasts ten years; the server certificate lasts one year. Record an annual renewal reminder. Renew **before** expiry:

```sh
./admin.sh renew
```

This stops/restarts the relay and disconnects sessions. The CA stays the same, so existing profiles remain valid. If the hostname/IP must change or the CA expires, create a separately backed-up deployment and distribute its new profile/invitations; do not bypass certificate validation.

## Remove someone's relay access

The preview uses shared group credentials. World-secret rotation in Connect stops access to that world, while relay-token rotation revokes old relay profiles for everyone:

```sh
./admin.sh rotate-token
```

Privately supply the replacement `data/relay.rs2relay` to the host. The host stops Connect, imports it, exports replacement invitations for the remaining friends, and restarts. Old invitations must fail. Do not post either version publicly.

## Backups and upgrades

Stop the relay, privately back up `.env` and `data/`, then restart. These are relay backups, not world backups; the host must separately back up the game world in Connect.

For an upgrade, retain the current folder/image and private backup. Read the new release notes, replace only program/deployment files, and preserve `.env` and `data/`:

```sh
docker compose stop
docker compose build --pull
docker compose up -d
```

The frp version/checksum are pinned in the Dockerfile. Check that both Windows app and relay use the supported version. Test a host and guest before inviting the group. To roll back, stop the new deployment and start the saved prior deployment with its matching app version.

To retire the service, stop/remove its container, remove the public firewall rule, and keep or securely dispose of the private data according to your needs. Removing only the image does not remove keys on disk.

## Troubleshooting and limits

- **Port closed:** inspect provider firewall, OS firewall, Compose port mapping and container state.
- **TLS rejected:** wrong DNS/IP, incorrect clock, expired certificate or invitation from another CA. Fix the cause; never turn verification off.
- **Authentication rejected:** relay token changed; redistribute the profile/invitations privately.
- **Proxy registration rejected:** only revision-225 Connect STCP proxies with the expected name are allowed. This deployment is not a general-purpose public proxy.
- **Latency:** pick a relay near the participants/host. Traffic travels through the VPS; there is no direct peer-to-peer optimization in this preview.
- **Relay trust:** TLS terminates at the relay. The operator can observe traffic; do not describe this deployment as end-to-end encrypted.

See [validation status](https://github.com/TommySanzCode/rs2-xbox/blob/main/docs/CONNECT-VALIDATION.md) for deployment tests completed on this release.
