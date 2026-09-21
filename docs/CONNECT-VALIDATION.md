# Connect preview validation

This file records actual evidence, not promised compatibility. The existing Xbox game archives are unchanged.

## Direct preview 0.2 — current implementation

This preview implements the selected **automatic direct connection** approach. No hosted relay, domain or tunneling service account is required. Unsupported routers and non-public host addresses are reported; CGNAT guest connections can work when the host is reachable. There is no relay fallback. See [the direct guide](ONLINE-DIRECT.md).

Local validation on Windows:

- Windows app and separate nxdk 64 MB / 128 MB XBE builds compile. Existing Xbox release folders are not overwritten.
- Eight simultaneous encrypted streams of more than 256 KB retain byte integrity. Invalid secrets/certificate pins, host stop, old invitations after rotation, and reconnection with the new invitation are tested.
- Mock router tests cover finite-lease creation/renewal/removal, preserving another program's rule, rejecting CGNAT/private addresses, permanent-only routers and unsafe router XML/URLs. These tests do not change the user's router.
- Real UDP discovery requires local approval and contains no character credentials. A raw revision-225 login traverses the local credential bridge, retaining the original cipher seeds and inserting the selected character.
- **Eight separate characters logged into an actual disposable revision-225 world through the Xbox bridge plus Windows TLS tunnel, received game data, logged out normally, and logged in again.** Normal shutdown completed and eight `.sav` files were present. This is a protocol test, not movement/chat/trade testing or proof of progress persistence across a server restart.
- DPAPI round trips include host TLS identity, world access secret and per-Xbox characters; the protected settings file contains none of those plaintext credentials. Existing INI preservation tests still pass.
- Native C parser tests reject malformed/unrelated discovery responses. Diagnostic XBE path sanitation retains code, headers and file layout.
- The Windows host screen was visually inspected. Start/Stop controls were subsequently moved into a fixed bottom bar and manual configuration fields collapsed for easier access. Further GUI input was interrupted by concurrent user input; full GUI/installer flows remain unverified.

Still pending: actual router mapping/expiry across router models, external household reachability, end-to-end Xbox discovery in xemu and on hardware, full host/join/install GUI walkthrough, real Xbox gameplay and first-time user acceptance. The 128 MB client remains unvalidated on suitable hardware/BIOS. Do not interpret an automatic mapping success as proof of outside reachability.

Reproduce direct software checks:

```powershell
dotnet run --project connect/tests/Connect.Direct.Tests -c Release
dotnet run --project connect/tests/Connect.Windows.Tests -c Release
dotnet run --project connect/tests/Connect.Game.Tests -c Release -- PATH_TO_CLEAN_SERVER_TEMPLATE PATH_TO_CONNECT_SCRIPTS
```

## Earlier 0.1 relay preview — historical status

The implementation is available as a **developer preview**. A ready-to-play Windows bundle is **blocked on Windows security rejecting the official frp 0.68.0 Windows archive**. Protection has not been disabled or bypassed. The Windows developer ZIP omits frpc and cannot establish online tunnels. Source, build tooling and a separate Linux relay package are available.

## Local results — 21 September 2026

| Check | Evidence |
| --- | --- |
| WPF app build | Release build succeeds with no warnings/errors; self-contained Windows x64 publish succeeds with .NET 10.0.5. |
| UI | App launches and Host layout was visually inspected. Further desktop interaction was stopped by the user with Escape; full GUI flows remain unverified. |
| Configuration | Tests preserve enhanced graphics/audio/custom controls, handle INI sections/duplicates, map nondefault ports and reject credential injection/loopback Xbox endpoints. |
| Local gateway | Eight concurrent fragmented streams (over 128 KB each) retain byte integrity; half-close, occupied port, and listener shutdown checks pass. |
| Actual game protocol | Eight generated accounts simultaneously log into a disposable revision-225 server through the gateway, receive initial game bytes, send ordinary logout packets, and log back in with the same credentials. This does not test movement, chat or trading. |
| Server lifecycle | Fresh initialization, loopback game binding, startup readiness and clean save/shutdown pass. Existing launcher pipe inheritance and cold-start timeout issues were found and fixed. |
| World safety | Backup while running is rejected. Stopped backup, restore to a separate world and import pass. Original/restored/imported SQLite database hashes match; each retains eight player save files and the same RSA identity. |
| Windows credentials | DPAPI save/load, absence of a plaintext password in the protected file, user/SYSTEM-only directory ACLs and rejection of corrupted settings pass. |
| Linux relay | Official frp 0.68.0 archive SHA-256 verified. Actual frps/frpc STCP connections pass TLS validation and eight concurrent 256 KB streams. |
| Relay failures | Relay restart/reconnection, wrong world secret, wrong relay token, wrong certificate hostname, certificate renewal preserving the CA, world-secret rotation rejecting the old invite, and successful replacement access pass. Relay token regeneration is checked. |
| Packaging | Developer app includes the original checksum-verified clean server template and notices, but no Windows frpc. Linux relay deployment is separate. |
| Privacy | Exact Git-index check passes. Non-documentation user addresses, private paths, credentials, private-key material, generated profiles, backups, binaries and build directories are rejected. |

Commands are reproducible from the repository:

```powershell
dotnet build connect/src/Connect.App/Connect.App.csproj -c Release
dotnet run --project connect/tests/Connect.Tests -c Release
dotnet run --project connect/tests/Connect.Windows.Tests -c Release
dotnet run --project connect/tests/Connect.Tests -c Release -- --server PATH_TO_CLEAN_PORTABLE_TEMPLATE PATH_TO_CONNECT_SCRIPTS
python scripts/check-public.py
```

Linux: `python3 connect/tests/test_relay.py PATH_TO_FRP_0.68.0_LINUX_AMD64`. The relay was tested as native Linux processes in WSL; Docker-image build/runtime validation is tracked separately in GitHub Actions. A passing Linux tunnel test does not prove the untested Windows frpc process integration.

## Acceptance still requiring external testers

- Two real Xboxes in separate households joining one world, seeing each other, chatting, trading, logging out and retaining progress after restart.
- Normal NAT plus CGNAT/double NAT with no home router forwarding.
- A mixed stock 64 MB and expanded 128 MB session on suitable hardware/BIOS.
- A fresh tester following the guide without developer help.
- End-to-end Tailscale, playit.gg, public direct-forwarding and VPS-world deployments.

The installed xemu BIOS exposes 64 MB; selecting 128 MB in the emulator does not establish enhanced-build compatibility. Gateway/socket tests are not substitutes for real game actions or internet testing.
