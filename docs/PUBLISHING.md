# Publishing the source

This folder is the standalone source repository. Its Git history starts fresh;
the original development checkout, game cache, server data, local configuration,
build logs, and previously configured packages are outside it.

Before a commit or upload, stage the intended files and scan the exact index:

```sh
git add .
python scripts/check-public.py
git diff --cached --stat
```

The check rejects generated/private files, non-loopback IPv4 addresses, personal
absolute paths, common credential/private-key patterns, and filled-in example
login settings. It scans indexed blobs, including changes that are staged but
no longer present in the working file. Pattern checks do not replace review.

Create a source archive containing only the checked index:

```sh
python scripts/export-source.py ../rs2-xbox-source.zip
```

The archive excludes Git metadata and refuses to overwrite an existing archive.
The game package command is separate: `Package-Xbox.ps1` packages the built XBE
and supplied game assets, using blank connection/login settings by default.
`-IncludeLocalConfig` explicitly includes private settings and produces a personal
package. Game packages, ISO files, and caches are not part of the source repository.

Source provenance and component notices are recorded in
[THIRD-PARTY.md](THIRD-PARTY.md). No project-wide license was found in the pinned
Client3 checkout, so no new blanket license has been assumed for this export.
Resolve that upstream licensing status before presenting a public release as
freely licensed software.

The source repository is published at
[TommySanzCode/rs2-xbox](https://github.com/TommySanzCode/rs2-xbox).
Run the checks above before future commits or releases. Nothing in the preparation
scripts publishes automatically.
