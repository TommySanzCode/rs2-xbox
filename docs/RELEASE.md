# v0.1.0-preview.1: Xbox client and Windows server

Choose one Xbox archive and the Windows server from this release:

| Archive | Runs on | Contents |
| --- | --- | --- |
| `RS2-2004-Xbox.zip` | Homebrew-capable original Xbox | Native XBE, blank config, font, and complete default client cache |
| `RS2-2004-Server.zip` | Separate Windows x64 PC | Portable server/runtime, matching packed game data, Start/Stop, Options, and Help |
| `RS2-2004-Xbox-128MB-480.zip` | Xbox with 128 MB exposed by its BIOS | Experimental high-detail client with music/effects; 640×480 output |
| `RS2-2004-Xbox-128MB-720p.zip` | Xbox with 128 MB and 720p support | Same enhanced XBE, configured to request 1280×720 output |

The original `RS2-2004-Xbox.zip` targets **stock 64 MB RAM**. The additional
128 MB downloads are experimental and require expanded RAM. All use RuneScape 2
**revision 225 (2004)** and **the same included server**. You do not need a second
server installation or new saves to switch clients. Different players can use
either client on that server with separate accounts.
The server PC must remain running while you play. Current official OSRS worlds
and Jagex accounts are not used.

## Set up and play

1. Fully extract both archives. Keep each extracted folder's contents together.
   Use a short destination such as `C:\RS2\` to avoid Windows path-length limits.
2. Open `RS2-2004-Server/Options.cmd` to edit `options.ini` if needed. The defaults
   detect the PC's LAN address and use game TCP port `43594`, members content,
   and normal XP. Choose custom account settings before the first start.
3. Open `Start-Server.cmd` and wait for the ready message. First setup creates
   fresh RSA keys and a local account. Blank account options use the default
   username `xboxplayer` and generate a random password. Later starts reuse the
   local account, keys, database, and saves.
4. Open the generated `RS2-2004-Server/Xbox-config.ini`. Its `socketip` must be
   the server PC's LAN address. If automatic detection chose the wrong adapter,
   stop the server, set `server_address` through Options, and start again.
5. Copy `Xbox-config.ini` into `RS2-2004-Xbox/` as `config.ini`, replacing the
   blank template beside `default.xbe`.
   For a **128 MB** folder, then change `lowmem = 0` and add `xbox_audio = 1`,
   `xbox_music = 1`, and `xbox_video = 480` or `720` for the chosen download.
   Alternatively, copy only the connection, account, and RSA public-key fields
   from the generated file into the enhanced folder's existing `config.ini`.
6. FTP the **whole** `RS2-2004-Xbox` folder to the Xbox, such as
   `E:\Games\RS2-2004-Xbox\`. Keep `default.xbe`, `config.ini`, `cache/`, and
   `Roboto/` beside one another. Launch `default.xbe` from the dashboard and
   press **Start** to log in.
   Keep a 128 MB client in its own folder, including its `TimGM6mb.sf2` file.
7. Use `Stop-Server.cmd` when finished. It requests the normal save and shutdown
   sequence without forcing termination.

The Xbox and PC need a reachable LAN connection. If Windows Firewall blocks the
game connection, allow the configured game TCP port on your private network.
`Help.cmd` opens the included instructions; server logs are in `Server225/runtime/`.

## Options

| Setting | Default | Meaning |
| --- | --- | --- |
| `server_address` | `auto` | PC LAN address written to the Xbox configuration |
| `game_port` | `43594` | Game TCP port, `35730`–`64030`, leaving room for companion ports |
| `members` | `1` | `1` for members content; `0` for free content |
| `xp_rate` | `1` | XP multiplier, `1`–`1000`; `1` is normal |
| `account_username` | blank | Initialize or reuse the local account; custom values use 1–12 letters/digits |
| `account_password` | blank | Generate or reuse a local password; custom values use 1–20 letters/digits/spaces/underscores/hyphens |

Stop and start the server after saving options. Recopy `Xbox-config.ini` when
connection or login settings change. Changing the password option after account
creation is rejected; leave it blank to reuse the existing password.

The distributed archives contain no personal network addresses, accounts, keys,
or saves. Setup generates runtime state locally. The generated Xbox configuration
contains plaintext login details, so keep configured copies private. Preserve
the server folder to retain keys, accounts, and saves between sessions.

## Controls and preview status

| Input | Action |
| --- | --- |
| Left stick / right stick | Cursor / camera |
| A / B | Left click / right click |
| X / Y | Ctrl/run modifier / performance display |
| Start | Submit configured login |
| Back + Start | Log out |
| White | Cycle fitted screen sizes |
| Black + left stick | Move cursor more slowly |

The stock 64 MB client is silent. Controller text entry is not implemented.
The experimental 128 MB client adds music, jingles and effects, but its hardware
audio, video and frame rate still need testing; see [the enhanced guide](XBOX-128.md).
An earlier filtered 64 MB build
was observed playing from Tutorial Island to Lumbridge at approximately
**14–16 FPS**. The latest optimized build passes compilation and host tests,
but **its hardware frame rate has not yet been measured**. Extended gameplay
and total memory use still need testing on a stock console.

See [credits](../CREDITS.md) and [third-party notices](THIRD-PARTY.md) for the
projects and components included in this work.

The original release tag, stock Xbox ZIP, server ZIP and `SHA256SUMS.txt` stay
unchanged. `128MB-SHA256SUMS.txt` covers the additional downloads. The separate
`RS2-2004-Xbox-128MB-Source.zip` and each enhanced package's `Source.zip` contain
the added source; GitHub's automatic source downloads still refer to the
original Preview 1 tag.
