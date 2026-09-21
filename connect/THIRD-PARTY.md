# RS2 Xbox Connect provenance

Connect's C# application, local gateway, packaging tools, relay setup/policy, and documentation are additions to TommySanzCode/rs2-xbox. They do not replace or relicense the upstream components. No blanket license is assigned to the combined playable distribution.

**Direct preview 0.2:** the direct TLS transport, UPnP adapter, invitation format, Xbox discovery and local login bridge are newly written project code using .NET and the existing Xbox runtime. Direct packages include no frp binary or relay runtime. The frp/Go/Python/Alpine/OpenSSL entries below apply to the retained advanced relay implementation; their source notices remain for attribution. All included Xbox, audio, server and .NET notices remain applicable.

| Component | Source and notice |
| --- | --- |
| frp 0.68.0 | [fatedier/frp and contributors](https://github.com/fatedier/frp/tree/v0.68.0), Apache 2.0; verbatim `licenses/frp-0.68.0-LICENSE.txt` and the binary archive's license are included. |
| frp's Go runtime and modules | The Go authors and module contributors listed in [frp's pinned module manifest](https://github.com/fatedier/frp/blob/v0.68.0/go.mod). The manifest, checksums and Go license are retained in `licenses/`; each module retains its own license in its linked source. |
| .NET runtime and WPF | [dotnet/runtime](https://github.com/dotnet/runtime) and [dotnet/wpf](https://github.com/dotnet/wpf), Microsoft and contributors. Included runtime `LICENSE.txt` / `ThirdPartyNotices.txt`, plus verbatim source notices in `licenses/`. |
| Portable Bun / Lost City server and content | The original Preview 1 archive, with its `THIRD-PARTY.md`, complete license inventory, source and dependency notices retained under `server-template/`. Lost City, 2004Scape, Bun, JavaScriptCore/WebKit and all contributors remain credited. |
| Python / Alpine / OpenSSL relay image | [Python Software Foundation](https://docs.python.org/3/license.html), [Alpine packages](https://pkgs.alpinelinux.org/), [OpenSSL](https://openssl-library.org/source/license/). The Dockerfile builds on the official Python image and retains its notices; installed packages retain applicable licenses. |
| Original game / Xbox client | Jagex and the original RuneScape team; lesleyrs/Client3 and its credited projects; XboxDev/nxdk and contributors. See the repository's CREDITS and third-party inventory. |

The only additional upstream runtime modification made inside a **Connect world copy** changes the TCP listener to read `NODE_BIND_HOST`, retaining `0.0.0.0` as the unset default. Connect sets loopback and uses its gateway. Existing portable source/release archives are not changed. Connect also writes its own per-world environment limits; these are configuration changes.

frp Windows archive SHA-256: `959f13d0d5f17040c3e79c3d9885dc0f43e5503619ea81b123babc3daf4dbeb6`.
frp Linux x64 archive SHA-256: `3cf934477f4fb1ee9e19e49c31fb33f5ffe3283300076f59afad8b8ccf1e1621`.

This project is not affiliated with or endorsed by these authors, Jagex, Microsoft, or XLink Kai. Historical game data remains distinct from open-source software licensing.
