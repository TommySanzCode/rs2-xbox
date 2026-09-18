# Xbox build and deployment

This is a native RuneScape 2 revision-225 client for a homebrew-capable original
Xbox. The default target is stock 64 MB RAM with low-memory mode enabled. See
[the 128 MB guide](XBOX-128.md) for the separate enhanced profile. A compatible
historical game server runs on another computer; current official OSRS worlds
and Jagex accounts are not supported.

## Prepare the build

The Windows build uses Git, MSYS2, and its MinGW64 environment. Install make,
Clang, LLD/LLVM tools, bison, flex, CMake, and Python 3. The build wrapper checks
its prerequisites. A checkout path without spaces is simplest; on NTFS the
wrapper can also use existing short names. It reports an error if it cannot
obtain a usable path.

From the repository directory in PowerShell:

```powershell
.\scripts\setup-nxdk.ps1
```

This installs the pinned nxdk checkout and recursive submodules in the ignored
`.deps/nxdk` directory. It does not install a global SDK.

Supply the matching packed client cache in this arrangement:

```text
rom/
  Roboto/Roboto-Bold.ttf
  cache/client/
    title
    config
    interface
    media
    models
    textures
    wordenc
    sounds
    maps/...
```

The font and its license are included. Client game assets must be supplied
separately. Use the cache produced for the revision-225 server you intend to run;
the revision number alone does not guarantee matching archive contents.

Build with:

```powershell
.\scripts\build-xbox.ps1
```

The wrapper defaults to `.deps/nxdk`. To use an existing SDK or a different MSYS2
installation, pass `-NxdkPath` or `-MsysRoot` with the appropriate local directory.
`-Jobs` controls parallel compilation.

The preparation step copies `xbox-config.example.ini` to `rom/config.ini` if no
local config exists. It generates `rom/cache/client/crc` from the eight packaged
archives: nine big-endian 32-bit checksums, with the first entry reserved as zero.
The Xbox reads that table during loading; server revision and checksum checks
remain active.

Outputs are `rom/default.xbe`, `client.iso`, and the build log/manifest in `build/`.
The ISO includes the current local config and assets. Builds and runtime files
are excluded from the source export.

## Configure a LAN game

Use a server explicitly configured for revision 225, such as
[LostCity Engine 225](https://github.com/LostCityRS/Engine-TS/tree/225), following
that server's setup instructions. Server installation, account creation, and
save data are separate from this client repository.

Edit the local `rom/config.ini`:

- Set `socketip` to the server computer's LAN IPv4 address. The loopback value in
  the template is a placeholder and refers to the Xbox itself when used there.
- The default game TCP port is `43594`; `portoff` adds to it. `nodeid = 1` maps to
  internal node ID 10 and must agree with the server's world configuration.
- Keep `lowmem = 1`; Xbox code also enforces low-memory mode. Leave the membership
  setting absent because the inherited example and parser disagree on its naming.
- Enter a separate account created on your historical server. There is no
  controller text-entry UI. Keep `remember_username` and `remember_password` at
  `1` to retain the fields in memory after logout; these flags do not write saves
  or credentials to disk.
- Use the built-in RSA public key with a matching server. If the server uses a
  different key, set its public exponent and modulus in hexadecimal. Its private
  key belongs on the server.

Login fields are plaintext in the local config. A build ISO or a package that
includes this config also contains those fields. Keep configured artifacts
private; the default packaging mode uses the blank example instead.

## Package and launch

Create a package with a blank configuration:

```powershell
.\scripts\Package-Xbox.ps1
```

For a private package containing your local connection and login settings:

```powershell
.\scripts\Package-Xbox.ps1 -PackageName RS2-2004-Private -IncludeLocalConfig
```

Copy the complete resulting game folder to the Xbox's hard drive. For example,
`E:\Games\RS2-2004\` should contain `default.xbe`, `config.ini`, `Roboto/`, and
`cache/` directly. Launch `default.xbe` from the dashboard. Copying the executable
alone is insufficient. nxdk normally maps the launched executable's directory
as `D:`, which is where this client reads its files.

The FTP helpers require an explicit `--host` and obtain credentials through
environment variables or an interactive prompt. Use each helper's `--help` for
its deployment options. There is no saved console address in the public source.

## Controller and display

| Input | Action |
| --- | --- |
| Left stick | Move cursor |
| Right stick | Rotate camera |
| A | Left click |
| B | Right click |
| X | Hold Ctrl/run modifier |
| Y | Toggle performance display |
| Start | Submit configured login |
| Back + Start | Log out when in game |
| White | Cycle normal, large, and inset interface sizes |
| Black + left stick | Move the pointer more slowly for precise clicks |

The logical interface stays at 789×532. The default fit is 608×409 centered in
640×480; White also selects 640×431 and 576×388. Cursor and click coordinates
remain aligned with the logical interface. Area filtering retains a source
canvas, processes dirty regions, and composites the cursor without storing it
in that canvas. The retained canvas occupies approximately 1.60 MiB.

## Validation and limits

An earlier filtered build booted on physical hardware and was observed progressing
from Tutorial Island to Lumbridge at about 14–16 FPS. The current optimized source
builds successfully and passes host renderer tests, but its hardware FPS has not
yet been measured. Do not infer the optimized build's performance from that
earlier observation.

With a host C compiler available, create the `build` directory if needed, then
run the renderer and CRC checks from the repository directory:

```sh
gcc -std=c99 -O2 -Wall -Wextra -Werror -pedantic src/xboxdisplay.c tests/xboxdisplay_test.c -lm -o build/xboxdisplay_test.exe
./build/xboxdisplay_test.exe
gcc -std=c99 -Wall -Wextra -Werror -pedantic src/cachecrc.c tests/cachecrc_test.c -o build/cachecrc_test.exe
./build/cachecrc_test.exe tests/fixtures/client225-crc.bin build/cachecrc-test-fixture.bin
```

The CRC check requires a nonexistent scratch-file path. Renderer checks compare
all three fits against an independent area-filter reference, with at most two
levels of error per RGB channel. They cover thin strokes, panel boundaries,
clipping, cursor movement and repaint, separated dirty regions, overflow fallback,
and memory guards.

Hardware checks still include extended gameplay, region changes, busy areas,
controller behavior, display overscan, frame time, and total free physical RAM.
The client's allocator overlay does not measure all Xbox memory. Audio and
controller text entry are absent; some network failures still produce generic
loading or login errors.

See [third-party provenance and licensing](THIRD-PARTY.md) before redistributing
source or supplying additional assets.
