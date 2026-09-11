# Xbox binary runtime notices

These notices accompany the native RuneScape 2 revision-225 Xbox executable. The primary C client author is **lesleyrs**, whose [Client3](https://github.com/lesleyrs/Client3/tree/d828cb3cb87f033d76f0582748e664bade049562) commit `d828cb3cb87f033d76f0582748e664bade049562` is the base. This port's changes, original source-file notices, build scripts, and full credits are published in [TommySanzCode/rs2-xbox](https://github.com/TommySanzCode/rs2-xbox). The release tag identifies the matching port source; the packaged build manifest records the executable's build provenance.

The original RuneScape game and assets originate with Jagex. The port, client, SDK, and library contributors are credited for their respective work; none of these credits claim their endorsement. No blanket license is assigned to the client or to the complete playable package. The package's other `licenses/` files, Roboto font license, and source third-party inventory remain applicable alongside these SDK notices.

## SDK and component sources

The build uses [XboxDev/nxdk](https://github.com/XboxDev/nxdk/tree/29638d0b001f179b73c3513489af10ddc2986216) at `29638d0b001f179b73c3513489af10ddc2986216`, with the recursively initialized submodules below. nxdk builds on OpenXDK and work by the XboxDev community. The following is a conservative inventory of the SDK libraries prepared and passed to the linker by `xbox.mk`; link-time optimization can discard unused library members. Retaining a notice here does not mean every library feature is active in this client.

| Component | Recorded revision | Preserved notices |
| --- | --- | --- |
| nxdk HAL, runtime, WinAPI, kernel interface, USB integration, pbkit, and network drivers | SDK commit above | [MIT](nxdk-MIT.txt), [CC0](nxdk-CC0-1.0.txt), [NCSA](nxdk-NCSA.txt), [Apache 2.0](nxdk-Apache-2.0.txt), and [source-file copyright/license blocks](nxdk-SOURCE-NOTICES.txt) |
| nxdk-pdclib | [`679081d4725ab893d0ef7822111b5c5d05a572f5`](https://github.com/XboxDev/nxdk-pdclib/tree/679081d4725ab893d0ef7822111b5c5d05a572f5) | [CC0](PDCLib-CC0.txt) and [individual source notices](pdclib-SOURCE-NOTICES.txt), including files with their own declarations |
| lwIP | [`77dcd25a72509eb83f72b033d219b1d40cd8eb95`](https://github.com/lwip-tcpip/lwip/tree/77dcd25a72509eb83f72b033d219b1d40cd8eb95) | [Modified BSD notice](lwIP-COPYING.txt), copyright Swedish Institute of Computer Science |
| nxdk SDL2 | [`2554902f7bbf469449216ee25f88016421271cd8`](https://github.com/XboxDev/nxdk-sdl/tree/2554902f7bbf469449216ee25f88016421271cd8) | [SDL notice](SDL2-COPYING.txt), copyright Sam Lantinga; Xbox adaptations by the nxdk SDL contributors |
| libusbohci | [`6a0c5fefbcd9db53f4a27ee10df99f93d5bce745`](https://github.com/XboxDev/libusbohci/tree/6a0c5fefbcd9db53f4a27ee10df99f93d5bce745) | [Apache 2.0](libusbohci-Apache-2.0.txt), [Nuvoton disclaimer](libusbohci-DISCLAIMER.md), and [source notices](libusbohci-SOURCE-NOTICES.txt) |
| SDL_ttf and its bundled FreeType 2.4.12 | [`200901a8f55bcf7aa031157f53a575c942d2b7aa`](https://github.com/libsdl-org/SDL_ttf/tree/200901a8f55bcf7aa031157f53a575c942d2b7aa) | [SDL_ttf](SDL_ttf-COPYING.txt); FreeType [license overview](FreeType-LICENSE.txt), [FTL](FreeType-FTL.txt), and [GPL alternative](FreeType-GPLv2.txt) |
| SDL_image | [`ab2a9c602623193d61827ccd395302d92d90fc38`](https://github.com/libsdl-org/SDL_image/tree/ab2a9c602623193d61827ccd395302d92d90fc38) | [SDL_image notice](SDL_image-COPYING.txt) |
| zlib | [`51b7f2abdade71cd9bb0e7a373ef2610ec6f9daf`](https://github.com/madler/zlib/tree/51b7f2abdade71cd9bb0e7a373ef2610ec6f9daf) | [zlib notice](zlib-LICENSE.txt), Jean-loup Gailly and Mark Adler |
| libpng | [`a40189cf881e9f0db80511c382292a5604c3c3d1`](https://github.com/glennrp/libpng/tree/a40189cf881e9f0db80511c382292a5604c3c3d1) | [libpng notice](libpng-LICENSE.txt) and the contributors named there |
| libjpeg-turbo | [`166e34213e4f4e2363ce058a7bcc69fd03e38b76`](https://github.com/libjpeg-turbo/libjpeg-turbo/tree/166e34213e4f4e2363ce058a7bcc69fd03e38b76) | [license inventory](libjpeg-turbo-LICENSE.md) and original [IJG notice](libjpeg-turbo-README.ijg) |

This software is based in part on the work of the Independent JPEG Group. The SDK's FreeType distribution credits David Turner, Robert Wilhelm, and Werner Lemberg; its FTL copyright notice is reproduced in full above. The Xbox application's supplied Roboto font is rendered with stb_truetype, whose notice accompanies the client source and binary package.

The source-notice extracts retain the original comment text and identify each source file. The SDK's MIT template deliberately retains its upstream placeholders: actual authors and dates are preserved in `nxdk-SOURCE-NOTICES.txt`. No template has been filled in with a replacement copyright holder. [SOURCES.json](SOURCES.json) records immutable source URLs and SHA-256 hashes for every copied or extracted file.

## Rebuild and replace components

Use the source attached to the same [rs2-xbox release](https://github.com/TommySanzCode/rs2-xbox/releases) as the executable and follow [docs/XBOX.md](https://github.com/TommySanzCode/rs2-xbox/blob/main/docs/XBOX.md). In that checkout, `scripts/setup-nxdk.ps1` obtains the pinned SDK and recursive submodules; `scripts/build-xbox.ps1` builds and links the complete client. Supply the matching revision-225 cache, either from the companion playable release or your corresponding server pack.

The source includes `src/thirdparty/bzip.c` and its micro-bunzip LGPLv2 notice and license. A modified or replacement decompressor can be rebuilt and relinked through the same build scripts; no signing key from this project is required for the homebrew Xbox executable. SDK components can likewise be changed in the external SDK checkout and rebuilt. The original notices and licenses must accompany the relevant components.

nxdk source can also be obtained directly from its [recorded commit archive](https://github.com/XboxDev/nxdk/archive/29638d0b001f179b73c3513489af10ddc2986216.tar.gz). GitHub's main-repository archive does not include submodule contents, so use recursive Git initialization or the individual source revisions linked above when rebuilding.
