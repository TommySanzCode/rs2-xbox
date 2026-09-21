# Connect preview validation

This file records actual evidence, not promised compatibility. The existing Xbox game archives are unchanged.

## Status

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
