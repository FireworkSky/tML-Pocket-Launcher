# tML Pocket Launcher

<img src="docs/branding/tml-pocket-launcher/current/pocket-launcher-192.png" width="112" alt="tML Pocket Launcher pixel gear and tree icon" />

**An Android launcher for precompiled tModLoader releases.** Import desktop Terraria resources or convert resources from an installed Terraria PE package, select a tML release, manage mods and launch through SDL, FNA's native backends and Android CoreCLR.

[简体中文](README.md) · [Installation](docs/getting-started.md) · [Documentation](docs/README.md) · [Development](docs/development.md) · [Issues](https://github.com/FireworkSky/tML-Pocket-Launcher/issues)

[![License](https://img.shields.io/badge/license-GPL--3.0--only-blue)](LICENSE)

## Status

This experimental community project targets **Android 9 / API 28+ on arm64-v8a**. A physical Android device reached the main menu with Terraria `1.4.4.9` assets and tModLoader `2026.09.3.1`, with the embedded touch patch loaded. Other devices, releases, world play and multiplayer still need separate testing.

## Features

- Import Steam Terraria ZIPs and GOG Linux `.sh` installers, or convert an installed Terraria PE package through the launcher's own interface.
- Download official tML releases or import complete release ZIPs. Keep multiple versions installed and choose which one to launch.
- Keep separate characters, worlds, mods, control layouts, and launch settings in game instances.
- Create, restore, import, and export per-instance save backups, with optional backups before launch.
- Queue, pause, and resume official tML release downloads.
- SteamKit2 login, Steam Guard, entitlement verification and Terraria resource transport.
- Official GOG web authorization, version selection and Linux installer downloads.
- Read mod metadata without loading the game; search, enable, disable, check dependencies and manage mod packs.
- Browse tModLoader mods under Mods and Terraria worlds and resource packs under Resources. Filter Workshop items, view details and install supported downloads.
- Convert resources extracted from the installed Terraria PE APK to PC-sized textures and PC XNB/XACT storage formats.
- Use the embedded MobileJoystick touch patch and edit controls and the game interface in the fullscreen Control Management editor. Default controls use textures extracted from Terraria PE.
- Built-in Android adapters and experimental multiplayer hosting, with optional 120 FPS rendering and a draggable FPS readout in the touch controls.
- Landscape and portrait launcher layouts, themes, custom backgrounds, motion settings and launch logs.

Terraria downloaded through the launcher's Steam integration can launch offline. Imported Steam ZIPs require an online ownership check at each launch. Imported GOG resources can launch offline. Steam authentication was verified on-device. GOG authentication with an account that owns Terraria and complete paid-resource transfers still require live validation.

## Quick start

1. Install an APK on an arm64 Android device. [Releases](https://github.com/FireworkSky/tML-Pocket-Launcher/releases)
2. On the Games page, add Terraria from an owned Steam or GOG copy, or use a converter-enabled APK to detect and convert the Terraria PE installation on the device. [Terraria PE support guide (Chinese)](docs/terraria-pe.md)
3. Choose Set Up Game and select a tModLoader version to download and install. Select another installed release if needed, import your mods, and launch the game.
4. Open Settings → Control Management to edit the layout. Tap a control for its properties, drag it to move it, and save to apply the layout on the next launch.

## Boundaries

Choose Set Up Game to open the tModLoader release list when no usable version is selected. Mainland China uses Gitee. Other regions use the official GitHub repository. Additional tModLoader versions can be downloaded or imported. Vanilla Terraria resources are supplied separately.

Terraria PE conversion is available only in arm64 APKs built with a matching mobile conversion profile. A device run converted 14,468 required resources to about 704 MiB. Actual size depends on the source game version; allow at least 2 GiB of free space while conversion is running.

With a converter-enabled build, Steam and GOG installations can also use control textures extracted from Terraria PE installed on the device. The launcher logs missing default textures and skips drawing them.

Game instances share Terraria assets and installed tML release files. Workshop reuses an existing Steam session or attempts anonymous access. Individual item permissions may block downloads, and on-device Workshop transfers still need verification.

GOG imports require a single-file installer. Download retries start over after the app process exits. The experimental hosting patch loads automatically and supports hosting on tModLoader 2026.09.3.1. The browser preview uses sample data.

## Build

The maintained scripts use Windows and PowerShell. Install .NET SDK 10, JDK 21 or a compatible Android Studio JBR, Android Platform 37, NDK `30.0.14904198 rc1`, CMake `3.22.1`, Git, Node.js 22+ with npm, and Python 3.10+ to compile touch drawing instructions. Configure local SDK/JDK paths.

```powershell
git clone --recurse-submodules https://github.com/FireworkSky/tML-Pocket-Launcher.git tML-Pocket-Launcher
Set-Location tML-Pocket-Launcher
.\scripts\build\prepare-game-references.ps1 -TmlRoot '<precompiled tModLoader installation>'
.\scripts\checks\verify.ps1 -Android -Assemble
```

Use precompiled tML `2026.09.3.1` / Terraria `1.4.4.9` references for the bundled mod and patches. References are staged only in ignored local build output. The debug APK is written to `android/app/build/outputs/apk/debug/app-arm64-v8a-debug.apk`.

Android Studio and command-line Debug and Release builds include the Terraria PE converter. Conversion uses the bundled texture dimensions and the locally installed PE assets. Select tModLoader `2026.09.3.1` to launch the game.

For the browser preview, run `npm ci` and `npm run dev` in `ui/launcher`, then visit `http://127.0.0.1:8769/preview.html`. Use `npm run check` and `npm run build` to validate types and generate production assets. See [development](docs/development.md) and [Android behavior and limitations](docs/android-launcher.md).

## Layout

| Directory         | Responsibility                                                        |
| ----------------- | --------------------------------------------------------------------- |
| `android/`        | Kotlin/Java application, native host and JNI                          |
| `managed/`        | C# host, compatibility, stores and bundled patches/mod                 |
| `ui/launcher/`    | Offline React/TypeScript UI                                           |
| `scripts/build/`  | Launcher asset preparation                                            |
| `scripts/checks/` | Structure, runtime, test and APK checks                               |
| `external/`       | Native submodules and Android MonoMod assemblies                      |
| `docs/`           | User, architecture, development and branding documentation            |

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Report issues with device/version information and sanitized logs. Do not upload credentials or game resources.

## License

The original launcher code is licensed under the GNU General Public License v3.0 (`GPL-3.0-only`). [Full license](LICENSE)

Third-party components are subject to their respective terms. The supplied MobileJoystick source has no separate license file. Terraria assets and third-party marks belong to their respective owners. [Third-party notices](THIRD_PARTY_NOTICES.md)
