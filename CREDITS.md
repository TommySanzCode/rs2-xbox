# Credits

This project builds on the work below. Source revisions, component license declarations, and retained notices are recorded in [THIRD-PARTY.md](docs/THIRD-PARTY.md) and [licenses/](licenses/).

## Original client and this Xbox version

- **[lesleyrs](https://github.com/lesleyrs)** — primary author of [Client3](https://github.com/lesleyrs/Client3), the portable C99 RuneScape 2 revision-225 client. Its existing Xbox platform support is the starting point for this repository.
- **Jagex and the original RuneScape team** — the original RuneScape game, client, and game content.
- This repository adds Xbox display fitting and filtering, controller changes, cache validation, and build/test work to the inherited client. Playable release archives include the matching historical game data separately from the Git source tree.

## Portable server release

- **[Lost City](https://github.com/LostCityRS), 2004Scape, and their contributors** — the [Engine-TS](https://github.com/LostCityRS/Engine-TS) server and [Content](https://github.com/LostCityRS/Content) project that power the matching revision-225 world.
- **[Bun](https://github.com/oven-sh/bun), its contributors, and the JavaScriptCore/WebKit contributors** — the bundled Windows runtime.
- **Dylan Blokhuis, Julian R Seward, and the authors of the server's installed dependencies** — the SQLite dialect, bzip2 implementation, and supporting libraries. Their original notices and package metadata remain included.

See [the server package notices](server-package/THIRD-PARTY.md) for exact revisions, full license texts, and runtime source/rebuild references. The server's software licenses do not cover Jagex's game assets.

## Source projects and references

The original Client3 [reference list](https://github.com/lesleyrs/Client3/blob/d828cb3cb87f033d76f0582748e664bade049562/docs/README.md) identifies these contributions and references:

- **[2004Scape/Client](https://github.com/2004Scape/Client)** and its contributors — renamed Java deobfuscation on which the C port is based.
- **[2003scape/rsc-c](https://github.com/2003scape/rsc-c)** and its contributors — libraries, networking, and platform code.
- **[2003scape/rscsundae](https://github.com/2003scape/rscsundae)** and its contributors — RSA implementation source.
- **[LostCityRS/Client-TS](https://github.com/LostCityRS/Client-TS)** and its contributors — related TypeScript port and shared implementation references.
- **[RuneWiki/rs-deob](https://github.com/RuneWiki/rs-deob)** and its contributors — unmodified Java deobfuscations.
- **[dennisdev's Client2 renderer work](https://github.com/2004Scape/Client2/compare/main...dennisdev:Client2:feature/webgl2)** — the OpenGL reference cited upstream.

The upstream document also records historical references to [Pazaz/RS2-225](https://github.com/Pazaz/RS2-225), [2003scape/rsc-client](https://github.com/2003scape/rsc-client), [2004Scape/Client2](https://github.com/2004Scape/Client2), and [galsjel/RuneScape-317](https://github.com/galsjel/RuneScape-317). These are credited as references, without asserting that each supplied code to this Xbox version.

## Xbox platform and external dependencies

- **[XboxDev/nxdk](https://github.com/XboxDev/nxdk) and its contributors** — the external homebrew Xbox SDK, runtime, hardware support, and build integration.
- **Sam Lantinga and the [SDL](https://github.com/libsdl-org/SDL) contributors**, including nxdk's SDL port contributors — controller and audio support. **Jannik Vogel** and the nxdk audio contributors provide the Xbox SDL/AC97 backend used by the enhanced build.
- **The PDCLib, lwIP, and libusbohci contributors** — C runtime, networking, and USB components provided through nxdk. The lwIP notice credits the Swedish Institute of Computer Science. Their complete notices remain with the external SDK.
- **LLVM/Clang/lld and GNU build-tool contributors** — the external compilation and build tools.

## Bundled libraries and font

- **rxi** — the INI parser.
- **Bob Jenkins** — ISAAC.
- **Rob Landley, Julian R Seward, Manuel Novoa III, and the additional contributors named in the [micro-bunzip notice](licenses/micro-bunzip-NOTICE.txt)** — decompression code and optimizations.
- **Tom St Denis and LibTomMath contributors** — multiprecision arithmetic.
- **RSC Sundae contributors** — the RSA wrappers identified by their source notices.
- **Sean Barrett and stb contributors** — `stb_image` and `stb_truetype`; their source headers retain the detailed contributor credits.
- **Bernhard Schelling** — TinyMidiLoader and TinySoundFont; **Steve Folta** — SFZero, credited as a basis for TinySoundFont. The 128 MB Xbox profile uses these libraries for music and jingles.
- **Tim Brechbill and David Bolton** — the TimGM6mb instrument SoundFont used by the 128 MB profile, distributed unmodified under GPL version 2. Thanks to **MuseScore** and the **Debian Multimedia Maintainers** for its preservation and packaging. See [the SoundFont notice](release-notices/xbox/TimGM6mb.txt).
- **Google and the Roboto contributors** — the bundled Roboto Bold font. Its embedded notice records Copyright 2011 Google Inc.

The inherited `bn.c`/`bn.h` files do not identify an author or license in their copied notices; the provenance gap is recorded in [THIRD-PARTY.md](docs/THIRD-PARTY.md) rather than filled with a guessed attribution.

These acknowledgments record provenance and references. They do not imply participation in, affiliation with, or endorsement of this Xbox version. No repository-wide license is assigned by this credits page.
