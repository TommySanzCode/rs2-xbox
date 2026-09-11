# Portable server sources and notices

The server package combines Lost City server software, RuneScape game data, Bun, and separately licensed libraries. This inventory preserves their provenance and declarations; it does not apply one license to the complete package. These projects and their contributors do not endorse this Xbox port.

## Lost City and original game data

| Component | Exact source | Included license |
| --- | --- | --- |
| Lost City Engine-TS | [`e1dea19f256c7ff1a89d47024c811c755ad2184d`](https://github.com/LostCityRS/Engine-TS/tree/e1dea19f256c7ff1a89d47024c811c755ad2184d) | [MIT, copyright 2023–2026 Lost City](licenses/LostCity-Engine-MIT.txt) |
| Lost City Content, revision 225 | [`9901aa27b60198afac49012f45f32e4eb4d5c012`](https://github.com/LostCityRS/Content/tree/9901aa27b60198afac49012f45f32e4eb4d5c012) | [MIT for source code, copyright 2023–2026 Lost City](licenses/LostCity-Content-MIT.txt) |
| Engine's Kysely Bun SQLite dialect | `src/db/dialect/` at the Engine revision above | [MIT, copyright 2024 Dylan Blokhuis](licenses/Kysely-Bun-SQLite-MIT.txt) |
| Engine's bzip2 WebAssembly dependency | `src/3rdparty/bzip2-wasm/` at the Engine revision above | [bzip2 notice, copyright 1996–2019 Julian R Seward](licenses/Engine-Bzip2-Wasm-LICENSE.txt) |

The original RuneScape game, client, artwork, music, maps, and other game assets originate with Jagex. The [Content README at the recorded revision](https://github.com/LostCityRS/Content/blob/9901aa27b60198afac49012f45f32e4eb4d5c012/README.md) expressly distinguishes its MIT source code from Jagex-owned assets. Its software license does **not** cover those assets. Packed cache and world data in a playable release retain that distinction.

The server source, its original notices, `package.json`, and lockfile accompany the portable package under `Server225/`. The local launcher and first-run setup are additions by this port. The Engine revision has the included [local bind-address patch](patches/engine-local-bind.patch), covering five runtime source files for the web, environment, login, friend, and logger services; this is the package's only change to upstream runtime source. Installed packages under `Server225/engine/node_modules/` retain their own license and notice files, authors, and version metadata; these include the runtime's native and WebAssembly dependencies. Their individual terms remain applicable. Keep those files when repackaging.

## Bun 1.4.2

The Windows runtime is the unmodified official `bun-windows-x64.zip` executable from [Bun 1.4.2](https://github.com/oven-sh/bun/releases/tag/bun-v1.4.2). Its revision is `1.4.2+744846f84`; the full source commit is [`744846f844374847c902b5e7fd59b4342a51ef99`](https://github.com/oven-sh/bun/tree/744846f844374847c902b5e7fd59b4342a51ef99).

The exact [upstream license inventory](licenses/Bun-1.4.2-LICENSE.md) is preserved verbatim. It declares Bun itself MIT-licensed and separately identifies linked libraries, embedded compatibility packages, and their contributors. This declaration is not a blanket MIT license for the executable. The Bun source tree's included [uSockets](licenses/Bun-1.4.2-uSockets-LICENSE.txt), [uWebSockets](licenses/Bun-1.4.2-uWebSockets-LICENSE.txt), and [clap](licenses/Bun-1.4.2-clap-LICENSE.txt) texts are also preserved.

Bun statically links JavaScriptCore/WebKit. The release [pins its patched WebKit](https://github.com/oven-sh/bun/blob/744846f844374847c902b5e7fd59b4342a51ef99/scripts/build/deps/webkit.ts) to [`2e2aa2290fac856d6f451ceacb58f7f5b44dd057`](https://github.com/oven-sh/WebKit/tree/2e2aa2290fac856d6f451ceacb58f7f5b44dd057). This folder includes that version's [JavaScriptCore LGPL text](licenses/WebKit-JavaScriptCore-COPYING.LIB.txt), WebCore [LGPL 2](licenses/WebKit-WebCore-LICENSE-LGPL-2.txt), [LGPL 2.1](licenses/WebKit-WebCore-LICENSE-LGPL-2.1.txt), and [Apple notice](licenses/WebKit-WebCore-LICENSE-APPLE.txt), plus WTF's [LLVM](licenses/WebKit-WTF-LICENSE-LLVM.txt), [libc++](licenses/WebKit-WTF-LICENSE-libc++.txt), [dragonbox](licenses/WebKit-WTF-LICENSE-dragonbox.txt), and [SIMDe](licenses/WebKit-WTF-LICENSE-simde.txt) texts. File-level copyright notices and further dependency notices remain in the corresponding source trees.

Source and rebuild references:

- [Exact Bun source archive](https://github.com/oven-sh/bun/archive/744846f844374847c902b5e7fd59b4342a51ef99.tar.gz) and [exact patched WebKit source archive](https://github.com/oven-sh/WebKit/archive/2e2aa2290fac856d6f451ceacb58f7f5b44dd057.tar.gz).
- [Windows build instructions at the Bun release commit](https://github.com/oven-sh/bun/blob/744846f844374847c902b5e7fd59b4342a51ef99/docs/project/building-windows.mdx).
- [Release-specific relinking instructions](https://github.com/oven-sh/bun/blob/744846f844374847c902b5e7fd59b4342a51ef99/LICENSE.md): clone the patched WebKit into `vendor/WebKit`, run `bun sync-webkit-source`, then `bun run build:local` from the matching Bun source checkout. Follow the pinned build prerequisites and retain the dependency revisions in that checkout. The executable can be replaced by a rebuilt compatible Bun in the portable runtime directory.

For this exact release, the [installation documentation](https://github.com/oven-sh/bun/blob/744846f844374847c902b5e7fd59b4342a51ef99/docs/installation.mdx) specifies Windows 10 version 1809 or later and an x64 CPU supporting **SSE4.2** (Intel Nehalem or newer, AMD Bulldozer or newer). **AVX2 is optional**: the executable selects faster paths when available. The `-baseline` downloads are aliases in this release, not a different compatibility build.

## Notice integrity

[licenses/SOURCES.json](licenses/SOURCES.json) records each copied notice's immutable upstream URL and SHA-256. License text was downloaded unchanged from the pinned official Bun/WebKit repositories or copied unchanged from the recorded Lost City sources. The upstream inventories and source links complement, rather than replace, each component's terms and original notices.
