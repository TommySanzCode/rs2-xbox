# Source provenance and third-party notices

This repository is a source snapshot of the revision-225 RuneScape 2 C client with changes for the original Xbox. This document records the provenance and license declarations found in the supplied sources. It does not assign a repository-wide license.

## Client and SDK provenance

| Component | Recorded source |
| --- | --- |
| Client3 base | [lesleyrs/Client3](https://github.com/lesleyrs/Client3), commit [`d828cb3cb87f033d76f0582748e664bade049562`](https://github.com/lesleyrs/Client3/tree/d828cb3cb87f033d76f0582748e664bade049562). The Xbox changes in this repository are additional to that base. |
| External Xbox SDK used for validation | [XboxDev/nxdk](https://github.com/XboxDev/nxdk), commit [`29638d0b001f179b73c3513489af10ddc2986216`](https://github.com/XboxDev/nxdk/tree/29638d0b001f179b73c3513489af10ddc2986216), including its recursively initialized submodules. The SDK is not vendored here. |
| Target client | RuneScape 2 revision 225, dated 18 May 2004 in the upstream README. |

No top-level `LICENSE`, `COPYING`, or `NOTICE` file was found in the inspected Client3 base. Its README does not supply a project-wide license declaration. This absence is recorded rather than replaced with a new blanket license. The component declarations below apply to the files that carry them; they do not establish a license for the rest of the client.

The upstream [references document at the recorded base](https://github.com/lesleyrs/Client3/blob/d828cb3cb87f033d76f0582748e664bade049562/docs/README.md) credits:

- [2004Scape/Client](https://github.com/2004Scape/Client): the renamed Java deobfuscation on which the C port is based.
- [2003scape/rsc-c](https://github.com/2003scape/rsc-c): libraries, networking, and platform code.
- [2003scape/rscsundae](https://github.com/2003scape/rscsundae): RSA code.
- [LostCityRS/Client-TS](https://github.com/LostCityRS/Client-TS): the related TypeScript port.
- [RuneWiki/rs-deob](https://github.com/RuneWiki/rs-deob): unmodified Java deobfuscations.
- [The Client2 OpenGL comparison](https://github.com/2004Scape/Client2/compare/main...dennisdev:Client2:feature/webgl2): an OpenGL renderer reference.

Those are upstream attribution references, not separately verified import commits. The original RuneScape client and game originate with Jagex. Game caches and server content are separate from the tracked client source and may accompany the project's playable release assets. The server package's [third-party inventory](../server-package/THIRD-PARTY.md) preserves the distinction between Lost City's MIT source code and Jagex-owned assets. The Xbox binary's [runtime notices](../release-notices/xbox/README.md) accompany its linked SDK components; the SDK source remains an external build dependency.

## Bundled source components

Unless a version is stated below, the exact imported component version was not identified independently. The Client3 base commit above records the source snapshot from which these files were taken. Original notices remain in the source files.

| Files under `src/thirdparty/` | Observed attribution and declaration | Copied notice or license |
| --- | --- | --- |
| `ini.c`, `ini.h` | Copyright 2016 rxi; MIT text in `ini.c`. | [rxi-ini-MIT.txt](../licenses/rxi-ini-MIT.txt) |
| `isaac.c`, `isaac.h` | Bob Jenkins, March 1996; public-domain declaration and permission to use the code in any way. The file also records a May 2008 modification. | [ISAAC-NOTICE.txt](../licenses/ISAAC-NOTICE.txt) |
| `bzip.c`, `bzip.h` | micro-bunzip by Rob Landley, based on Julian R Seward's decompression code; additional credits and Manuel Novoa III optimizations are listed. The implementation says **“This code is licensed under the LGPLv2”**. | [micro-bunzip-NOTICE.txt](../licenses/micro-bunzip-NOTICE.txt) and [LGPL-2.0.txt](../licenses/LGPL-2.0.txt) |
| `mpi.c`, `tommath.h`, `tommath_class.h`, `tommath_cutoffs.h`, `tommath_private.h`, `tommath_superclass.h` | LibTomMath; Tom St Denis; `SPDX-License-Identifier: Unlicense` declarations. | [Unlicense.txt](../licenses/Unlicense.txt), copied from the explicit public-domain alternative already bundled in the stb headers. |
| `rsa-libtom.c`, `rsa-openssl.c`, `rsa-tiny.c`, `rsa.h` | Each carries “from RSC Sundae. Public domain.” | [RSC-Sundae-RSA-NOTICE.txt](../licenses/RSC-Sundae-RSA-NOTICE.txt) |
| `stb_image.h` | Version 2.30; Sean Barrett and contributors; MIT or public-domain/Unlicense alternatives at the end of the file. | [stb-dual-license.txt](../licenses/stb-dual-license.txt) |
| `stb_truetype.h` | Version 1.26; Sean Barrett and contributors; the same two license alternatives at the end of the file. | [stb-dual-license.txt](../licenses/stb-dual-license.txt) |
| `tml.h` | TinyMidiLoader 0.7; Copyright 2017, 2018, 2020 Bernhard Schelling; zlib license text. | [TinyMidiLoader-ZLIB.txt](../licenses/TinyMidiLoader-ZLIB.txt) |
| `tsf.h` | TinySoundFont 0.9; Copyright 2017–2023 Bernhard Schelling; based on SFZero, Copyright 2012 Steve Folta; MIT text. | [TinySoundFont-MIT.txt](../licenses/TinySoundFont-MIT.txt) |
| `bn.c`, `bn.h` | A big-number implementation with descriptive introductory comments. No explicit author or license declaration was found in these copied files. | No separate license assigned. |
| `rsa-bigint.c` | An alternate RSA wrapper. No explicit license declaration was found in this implementation file. | No separate license assigned. |

The micro-bunzip notice's LGPLv2 declaration is accompanied here by the [GNU Library General Public License, version 2.0](https://www.gnu.org/licenses/old-licenses/lgpl-2.0.html), dated June 1991. The complete text was downloaded unchanged from [GNU's archived plain-text source](https://www.gnu.org/licenses/old-licenses/lgpl-2.0.txt) on 11 September 2026. The 25,270-byte file has SHA-256 `cc535c21133c895b56b374c8a1dc1eb948d99003ed2b47372069456b62f42b24`. It accompanies this component's existing declaration; it is not a repository-wide license assignment.

The Xbox configuration selects `WITH_RSA_LIBTOM`. Alternate RSA implementations and the TinySoundFont/TinyMidiLoader headers are retained from the portable source tree; the Xbox audio functions currently do not use the MIDI synthesizer. Their notices are retained because the source files are included in this repository.

## Runtime font

Only `rom/Roboto/Roboto-Bold.ttf` is needed by the Xbox platform text renderer. The font was copied from the Client3 base alongside its supplied Apache License 2.0 text, retained at `rom/Roboto/LICENSE.txt` and also copied to [licenses/Apache-2.0.txt](../licenses/Apache-2.0.txt).

The embedded font metadata records:

- Copyright 2011 Google Inc. All Rights Reserved.
- Version 2.137; 2017.
- Licensed under the Apache License, Version 2.0.
- Font SHA-256: `8e8cb127554bdd9c8685788dce557e2725a9b62e183d9151fb506b3007ca6a07`.

## External dependencies and notice-copying method

nxdk supplies the Xbox runtime, networking support, SDL controller support, and build tools. Its component licenses and notices remain in the external SDK checkout at the recorded revision. This document does not attempt to replace that SDK's component inventory.

The files in `licenses/` were copied from the bundled font license, extracted from explicit source-file notice blocks, or, for LGPL 2.0, downloaded unchanged from GNU as recorded above. The stb dual-license blocks in `stb_image.h` and `stb_truetype.h` match. `Unlicense.txt` repeats their public-domain alternative for convenient reference to the same declared license used by LibTomMath. No third-party notice was synthesized into a license for the client as a whole.
