<h1 align="center">Ryujinx-Nextendo</h1>

<p align="center">
  <b>The Ryujinx fork with Nextendo Network built in.</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/based%20on-Ryujinx-blue" alt="Based on Ryujinx">
  <img src="https://img.shields.io/badge/license-PolyForm%20Shield%201.0.0-orange" alt="License: PolyForm Shield 1.0.0">
  <img src="https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey" alt="Platforms">
</p>

---

## What this fork adds

This repository is a fork of the open-source [Ryujinx](https://github.com/Ryubing/Ryujinx) Nintendo
Switch emulator. The emulation core (CPU, GPU, audio, input, filesystem) is unchanged from upstream:
this fork's own additions are limited to the networking layer and the surrounding user experience,
so supported games can be played online through [Nextendo Network](https://github.com/NextendoNetwork)
with no manual configuration, custom hosts file, or SSL bypass needed. See the
[org page](https://github.com/NextendoNetwork) for what Nextendo Network is and which games it supports.

> This is an independent, non-commercial project. It is not affiliated with, endorsed by, or
> associated with Nintendo. "Nintendo Switch" and all game titles are trademarks of their respective
> owners. This project ships **no** Nintendo code, keys, or copyrighted assets: you must provide your
> own legally dumped games and system files, exactly as with upstream Ryujinx.

## How online works

This fork implements the client side of Nextendo Network entirely inside the emulator, so no external
tools are needed:

- **Hostname redirection.** A built-in DNS-MITM resolver redirects the game's online hostnames to the
  configured Nextendo servers. Exact hosts-file entries still take precedence, so a custom server can
  be targeted without rebuilding.
- **Account tokens.** The emulator locally signs the account (BAAS) `id_token` that some titles verify
  before allowing online entry. The signing key is **not** bundled: it is supplied at runtime (see
  [Configuration](#configuration)).
- **NAT and P2P.** NEX titles use peer-to-peer networking (Pia) once matchmaking completes. This fork
  keeps the emulator's UDP NAT mapping alive across the P2P bring-up so hole-punching succeeds.
- **First-run wizard, online status, in-game friends, and a kill-switch** round out the experience.
- **Client attestation.** Official builds sign their API requests with a private key baked in at build
  time, so a server can tell an official release apart from an arbitrary client presenting the same
  public application ID.

None of the server infrastructure is hardcoded in this repository: addresses and keys come from
environment variables and fall back to loopback when unset, so an unconfigured build simply behaves
like stock Ryujinx offline.

## Configuration

Point a build at a Nextendo Network server (or your own) with these environment variables:

| Variable                     | Purpose                                                            | Fallback   |
| ---------------------------- | ----------------------------------------------------------------- | ---------- |
| `NEXTENDO_SERVER_IP`         | Address the main online hostnames resolve to.                     | `127.0.0.1`|
| `NEXTENDO_NAT_IP`            | Address of the second NAT-check responder (required by NAT probe).| `127.0.0.1`|
| `NEXTENDO_BAAS_SIGNING_KEY`  | PEM of the RSA key used to sign account `id_token`s. May instead be placed in a `nextendo_baas.pem` file next to the executable. | none (throwaway key) |

If none are set, online features stay dormant and this build runs as a normal offline emulator.

## System requirements

To run comfortably, your PC should have at least:

- 8 GiB of RAM
- 6 CPU cores
- A GPU released within the last 10 years, supporting OpenGL 4.6 or Vulkan 1.4
- Windows 10 (20H1) or newer, a modern Linux distribution, or macOS Big Sur (Apple Silicon) or newer

Failing to meet these requirements may result in poor performance or crashes.

## Building

This fork builds like upstream Ryujinx: you need the .NET SDK (see `global.json` for the required
version). See [COMPILING.md](COMPILING.md) for details. In short:

```sh
git clone <this-repository>
cd Ryujinx-Nextendo
dotnet build src/Ryujinx/Ryujinx.csproj -c Release
```

## License

This fork's own source is released under the **[PolyForm Shield License 1.0.0](LICENSE.md)**, a
source-available license: you may read, use, modify, and self-host the code, but you may not use it to
compete with the project.

It is derived from Ryujinx, which is licensed under the **[MIT License](LICENSE.txt)**; the upstream
license and copyright are retained in `LICENSE.txt`, as required. This project also makes use of code
from the libvpx (BSD) and ffmpeg (LGPLv3) projects. See
[distribution/legal/THIRDPARTY.md](distribution/legal/THIRDPARTY.md) for third-party notices.

## Credits

This fork stands on the shoulders of the emulator it forks and the projects Ryujinx itself builds on:

- **[Ryujinx](https://github.com/Ryubing/Ryujinx)**, originally created by **gdkchan** and continued by
  the Ryubing community: the entire emulation core.
- [LibHac](https://github.com/Thealexbarney/LibHac): filesystem.
- [AmiiboAPI](https://www.amiiboapi.com): Amiibo emulation.
- [ldn_mitm](https://github.com/spacemeowx2/ldn_mitm): one of the available local-multiplayer modes.
- [ShellLink](https://github.com/securifybv/ShellLink): Windows shortcut generation.
