# Development

## Tools

- .NET SDK 10 (see `global.json`)
- JDK 21
- Android SDK Platform 37 and NDK 30.0.14904198 rc1
- CMake 3.22.1, Git and PowerShell
- Node.js 22+ and npm (the offline UI is built during Gradle preBuild)

Configure `JAVA_HOME` and `ANDROID_HOME` locally, or use an ignored `android/local.properties` with `sdk.dir`. Do not embed developer machine paths in committed files. Native dependency sources live under `external/`; managed game sources are not build inputs.

SDL3 is fetched at the commit pinned in `android/native/CMakeLists.txt`. An explicit `TML_SDL3_SOURCE_DIR` overrides it. When CMake is configured without that variable, the previous override is cleared and the pinned source is restored. CMake checks the SDL version and every JNI registration against the launcher's Java declarations and stops before compiling incompatible bindings. For a standalone check, run `scripts/checks/check-sdl-jni.ps1 -SdlSourcePath <SDL3 source directory>`.

The default build patches SDL's Vulkan feature negotiation so FNA3D can run on Android GPUs that lack optional features. If you override SDL with a local checkout of the pinned revision, apply `android/native/cmake/sdl-vulkan-optional-features.patch` to that checkout to get the same behavior. [SDL upstream change](https://github.com/libsdl-org/SDL/pull/13016)

## Commands

```powershell
# Native dependencies
git submodule update --init --recursive external/SDL external/FNA3D external/FAudio external/tML-Pocket-Patches

# Managed projects
.\scripts\build\prepare-game-references.ps1 -TmlRoot '<installed precompiled tModLoader release>'
dotnet build MobileLauncher.slnx -c Release

# All repository checks
.\scripts\checks\verify.ps1

# Android application (publishes only launcher-managed code)
Push-Location android
.\gradlew.bat :app:assembleDebug --console=plain
Pop-Location

# Inspect the generated APK
.\scripts\checks\check-apk.ps1 -ApkPath android/app/build/outputs/apk/debug/app-arm64-v8a-debug.apk
```

The general patch assembly is checked out from `external/tML-Pocket-Patches`. The touch patch is built from `managed/MobileLauncher.Controls`. Both use this checkout's game references and MonoMod dependencies.

When preparing APK assets, the launcher script also passes `DirectoryBuildPropsPath` to the external patch build so it reads `managed/Directory.Build.props`. Android Studio does not need a shell environment variable for these shared settings.

The APK includes Android CoreCLR, launcher/store hosts and Android native libraries. When the player chooses Set Up Game without a usable tML version selected, the launcher opens the release catalog. The player chooses which release to download and install. The catalog and default download use Gitee for mainland China (CN) and the official GitHub repository elsewhere. Android supplies the mobile network country when available, with the system region as fallback.

The app can download or import additional tML releases. Terraria imports accept Steam ZIPs and GOG Linux `.sh` installers. Downloads use SteamKit/GOG account authorization. Steam downloads verify ownership before downloading and can launch offline. Imported Steam ZIPs require an online ownership check at launch. GOG resources require no login at launch.

Gradle also runs `scripts/build/prepare-launcher-ui.ps1` and packages the approved
UI under `assets/ui`. Source maps and the phone preview wrapper are excluded.
Run `npm run check`, `npm run build` and `npm run format:check` in `ui/launcher`
to validate types, generate production assets and check source formatting.
See [implemented Android paths and limits](android-launcher.md).

## Runtime validation

The Terraria PE resource converter uses C++ and JNI on Android arm64 and is included
in both Debug and Release builds, including Android Studio Run/Build.
For tML `2026.09.3.1`, the converter reads the image inventory and embedded UI
from the installed `tModLoader.dll`. tModLoader resolves its textures and shaders
from the installed release. The converter assembles UI locally from PE sprites
and programmatic drawings. The APK contains the mobile control recipes and
`lib/arm64-v8a/libterraria_converter.so`. Run `check-apk.ps1` to verify the package.

To convert the installed PE resources, select tModLoader `2026.09.3.1` and use
**Games → Add game → Terraria PE → Convert resources**. The launcher
stores and selects the resulting files in `games/terraria-android`. Start the
game from **Home → Launch game**. The Installed tab can select desktop
Terraria resources again.

Keep at least 2 GiB free on the device during conversion. The converter uses the installed Terraria PE APK as the
source of music, sound effects, fonts, and available textures.

Install the APK, import Terraria Content, select/download or import tML, and launch. Confirm the game reaches its main menu, then check touch controls, text input and a second launch for the selected tML release.

For account transport validation, use an account owning Terraria from Games >
Accounts. Complete Steam Guard or the separate GOG official login page on the
device; do not put passwords/codes in scripts or logs. Verify ownership, download,
cancellation, installation rollback on failure and token restore after reopening.
Store protocol code is in `managed/MobileLauncher.Stores`; Android token/runtime
glue is in `feature/stores`. Run managed compilation before the Gradle build,
since both restore and publish the same projects and share their obj directories.

```powershell
adb install -r android/app/build/outputs/apk/debug/app-arm64-v8a-debug.apk
adb shell am start -n app.com.tmlpocket/app.com.tmlpocket.feature.launcher.ui.MainActivity
adb logcat -v time tMLPocketLauncher:I tMLPocketCoreClr:I tMLPocketNative:I tMLPocketStatus:I tMLPocketRenderers:I SDL:I tMLPocketIme:I libc:F DEBUG:F AndroidRuntime:E "*:S"
```

## Windows Java socket issue

If a Java stack trace shows `PipeImpl` → `UnixDomainSockets.connect` → `Invalid argument`, the host's Unix-domain socket support is failing before Gradle configuration. For that environment only, set `JAVA_TOOL_OPTIONS` to `-Djdk.net.unixdomain.tmpdir=<a nonexistent local directory>` so Java's selector falls back to TCP. Apply this locally and to child JVMs; it is not an application setting.

## Bundled touch mod and patch references

Use a precompiled Terraria 1.4.4.9 / tML 2026.09.3.1 release as `TML_REFERENCE_ROOT`, or stage it with the command above. The chosen source path is remembered only in ignored `build/game-references/source.txt`. Do not point it at the tML source checkout. The launcher's build compiles MobileJoystick and its own patch assembly, never the game or FNA.

Gradle bundles `MobileLauncher.Controls.dll` with its textures and localization, `MobileLauncher.Patches.dll` and upstream GPL text.
