# RuneScape 2 for the original Xbox

A native homebrew client for **RuneScape 2 revision 225 (18 May 2004)**, targeting
the original Xbox with **64 MB RAM**. It uses nxdk, software rendering, and a
filtered interface fitted to a 640×480 display. A compatible game server runs
separately on a PC. This client does not connect to current official OSRS worlds.

Based on **[lesleyrs/Client3](https://github.com/lesleyrs/Client3)** and the projects
credited by its author. This repository adds Xbox controller, display, and build
work to that foundation. See [CREDITS.md](CREDITS.md) for the people and projects
behind the client, and [the license notices](docs/THIRD-PARTY.md) for component details.

This repository contains source and build tools. Supply your own matching client
cache in `rom/cache/client/`; game assets, account data, and configured packages
are not included. See [third-party provenance and licensing](docs/THIRD-PARTY.md).

## Build

On Windows, install Git and MSYS2 with the MinGW64 compiler tools described in
[the Xbox guide](docs/XBOX.md). From the repository directory in PowerShell:

```powershell
.\scripts\setup-nxdk.ps1
.\scripts\build-xbox.ps1
.\scripts\Package-Xbox.ps1
```

The setup script checks out the pinned nxdk revision and its submodules into
`.deps/nxdk`. The build requires your client cache, creates a blank local config
if needed, and produces `rom/default.xbe` and `client.iso`. Packaging defaults
to a blank configuration; the script prints its output location.

Copy the complete packaged game folder to a homebrew-capable Xbox and launch
`default.xbe`. Configure a matching revision-225 LAN server and a local account
before attempting login. [Build, configuration, and deployment details](docs/XBOX.md)
explain the required files and checks.

## Controls

| Input | Action |
| --- | --- |
| Left stick | Move cursor |
| Right stick | Rotate camera |
| A / B | Left click / right click |
| X | Hold Ctrl/run modifier |
| Y | Toggle performance display |
| Start | Submit configured login |
| Back + Start | Log out when in game |
| White | Cycle normal, large, and inset interface sizes |
| Black + left stick | Move the cursor more slowly |

## Status

An earlier filtered build booted on hardware and was observed playing from
Tutorial Island to Lumbridge at approximately **14–16 FPS**. The latest optimized
source passes the Xbox build and host renderer tests; its hardware frame rate
has **not yet been checked**. Extended gameplay and total memory usage still need
measurement on a stock console.

Audio and controller text entry are not implemented. Enter login details in the
local configuration. The 789×532 logical interface is reduced to fit the TV;
area filtering preserves thin strokes, but cannot retain the original resolution.

For the staged-file privacy check and source archive command, see
[publishing the source](docs/PUBLISHING.md).
