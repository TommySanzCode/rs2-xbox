# v0.1.0-preview.1: Xbox client and Windows server

Download both archives from this prerelease:

| Archive | Runs on | Contents |
| --- | --- | --- |
| `RS2-2004-Xbox.zip` | Homebrew-capable original Xbox | Native XBE, blank config, font, and complete default client cache |
| `RS2-2004-Server.zip` | Separate Windows x64 PC | Portable server/runtime, matching packed game data, Start/Stop, Options, and Help |

The client targets **stock 64 MB RAM** and RuneScape 2 **revision 225 (2004)**.
The server PC must remain running while you play. Current official OSRS worlds
and Jagex accounts are not used.

## Set up and play

1. Fully extract both archives. Keep each extracted folder's contents together.
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
6. FTP the **whole** `RS2-2004-Xbox` folder to the Xbox, such as
   `E:\Games\RS2-2004-Xbox\`. Keep `default.xbe`, `config.ini`, `cache/`, and
   `Roboto/` beside one another. Launch `default.xbe` from the dashboard and
   press **Start** to log in.
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

Audio and controller text entry are not implemented. An earlier filtered build
was observed playing from Tutorial Island to Lumbridge at approximately
**14–16 FPS**. The latest optimized build passes compilation and host tests,
but **its hardware frame rate has not yet been measured**. Extended gameplay
and total memory use still need testing on a stock console.

See [credits](../CREDITS.md) and [third-party notices](THIRD-PARTY.md) for the
projects and components included in this work.
