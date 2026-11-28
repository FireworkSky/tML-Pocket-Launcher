# Architecture

tML Pocket Launcher installs precompiled game packages and hosts them on Android. Building this repository does not compile tModLoader or managed FNA.

## Repository map

```text
android/                         Android Gradle project
  app/src/main/java/
    app/com/tmlpocket/
      feature/launcher/          Offline WebView host, restricted bridge and operation state
      feature/installation/      ZIP validation, transactional installation
      feature/files/             Restricted external file browser, export and image decoding
      feature/releases/          Official release metadata and downloads
      feature/mods/              TMOD metadata, enabled.json, packs and public Workshop
      feature/patches/           Built-in adapter catalog and declarative launch patches
      feature/stores/            Store runtime, Keystore tokens and official GOG login
      feature/game/              SDL activity and startup presentation
      core/platform/             Android services exposed through JNI
      core/runtime/              CoreCLR preparation and launch
    org/libsdl/app/               SDL Java bindings
    net/dot/android/crypto/       CoreCLR Android crypto bindings
  native/                        JNI bridge and hostfxr integration
managed/
  Directory.Build.props         Shared C# build settings
  MobileLauncher.Host/           Managed process entry point
  MobileLauncher.Bootstrap/      Assembly loading and Android compatibility
  MobileLauncher.Stores/         SteamKit2/GOG authentication and resource transport
  MobileLauncher.Patches/        Bundled tML touch, rendering and hosting adapters
  MobileLauncher.Controls/       Embedded touch patch, localization and texture metadata
scripts/
  build/                         Launcher publish and APK preparation
  checks/                        Repository and package verification
external/
  SDL/                           Native SDL2 source
  FNA3D/                         Native graphics backend source
  FAudio/                        Native audio backend source
  monomod/                       Prebuilt Android MonoMod dependencies
docs/                            Current architecture and contributor reference
ui/launcher/                     Offline React/TypeScript launcher UI (Animate UI)
  src/native/                    Typed Android request/response transport and models
  src/components/                Shared animated controls and modal adapter
  src/features/                  Launcher pages and workflows
  src/domain/                    Typed mod metadata, dependencies and pack validation
  src/state/                     Session state and versioned visual preferences
build/                           Ignored generated artifacts and diagnostics
```

## Dependency direction

Android source sets stay under `android/app/src/`. C# projects and shared build settings live under `managed/`. The root solution provides a single entry point for managed builds.

The UI sources live under `ui/launcher`; its Web `src/` directory remains separate
from Android source sets and C# projects. Gradle builds and bundles the approved
React/Animate UI interface as offline assets, hosted by the launcher WebView.
`src/native` owns the typed bridge; production panels use real device snapshots.
The normal browser mode keeps its sample catalog/workflows for design testing.
External pages cannot access the origin-scoped native bridge.

`feature/launcher` coordinates `feature/installation` and `feature/releases`. These two features use JVM file and network APIs and do not depend on Activities, JNI, or each other. `feature/game` invokes `core/runtime`, which reads verified installation state, prepares launcher/runtime files, and enters the native host. The native host starts `MobileLauncher.Host`; `MobileLauncher.Bootstrap` loads the imported game and applies Android adapters.

UI classes render state and forward actions. Controllers own background operation lifetime. Archive parsing, HTTP, manifest validation, and filesystem transactions belong outside UI classes. Introduce an interface only when there are multiple implementations or a useful testing boundary.

## On-device data

| Location under app files | Owner | Updated by |
| --- | --- | --- |
| `launcher/` | Managed host and compatibility libraries | APK asset preparation |
| `runtime/` | Android CoreCLR | APK runtime extraction |
| `games/tmodloader/` | Legacy/first imported tML release and its Content | Version-aware import/download installer |
| `games/tmodloader-<UUID>/` | Additional independent tML releases and their Content | Version-aware import/download installer |
| `games/terraria/` | Imported Terraria resources | Import installer |
| `userdata/` | Saves, mods, game configuration | Game process |
| `installed-packages.json` | Validated version catalog, selected tML and shared Terraria layout | Package installer |

Launcher updates must never merge packaged game binaries over imported packages. Installation uses staging, a durable transaction journal and recovery so cancellation or process interruption cannot silently replace the last valid installation. User data stays outside replaceable package directories.

The tML catalog is keyed by `legacy` or canonical UUIDs; filesystem destinations are derived from these keys, never from version labels or bridge paths. Older single-install manifests remain readable without moving files. A successful install selects its release; the same normalized label replaces only that release. Selection atomically updates metadata, and `installation()` supplies the selected root to runtime preparation. Uninstall renames only the specified release into a transaction backup, commits catalog/selection, then removes the backup. Interrupted uninstalls roll back before the metadata commit or finish cleanup afterward. Format-1 journals from older launcher builds remain recoverable. Version selection and uninstall are blocked while the game process runs. All versions share Terraria resources and `userdata/`; uninstall never removes either.

## Native and managed boundaries

SDL2 owns rendering and native input. The JNI bridge provides Android clipboard, IME, display, lifecycle and input events. `NativeCoreClr` package names, JNI entry symbols and native class lookups must change together. Numeric input event IDs and C ABI struct layout are versioned contracts; do not reorder enum members.

The Android application ID and source namespace are `app.com.tmlpocket`. Manifest activity names use complete source class names. Android creates the external application directory under `Android/data/app.com.tmlpocket/`; private runtime and game data remain under the application's internal files directory. The former template application ID is a separate installation and its data is not automatically migrated. The published renderer-discovery action stays `dev.tml.mobilelauncher.action.RENDERER_PLUGIN` so existing renderer APKs remain discoverable.

FNA.dll comes from the selected tML release. SDL2/FNA3D/FAudio are Android native libraries built by the launcher. Windows/Linux native libraries included in release ZIPs cannot replace the APK's Android libraries.

## Repository scope

Keep only the current launcher implementation and its dependencies in the working tree. Retired source-port code, design drafts, screenshots and investigation files belong in an external backup. Generated launcher output goes under the ignored `build/` directory; Gradle and .NET keep their conventional local output directories. External source trees retain their upstream layout and licenses.

## Touch system and bundled patches

SDL forwards raw contacts directly to FNA. `MobileLauncher.Patches` adapts FNA polling and the Terraria menu pointer. `MobileLauncher.Controls` provides in-game dual joysticks, menus and inventory. Bootstrap loads both APK assemblies before the game entry point. The touch patch attaches and releases its systems on tML content load and unload.

`LegacyTouchMod` moves previous MobileJoystick files to `userdata/Mods/.launcher-backup` and removes the old entry from the enabled list. The launcher editors and patch share `userdata/MobileControls.json`. On first use they can read the previous MobileJoystick settings from `userdata/ModConfigs`.

Only the fixed APK-owned `MobileLauncher.Patches.dll` can execute patches. Required adapters, including the hosting patch, are initialized once per game process. The optional 120 FPS patch defaults off. The touch HUD reads frame statistics from successful presents. Hosting uses the upstream exact assembly MVID guard for tML 2026.09.3.1. On other versions, the game falls back to client mode. Players create rooms from inside the game. The existing resource/social/logging/URL adapters cover the upstream base patch without installing a competing base DLL. The successful final buffer swap owns the startup overlay's first-frame signal.

Compile-only references are staged under ignored `build/game-references` from a precompiled release. No tML, FNA or Terraria resource files are included in the APK or rebuilt. The build invokes tML's packager with a separate `build/mod-packaging-userdata` directory; it never uses the developer's normal mod/save directory.
