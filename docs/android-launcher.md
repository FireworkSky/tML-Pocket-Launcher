# Android launcher

The approved React/Animate UI interface is the Android launcher, hosted offline by
MainActivity. Gradle builds the UI and packages `assets/ui`; no local server is
needed on a phone. The old Android button-list screen has been replaced.

## Functional paths

- Import Steam Terraria ZIPs, GOG Linux `.sh` offline installers or precompiled
  tModLoader release ZIPs using the launcher's file browser. GOG installers are
  parsed as data without running scripts; validation and installation are transactional.
- Persist Terraria's source alongside its installed paths. Steam ZIP imports require an active Steam session and a fresh Terraria ownership ticket
  before every launch. Steam downloads verify ownership during acquisition and can launch offline.
  Existing Steam installations without acquisition records need one successful online check after upgrade. GOG imports/downloads skip this login/ownership gate. Legacy
  installations without a source require Steam verification; reimport the GOG
  `.sh` to establish a GOG source. Selecting GOG does not accept a renamed ZIP.
- Browse paginated official GitHub Releases, filter stable/preview, confirm size,
  download, verify available hashes and install. Tasks report actual progress,
  support cancellation and retry, and survive launcher screen recreation.
- Steam, GOG and tModLoader downloads open a progress window without navigating
  away from the current page. Minimize, Escape and Android Back dismiss the window
  while the task continues. The downloads page can reopen it, cancel the task,
  retry with the same selection, and show the latest session result. Background
  completion or failure posts a notification without opening a window.
- Read the tModLoader release from the PE ProductVersion build identifier, rather
  than the base Terraria version in deps.json. Correct old installed labels at
  read time without reimporting or changing resources. Terraria versions come from
  executable version metadata, GOG gameinfo, or generated launcher-package.json;
  resource-only installations without version metadata display “版本未识别”.
- Read local TMOD metadata and Info without starting Terraria or loading mod code.
  Imports verify the archive's SHA-1 integrity (not publisher authenticity).
  Enabling resolves dependencies and minimum versions; disabling cascades through
  dependents. Launch checks the mod's tML major/minor line against the installed
  release. Damaged files are marked in the list.
- Save/apply/delete mod-pack presets. Applying resolves the complete selection
  before its atomic enabled.json write. Export uses tML-compatible JSON arrays of
  internal names; import also accepts `{ "name": "...", "mods": [...] }`.
- Manage declarative launch patches with target-version checks and enable state.
  Patch arguments are validated again at launch. Imported DLL or archive patch
  execution is not supported by this format.
- Persist appearance, launcher landscape/portrait preference, rendering scale,
  launch arguments and metered-network download restrictions. Launcher
  direction changes the Activity in place; the game remains landscape. Custom
  backgrounds are resized and stored in WebView local storage with a size bound.
- Inspect actual app storage and free space, clear only owned temporary downloads,
  and read/share bounded tails of game and startup logs.
- Start the existing SDL/CoreCLR GameActivity after installation, dependency and
  patch checks. File mutations and duplicate starts are blocked while its process
  is running. Game packages and saves remain outside the APK.

## tML version management

tML releases install side by side. Use Games → Installed or the launch bar's
version selector to choose the current launch version or uninstall one release.
Installing selects that release; reinstalling the same normalized version label
replaces only its own files. Saves, mods, configuration and Terraria resources
remain shared. Uninstalling the selected version falls back to another validated
release, or clears selection if none is usable. Damaged releases remain visible
for removal but cannot be selected. Installation and uninstall use the same durable
recovery journal; legacy single-release metadata and format-1 journals remain
readable. Version changes are blocked while the game process is running.

Unrecognized tML version labels are not deduplicated: each such import is kept
independently so an unidentified package cannot replace another by accident.

## Game and mod management pages

Games has two tabs: Installed and Add game. Installed shows Terraria's local
resource status and a single compact tML version list, with direct selection and
uninstall controls. The current release uses one badge instead of a second disabled
selection button. Add game groups official tML downloads/local ZIP import and
Steam/GOG/local Terraria acquisition. Account shortcuts resolve to this same tab;
there is no separate mirrored account page or second resource-status card. The
game page's launch dock contains the current-version picker, launch and logs,
without another account shortcut.

Mods has Installed and Mod packs tabs. Add mod opens a choice between the existing
Workshop download workspace and local multi-file TMOD import. Workshop is no
longer embedded again under a local-management tab; old browse shortcuts redirect
to its workspace and Back returns to Installed. On Mod packs, the header changes
to JSON import and the content only saves/manages local presets. This avoids a
second JSON import entry or an unrelated mod-import action on the pack page.

## Resource browser

Games → Add game → Download version opens the download workspace. Its category navigation
switches between Games (official tML releases), Mods (Steam Workshop) and Mod packs
(local JSON presets). Wide layouts place categories on the left, resource cards in
the center and search/filter controls on the right. Narrow layouts move categories
to the bottom and fold filters into a disclosure. Themes use the launcher's existing
light/dark settings.

Release search, channel and sort apply to loaded pages; Load more fetches the next
official page. Refresh replaces the list only after a successful response, and
Retry repeats the failed request. Installed-version management and ZIP import
remain available in the filter column.

The Workshop workspace provides separate Terraria and tModLoader catalogs with
search, official tag filters, sort modes, project details, and staged download/install
flows. Downloads share the foreground progress window and minimized task flow.
Viewing installed resources after a Workshop download returns to installed mods.
Mod packs support name search,
sorting, saving the enabled-mod list, JSON import/export, apply and delete. These
presets do not bundle or automatically download missing mods.

## Current limits

Steam uses SteamKit2 3.5.0 in the managed store host: account/password login,
email/Steam Guard codes or mobile approval, refresh-token restore, a Terraria
ownership ticket, Linux/public depot manifests and verified content chunks.
`Content` resources and bounded version probes from the same manifests are
downloaded and SHA-1 checked. Only Content and generated version metadata enter
the resource ZIP; desktop executables are discarded before installation. The
existing transactional installer commits the result.

Terraria's detail page reads the public Steam Store app-details endpoint for
app 105600: localized summary, developers, publishers, genres, release date and
header art. Metadata uses a 24-hour native cache and retains cached content when
offline. Artwork is restricted to official Terraria CDN paths and decoded to a
bounded raster before entering the offline WebView. Detail requests do not need
Steam login and do not count as ownership verification.

GOG uses a separate unprivileged Activity for the official authorization pages,
code exchange and refresh, owned-library checks and Linux installer selection.
The installed-client OAuth parameters match the public Galaxy client parameters
used by the reference launcher. A downloaded single-file makeself installer is
parsed as data: its appended ZIP is read, paths and CRCs are verified, and only
`Content` and generated version metadata are retained. Bounded gameinfo or
assembly metadata supply the version; official installer metadata is the download
fallback. No shell scripts or desktop binaries are executed.

Steam account sign-in and Terraria entitlement were verified on the connected
device with 0.1.2, with successful login also confirmed by the user. GOG account
authentication and complete paid-resource transfers have not been validated
with an owned account. These checks do not establish game compatibility. GOG
multipart installers are not supported. Steam uses the public Linux resource branch.

Passwords and Guard codes are not persisted. Refresh tokens are retained only
in Android Keystore AES-GCM encrypted preferences; they are removed from managed
responses before they reach the UI. Logout deletes the respective saved token;
GOG logout also clears the login cookies. Downloads support cancellation and
retry from the account panel, without resumable transfer across process death.

The Workshop workspace separates Terraria (app 105600) from tModLoader (app
1281930), with search, category filters, sorting, item details, and staged downloads.
The tModLoader catalog filters by its official mod tags, supported version tags, and
language tags. Terraria worlds and resource packs are installed into their respective
user-data locations; tModLoader packages are validated and imported as mods. Existing
Steam sessions are reused; when signed out, public content is requested anonymously.
Steam may deny individual items. The native browser fixture and automated tests do not
replace a real-device download check.

The installer selects one active tML release from the independently installed
versions described above. There is no resumable
download/pause engine. Launcher settings provide fullscreen control and interface editors;
the Android joystick overlay has been removed. Rendering uses OpenGL ES with
50%/75%/100% backbuffer scaling through the migrated patch.

The browser's normal mode remains a UI preview. Its native bridge test uses a
fixture and does not establish Android game compatibility. Successful compilation,
JVM tests and APK inspection do not prove a particular imported tML version reaches
the game menu; that requires a real-device run with owned resources.

## Verification record (2026-11-16, 0.1.0)

The arm64 debug APK (`0.1.0`, version code `1040410`) was built and upgraded
in place on a Xiaomi 23113RKC6C with WebView 143. Device checks covered the
offline interface, actual GitHub release metadata, closing the release drawer
with Android Back, opening/cancelling the system document picker, and switching
between portrait and landscape. The final device state is the landscape home
page. No game-resource installation or game-menu startup was exercised.

Verification passed for 47 Android JVM tests, 15 managed checks, all six browser
suites (including the native bridge fixture), and launcher-only APK inspection.
The fixture verifies bridge requests and state rendering; it is not a device
test of downloads or game startup.

The additional `:app:lintDebug` check does **not** pass: it reports 60 errors and
75 warnings, primarily in the existing vendored SDL permission/API code, plus
legacy back handling in GameActivity. The new launcher Activity's back dispatch
was migrated to the platform callback on Android 13+. These findings are not
hidden by a baseline or global suppression. The report is generated at
`android/app/build/reports/lint-results-debug.html`.

### Store integration / fullscreen (0.1.1)

The launcher now hides both system bars on creation, resume and restored focus,
using transient swipe access. On the connected Xiaomi, the landscape right-side
navigation-bar inset was confirmed as `visible=false`, including after GOG login
cancellation. Steam's real managed host returned the signed-out account state;
GOG opened `login.gog.com` in a WebView without `LauncherNative`.

Thirteen new managed tests cover content selection, unsafe paths, Steam file hash
failure, makeself extraction, GOG ZIP CRC failure and malformed installers.
The browser native-bridge fixture covers Steam credentials/Guard progression,
logout and resource download, plus GOG authorization, version selection and
download requests. These fixture tests use no real store accounts.

The final 0.1.1 verification run passed all 28 managed tests (15 compatibility +
13 store tests), 47 Android JVM tests, six browser suites, formatting and APK
inspection. The additional Android Lint findings recorded above are not included
in that pass claim. No real account credentials were used during this run.

Implementation boundaries: `managed/MobileLauncher.Stores` owns store protocols,
`feature/stores` owns encrypted tokens, host transport and the official GOG
Activity, and `ui/launcher/src/features/native/steam` / `gog.tsx` own account controls.
The host listens only on IPv4 loopback with a random per-process secret. It runs
on a dedicated thread in the launcher process, independently of the `:game`
CoreCLR instance. Store download archives feed the existing package installer.

### Steam connection recovery (0.1.2)

Steam connections now have separate callback lifetimes for each attempt and a
random LoginID to avoid collisions with other clients. Transient failures try
WebSocket, TCP, then the SteamKit bootstrap nodes without depending on a
successful SteamDirectory request. Permanent authentication errors stop retries.
Refresh tokens are retained immediately after authentication, so a subsequent
session disconnect can recover without repeating the password/Guard exchange.
Ready sessions reconnect after transient disconnects; manual recovery is also
available. Returning to the foreground also retries a transient failure using the
saved session, because Android background restrictions can block even loopback
sockets. Permanent authentication/logoff errors do not trigger this recovery.
Connection diagnostics expose fixed failure categories rather than
raw protocol logs. A credential-free connection probe is available in the panel.

The 0.1.2 APK (version code `1040412`) was upgraded in place on the connected
Xiaomi. The managed connection probe reached Steam via WebSocket. The actual
account state subsequently reported `ready` with Terraria ownership, and the
user confirmed successful sign-in. No passwords or Guard codes were collected
through development tools. A full resource download was not exercised.
After the final upgrade, automatic token restoration also reached `ready` through
the bootstrap WebSocket node after both directory-based routes timed out.
In a subsequent device check, the app was backgrounded for 35 seconds; its Steam
socket was interrupted by background restrictions. After returning to the
launcher, the saved session recovered to `ready` with Terraria ownership without
password or Guard entry.

Retired SteamKit clients disconnect on a background task after their callbacks
are detached. SteamKit synchronously waits for server discovery in Disconnect;
the regression test reproduces how that wait used to block protocol fallback.

Verification passed for 50 managed tests (15 compatibility + 35 store tests),
47 Android JVM tests and six browser suites. New regression cases cover failure
before Guard, protocol/bootstrap fallback, permanent authentication errors,
token preservation, reconnect, foreground recovery, stale disconnect callbacks
and connection probes.
The existing Android Lint findings above remain outside this verification pass.

### Steam pages (0.1.3)

Steam login and resource selection are full content pages using Steam-like blue
surfaces; the existing compact header and launcher navigation remain. The launch
dock is hidden on account/download pages and its grid column is reclaimed.
Login, Guard and mobile confirmation are rendered within one login route. A
successful session navigates automatically to the resource library. Starting a
download navigates immediately to the dedicated download route; completion keeps
the verification/installation result there with a link to installed resources.

`steam/state.tsx` owns the session view and current download request above page
content, so progress, cancellation, error/retry and the last result survive
navigation. This is in-memory state for the current app session. The page does
not claim persistent download history, transfer speed, pause/resume or a total
size the native transport has not supplied. GOG retains its official-page flow.
Steam account and password are still submitted through the existing SteamKit
bridge; this UI does not use a browser OAuth login or expose unsupported QR login.

Native routes reuse Animate UI PageTransition, support Android Back/Escape and
retain per-page scroll positions. Browser native fixtures cover email Guard,
login/library/download navigation, six phone sizes, the keyboard viewport,
navigation during download, failure, cancellation, retry and completion.

The 0.1.3 APK (version code `1040413`) was upgraded in place on the Xiaomi.
The actual WebView (914 x 411 CSS pixels) rendered the dedicated Steam route,
restored the existing account and navigated to the library, opened the empty
download page and returned to the library. No horizontal overflow or JavaScript
errors were observed. This device check did not log out or start a resource
download. Six browser suites, 47 Android JVM tests, TypeScript/formatting and
launcher-only APK inspection passed. The existing Lint findings remain.

### Steam details and resource-source imports (0.1.5)

The detail page now uses official Steam metadata with native artwork decoding,
bounded responses and fresh/stale cache handling. Steam ZIP and GOG Linux `.sh`
imports have separate choices, source labels and launch requirements. GOG local
imports reuse the managed makeself parser without GOG authentication. The source
is part of the package transaction, so a failed replacement retains the previous
source as well as the previous resources. Missing/unknown source metadata defaults
to Steam verification. For manually imported Steam ZIPs, ownership is queried again with SteamKit before each
launch; cached UI state cannot authorize it, and changing/disconnecting the account
during the query invalidates the result.

The 0.1.5 verification passed 38 managed store tests, 57 Android JVM tests with one
additional test skipped, all six browser suites, TypeScript/Prettier, project-layout
and launcher-only APK checks. Tests cover ownership changes, logout during a query,
source persistence and failed replacement, legacy metadata, valid local GOG
extraction without login, renamed ZIP rejection, import choices and login guidance.
Existing Android Lint findings remain as recorded above. Full GOG installer import,
paid-resource transfers and a game-menu launch still require owned-resource device
validation.

The 0.1.5 APK (version code `1040415`) was upgraded in place on the connected
Xiaomi with data retained. The actual 914 x 411 WebView showed both import choices
without horizontal overflow. Its saved Steam session restored to ready and a new
`steam.verify` request successfully obtained Terraria ownership. Steam app-details
refresh returned the Chinese summary, Re-Logic developer/publisher, genres, release
date and header art. Library/download/back navigation fit the screen without
JavaScript errors. No resource download or game launch was started; this device
currently has neither Terraria nor tML installed.

### Built-in file browser (0.1.6)

Production import, background selection and mod-pack JSON export use a reusable
full-screen launcher file browser. Native OPEN_DOCUMENT, CREATE_DOCUMENT and
WebView file chooser paths have been removed. Browser preview uploads retain
normal browser behavior. Android's system permission screen remains necessary
when granting shared-storage access; it does not select files.

The browser supports storage locations, Download/Documents shortcuts, parent/
breadcrumb navigation, current-directory search, name/date/size sorting, paginated
directory results, up to 100 mod selections and one-file resource imports. JSON
export refuses to overwrite existing files. Background selection uses bounded
native bitmap decoding before saving the image in existing UI preferences.

The revised layout prioritizes directory contents: a 44px header, a compact
landscape toolbar, no permanent sidebar, storage explanations revealed on demand,
and a fixed bottom confirmation row. The six tested phone sizes require the file
list to occupy at least half the viewport. Pagination controls appear only for
directories over 100 entries.

`feature/files` permits only registered external roots. Internal launcher storage
is not exposed. Shared roots require Android all-files access on API 30+ or legacy
storage permissions on API 28–29. Without permission, app-owned external Documents
remains available. Traversal, symbolic links, mismatched file extensions, stale
selections and protected Android/data or Android/obb paths are rejected natively.

The 0.1.6 build passed 63 Android JVM tests with two tests skipped on this Windows
host (symbolic-link creation unavailable), six browser suites, TypeScript/Prettier
and launcher-only APK checks. Browser coverage includes denied/restored storage
permissions, search/navigation, mod multiselect, cancellation, export collision
retry, Steam ZIP/GOG SH selection and background-image import. Existing Android
Lint findings remain as previously recorded.

The final 0.1.6 APK was upgraded in place on the Xiaomi. At 914 x 411 CSS pixels,
the full-screen file browser had no horizontal overflow or native HTML upload
input. A uniquely named empty mod-pack JSON was exported to the app-owned
Documents root, found using the browser search, selected and imported through the
actual native installer path. A duplicate export and path traversal were rejected.
The temporary pack and exported file were removed after the check. No game
resources, account sessions or user mod enable states were changed.

### Launch transition (0.1.8)

The launch button expands into a dedicated startup surface with real resource,
Steam-ownership and mod/patch validation stages. Repeated presses are guarded in
both the WebView and Activity. Logs remain readable while validation is busy;
failures expose retry, return and Steam sign-in guidance as appropriate. Returning
from GameActivity resets the launcher state and restores usable controls.

GameActivity displays matching native artwork during runtime preparation. Its
status, short step segments and footer occupy normal layout rows, with compact
landscape sizing and scrolling for constrained heights. App and Android motion
preferences disable the orbit and expansion; paused/detached surfaces stop their
animators. A successful FNA GraphicsDevice.Present signals the native overlay to
fade away once. Starting the Activity or entering CoreCLR does not mark the game
ready, and failed presentation leaves the startup surface visible.

Verification passed 17 Bootstrap tests, 68 Android JVM tests (two skipped for
Windows symlink restrictions), the seven browser suites, TypeScript/formatting,
project-layout and launcher-only APK checks. Browser checks include five startup
viewport sizes, duplicate presses, live stages, logs, failure actions, reduced
motion and Activity return. The APK is version 0.1.8 / code 1040418 and is installed
in place, preserving resources, saves and account sessions.

In 0.1.8, the imported tML 2026.09.3.1 reached the native startup
surface but fails in an existing compatibility patch: RuntimeMethodHook rejects
the ref-parameter signature of DetectAbnormalSteamWorkshopDownloads. Error details
and returning to the launcher have been exercised. Actual first-game-frame fade
and reaching the game menu remain unverified until that compatibility issue is
resolved; the new signal behavior is covered by managed tests. This blocker was
subsequently resolved in 0.1.9, as recorded below.

### Imported tML startup compatibility (0.1.9)

RuntimeMethodHook now supports `out` arguments without reading uninitialized
caller storage. Interceptors receive default values and their final outputs,
including outputs from the original delegate, are written back to the caller.
Unsupported `ref` inputs and by-reference returns still fail before installing a
hook. The mobile workshop anomaly adapter returns an empty message and initializes
all three outputs, matching tML's no-anomaly contract.

Desktop tML initializes SteamGameServer even with SocialMode.None. The imported
Android adapter now initializes its mod tags while leaving SteamClient and
SteamAvailable false, and refuses native GameServer initialization. This prevents
the missing `steam_api` library error and startup warning; workshop downloads stay
in the launcher's existing service. Launcher-side Steam ownership verification is
unchanged and remains required for Steam Terraria packages.

Version 0.1.9 / code 1040419 was built, inspected and installed in place. Managed
checks passed 20 Bootstrap and 42 store tests; Android JVM checks passed 68 tests
with two Windows symlink skips. The connected arm64 phone ran the existing GOG
resources and tML 2026.09.3.1 through first-frame fade, Chinese menus and player
selection. Its fresh runtime log records completed mod loading and neither the
unsupported-signature failure nor the missing Steam library failure. No new
character or world was created during verification; entering a world and gameplay
remain outside this startup check.

## Declarative patch format

Import this as a UTF-8 JSON file using Patches > Import. Newly imported patches are
disabled until enabled explicitly. `targetVersion` is a prefix of the installed
release version (including its `v` prefix if present).

```json
{
  "format": 1,
  "id": "game-language",
  "name": "游戏语言",
  "description": "启动时选择简体中文",
  "targetVersion": "",
  "launchArguments": ["-lang", "zh-Hans"]
}
```

Data-directory overrides are prohibited. Built-in Android compatibility hooks
remain part of the launcher and cannot be disabled as optional patches.

## Bridge and lifecycle

AndroidX WebViewAssetLoader serves a fixed local HTTPS origin. WebMessageListener
accepts only its main frame and validates operation names and parameters. File and
content access are disabled in the WebView; picker grants are handled by Activity.
External pages open outside the privileged WebView. Requests have per-page unique
IDs and bounded payloads. Controller workers keep application context, never a
WebView. Repository and network work is serialized off the UI thread, with throttled
progress messages. Controller ownership survives configuration recreation.

The bridge transport/models live in `ui/launcher/src/native`; actual-device page
panels live in `src/features/native` and reuse the approved CSS/controls. Example
data and demonstration controls do not appear when Android supplies the bridge.

## MobileJoystick migration (0.1.10)

The original migration used a bundled MobileJoystick client mod. The launcher now loads `MobileLauncher.Controls.dll` at startup, backs up legacy copies under `userdata/Mods/.launcher-backup`, and shares `userdata/MobileControls.json` between the editors and the game. SDL supplies raw contacts directly to FNA.

RotatingartLauncher tML touch/fullscreen/scale/frame-rate/FPS/console/hosting patches were adapted into the APK-owned MobileLauncher.Patches assembly, with source and GPL text retained. Base resource/platform behavior reuses Bootstrap. Optional frame-rate/FPS/hosting features default off. Host support retains the exact 2026.09.3.1 MVID guard. No server starts at launcher startup. Real-device verification reached the MobileJoystick settings/menu on tML 2026.09.3.1 with GOG resources. Runtime harnesses exercise synthetic tap/multitouch/attack, hook reload and native accumulator behavior. In-world touch, sustained hardware 120 FPS and real multiplayer connections remain separate verification work.
