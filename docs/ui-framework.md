# Launcher UI framework

The approved interface is now packaged into the Android APK. Actual device-backed
operations and current service limits are documented in [Android launcher](android-launcher.md).
The browser's normal mode remains the design/interaction preview described below.

## Scope

Design a full launcher workspace with Home, Games, Mods, Patches and Settings. The default surface is dark charcoal and has no wallpaper. Terraria/tML identity comes from the existing game icon, game-specific language and resource/mod workflows. Custom images and pixel scenes are optional appearance settings.

## Current Web implementation: Animate UI

The active interaction prototype now lives in [`ui/launcher`](../ui/launcher/README.md)
inside the project, with React 19, strict TypeScript, Vite and the official
`@animate-ui/react` 2.4.5 package. This supersedes the earlier visualization-folder
HTML/JavaScript renderer. Start with `npm ci` and `npm run dev` in that directory;
the phone preview is `http://127.0.0.1:8769/preview.html`. `npm run build` checks
TypeScript and generates production assets.

The workspace fills the viewport and begins directly with its 48 px page header.
Page titles use 13 px, medium weight and the muted text color. Preview disclosure and the example-data toggle live in page help and
Settings > About, rather than a persistent development banner above the launcher.

| Launcher behavior | Animate UI integration |
| --- | --- |
| Main navigation | `primitives/bottom-navigation`: shared selection spring and a neutral capsule behind the active destination; icons above always-visible labels in both layouts |
| Settings selection | `primitives/effects/highlight`: dedicated 56 px horizontal category row below the compact 48 px header, with one controlled gliding highlight and arrow/Home/End keyboard navigation |
| Page changes | Official Page Transition registry component, adapted to controlled launcher keys: `slide` for main pages and directional `push` for settings categories via React transitions and Motion `AnimateView` |
| Dialogs and responsive sheets | `primitives/radix-ui/dialog`: right edge in landscape, bottom edge at ≤480 px |
| Game/mod/patch categories | `primitives/radix-ui/tabs`: accessible triggers and coordinated panel presence |
| Settings and mod enable state | `primitives/radix-ui/switch`: controlled checked state and animated thumb |
| Shared simulated task | `primitives/radix-ui/progress`: accessible progress indication |
| Buttons and action controls | `primitives/buttons/button`: default 0.97 press scale and spring return; resting touch targets remain ≥48 px |
| Primary prepare/launch action | `primitives/buttons/ripple`: pointer-origin waves clipped within a standalone launch button, with text/icons above the ripple |
| Filter and settings options | `primitives/radix-ui/select`: popper placement, shared highlight, animated selection checks and keyboard selection |
| Opacity adjustment | `primitives/radix-ui/slider`: press stretch, track-click glide, direct dragging and keyboard bounds |
| Icon hints | `primitives/radix-ui/tooltip`: delayed hints, suppressed while a dialog is open so an exiting hint cannot intercept Escape |
| Timing and motion preferences | `AnimateUIProvider`: shared tempo and system/user reduced motion |

Source boundaries: `components` contains adapters and the modal host; `features`
contains focused page/workflow components; `domain` contains typed mod metadata,
dependency resolution and pack validation; `state` contains session state and
validated preferences. React owns DOM updates and focus-preserving inputs. There
is no legacy `innerHTML` rendering or duplicate hand-written tab/focus-trap engine
in the active workspace. The old static prototype remains available for reference.

The modal adapter accounts for AnimatePresence retaining outgoing props: it marks
the exiting launcher surface inert and hidden from assistive technology immediately,
restores the initiating control, and prevents an old exit from moving focus out of
a newly opened sheet. Radix owns the modal focus trap and dismissal. Keyboard and
reduced-motion actions use immediate transitions. A few scoped layout resets adapt
the unstyled primitives to the existing 48 px header and compact launch controls.
The portrait bar is 60 px high, with five equal-width accessible destinations,
always-visible labels below their icons, and no scroll-to-hide behavior. Only the
selected destination has a neutral, borderless capsule; the surrounding bar is
transparent. The official highlight spring carries this capsule between entries.
Landscape uses the same selection treatment on the narrow rail. Launch controls remain in
their own 60 px row above it; the reserved lower area is 128 px plus the safe inset.

Settings no longer reserve a side column. Their five full category labels occupy
a dedicated 56 px row below the 48 px page header, with 16 px button padding and
8 px gaps. The row scrolls horizontally when space is limited and reveals the
active category automatically. Only the page content enters the
official Page Transition boundary, while its scroll container stays mounted and
retains positions per page/category. Game/mod/patch category tabs keep their own
Radix transitions and keyboard focus. Native view transitions are started only
for pointer navigation with motion enabled. Keyboard, reduced-motion and
unsupported-browser navigation stays immediate. Header actions and the previous
page are inert while React prepares a route commit, so a quick follow-up action
cannot run a previous page's handler. Browser snapshot transitions are short and
finish before another snapshot animation starts; the previous WAAPI page fade is
removed to avoid duplicate movement.

Appearance includes a persistent launcher orientation preference, defaulting to
landscape. Portrait/landscape selection and the phone preview toolbar synchronize
through origin/source-checked messages and resize the existing frame without
reloading its page or session. An explicit preview URL orientation takes precedence
on entry; otherwise the saved preference applies. Direct browser windows remain
responsive to their viewport. Android applies the saved direction to MainActivity
without recreating its WebView; the separate game Activity remains landscape.

Animate UI runs in the launcher's offline WebView. Kotlin owns file operations,
downloads, settings, document pickers and the separate game Activity. The existing
palette, optional wallpaper, glass treatment and visual preference schema remain.
Android hides demonstration data and uses native task/installation snapshots.
The regular browser mode still demonstrates workflows. Actual Android frame-rate
and imported-game compatibility require physical-device verification.

## 手机端布局方向

参考 PojavLauncher、Fold Craft Launcher、Zalith Launcher 这类手机端 Minecraft Java 启动器的控件组织方式，以手机横屏为主要场景。当前交付是浏览器交互设计原型，正式 Android 界面尚未改动。

- 横屏：左侧窄导航、中央可独立滚动的管理区、右侧固定的账户与游戏操作面板。下载入口在中央顶部，账户、当前配置、启动和日志集中在右侧；环境检查保留在首页资源准备列表。
- 右侧操作面板：宽 184 px，窄横屏收至 168 px；顶部连接 Steam/GOG 账户，底部只保留一个游戏版本选择器、独立启动按钮和小号日志入口。移除重复的版本菜单、独立等待状态行、装饰标识与配置标题。启动胶囊横屏最大宽 140 px、竖屏宽 120–128 px，主按钮和版本选择器的触控高度至少 48 px。日志为居中的次要文字入口，高 32 px、无玻璃底板，支持键盘和关闭面板后的焦点恢复。未安装时显示“准备游戏”，示例就绪状态显示“启动游戏”。
- 版本选择面板：区分“选择已安装版本”和“下载新版本”。没有安装时提供下载及 ZIP 导入入口；选择配置后更新启动栏和当前游戏概览。
- 竖屏：五项底部导航，启动栏紧邻导航上方。版本选择与启动按钮并排；独立启动按钮缩窄后，为版本名称留出更多空间。环境检查放在首页，日志可从设置的关于页访问，避免增加底部栏高度。
- 统一页头：所有页面只有一个页名，移除“tModLoader > 页面”面包屑、正文重复大标题及英文眉题。页头统一高 48 px，标题 13 px、字重 500、弱化文字颜色；移除横屏页头与内容之间的额外留白，分类栏下间距为 8 px。导入操作、帮助与下载任务集中在固定页头；按钮触控区域保留至少 48 px，页面说明通过帮助按需查看。正文直接从分类、搜索或列表开始。
- 首页：三行展示 Terraria 资源、tModLoader 和运行环境，每行直接进入准备流程；四项常用工具为模组、补丁、触控布局和游戏目录。中央可用宽度至少 520 px 时资源与工具并排，较窄时上下排列，取消欢迎大标题和介绍段落。
- 补丁：使用“内置适配 / 可选补丁”分类，显示数量；取消常驻介绍卡和重复分区标题。三项内置适配在 932 × 430 横屏首屏完整显示。行高至少 72 px、详情触控区至少 48 px；底部以一行说明标明必需属性，详细规则通过帮助查看。
- 外观：默认深色纯色表面，冰蓝色控件强调，保留游戏图标。场景背景由用户在设置中选择。横竖屏共用同一份版本、模组和任务状态。

原型附带横屏手机（932 × 430）和竖屏手机（390 × 844）的预览切换。这些数值是浏览器布局尺寸，正式 Android 实现按 dp、字体缩放和系统安全区域适配。

### GitHub 源码参考

- [Fold Craft Launcher · activity_main.xml](https://github.com/FCL-Team/FoldCraftLauncher/blob/main/FCL/src/main/res/layout/activity_main.xml)：左侧功能导航、中间内容容器、右侧账户/当前版本/启动按钮的分区组织。原型借鉴其集中式操作区，并适配更窄屏幕。
- [Zalith Launcher 2 · LauncherScreen.kt](https://github.com/ZalithLauncher/ZalithLauncher2/blob/main/ZalithLauncher/src/main/java/com/movtery/zalithlauncher/ui/screens/content/LauncherScreen.kt)：`ActionMenuCardContent` 与 `ActionMenuTallerContent` 根据空间重排账户、版本和启动操作；原型据此为横竖屏使用不同布局并共享状态。
- [Zalith Launcher 2 · VersionCardContent.kt](https://github.com/ZalithLauncher/ZalithLauncher2/blob/main/ZalithLauncher/src/main/java/com/movtery/zalithlauncher/ui/screens/content/home/version/VersionCardContent.kt)：版本信息与操作区各自保留所需空间，狭窄时简化展示。原型仍保持至少 48 px 的关键触控区域。
- [PojavLauncher · fragment_launcher.xml](https://github.com/PojavLauncherTeam/PojavLauncher/blob/v3_openjdk/app_pojavlauncher/src/main/res/layout/fragment_launcher.xml)：内容滚动与版本选择、启动操作分开。该仓库已归档，仅作为交互结构参考。

以上借鉴信息组织与响应式行为；浏览器原型的 HTML/CSS/JS 独立编写，没有将参考启动器的源码或界面素材移入 Android 项目。

## 液态玻璃控件风格

根据用户提供的参考图调整控件材质：导航采用无描边的中性胶囊选中底板、图标在上文字在下，切换时底板通过 Animate UI spring 平滑移动；中央为带边缘高光的资源准备列表；右侧使用磨砂玻璃面板、版本选择控件与紧凑启动胶囊，日志采用小号文字入口。竖屏将版本选择与启动胶囊移至底部导航上方。版本统一由独立选择器管理，启动胶囊不再附带重复菜单；主操作保留至少 48 px 的原型触控区域，Android 实现对应至少 48 dp。

玻璃材质采用低强度中性覆色、细高光边缘与轻阴影，移除大面积白色渐变、厚内阴影和实色蓝色渐变，避免石材或陶瓷质感。版本选择与启动按钮的背景层为 WebGL 液态玻璃透镜：按视口重建壁纸（纯色、色调渐变或自定义图片），用圆角矩形高度场求法线后做两次折射，叠加细色散、方向性边缘高光和游戏坞/胶囊覆色。透镜与控件同尺寸，仅在尺寸、主题或背景变化时按需重绘；无 WebGL 时回退为 CSS 模糊。技术思路参考 [Liquid Glass WebGL Demo](https://github.com/rxing365/html-liquid-glass-effect-webgl)。文字、图标和 Ripple 动效独立于折射层；大块内容面板使用普通 CSS 材质。液态玻璃开关位于设置外观中，关闭后移除透镜画布及其 ResizeObserver，使用静态表面。面板不透明度仍由现有设置控制。浅色和深色主题分别定义材质覆色及启动文字颜色。

原生参考为 [AndroidLiquidGlass / Backdrop](https://github.com/Kyant0/AndroidLiquidGlass) 的 `LiquidButton` 和 `LiquidBottomTabs` 示例，包括 `blur`、`vibrancy`、`lens` 和交互高光。该库基于 Compose；浏览器原型用 WebGL 透镜模拟折射，尚未集成此原生依赖，效果也不等同于 Compose 原生透镜。正式 Android 阶段需按现有 View/Controller 边界评估 Compose 控件承载方式、系统版本与设备性能，并提供静态表面回退。

This document specifies navigation and behavior. The ordinary browser preview demonstrates workflows. The Android application implements offline UI hosting, imports, official tML releases, local mods/packs, declarative patches, runtime entry, SteamKit Workshop catalog/download integration, and Steam/GOG account-resource flows. Workshop transfers use the selected Steam app ID and require live-device validation; anonymous access remains subject to per-item permissions.

## Navigation

| Destination | Primary responsibility | Main actions |
| --- | --- | --- |
| Home | Current game version, resource readiness, enabled mod count and recent activity | Prepare or launch; open the relevant failed readiness check |
| Games | Terraria resources, tML versions and linked store accounts | Steam/GOG login and download, ZIP import, version selection |
| Mods | Installed `.tmod` files, mod downloads and enabled-list packs | Search/filter, download/update with dependencies, import, batch enable/disable, inspect/delete, manage packs |
| Patches | Android compatibility and optional extensions | Inspect required adapters; import, validate, enable and restore optional patches |
| Settings | Launcher appearance, runtime preferences, input, storage and diagnostics | Change preferences, customize backgrounds, inspect data and logs |

Downloads and tasks are available from every page. Store accounts belong to Games, separate from launcher appearance and runtime settings. Landscape phones/tablets use a narrow left navigation rail and a persistent right account/version/launch panel; narrow portrait previews use five bottom destinations. The central content scrolls independently. Launch controls sit above the navigation in portrait, with accounts accessible from the header.

## Pages and flows

### Home

Start directly with a three-row resource/version/environment preparation checklist and four customizable tool entries, defaulting to mods, patches, touch layout and game directories. Place these sections side by side when the central workspace is at least 520 px wide, otherwise stack them. Tools use a compact two-column grid. Keep the page name only in the shared fixed header. First launch shows an empty state: no selected tML version, no installed Terraria resource package and no mod count. The persistent launch controls offer preparation until resource, runtime and compatibility checks succeed. A failed check opens its page with the cause and a recovery action. Sample installed profiles can be selected in the prototype, without implying real installations.

The shortcut picker offers ten destinations: installed mods, mod downloads, mod packs, patches, touch layout, directories, tasks, accounts, logs and activity. Exactly four selections are required; order follows selection order. Save commits the draft; cancellation, Escape and backdrop dismissal discard it. Visual preferences persist locally, while sample installs and session history do not.

A compact configuration overview links enabled/total mods, available updates, enabled optional patches and the current task to their corresponding workspaces. Counts derive from the current illustrative state rather than fabricated disk or network statistics. Recent activity records actual actions during the current session, capped at 50 entries, with configuration/task/message filters and confirmed clearing. No synthetic historical events are seeded.

### Games and accounts

Use three tabs: Installed, Get resources, Accounts. Terraria acquisition offers Steam, GOG and local ZIP import. tML acquisition offers the official GitHub version list and local ZIP import. Present stable/preview channel, exact release version, size, source and compatibility before replacement.

Steam flow: login or recover a saved session, complete Steam Guard/email verification when requested, verify Terraria ownership, select the compatible resource version, download and validate. Model cancellation, expired session, missing ownership, transport failure, disk-full and recovery explicitly.

GOG flow: open official web authorization, return to the launcher, verify ownership, choose a supported installer/resource version, download, extract and validate. Authorization cancellation, expired sessions, unavailable installers and extraction failures each have a distinct recovery path.

Local import flow: system picker, identify the package, show detected metadata, check compatibility/storage, stage and validate, commit or restore the previous installation. Keep user saves and mods outside replaced game packages.

### Downloads

Task state belongs to the operation controller, independent of page navigation. Show source, progress, transferred/total bytes when known, current stage and recoverable error. Provide cancellation and retry; offer pause/resume only when the underlying transport supports it. Download, validation and installation are separate stages. Do not show invented speed or remaining time.

### Mods

Use three tabs: 已安装 / 下载模组 / 模组包. Keep the compact shared header and import action; filter controls belong to the selected tab. Local management reads archive metadata and enabled lists without starting Terraria, loading mod assemblies or invoking a mod reload. Changes apply on the next game launch; lock file mutations while the affected instance is running.

Installed: show internal name, display name, author, version, source, target tML, runtime side and dependencies. Support name/author search, enabled filters, sorting, single and batch enable/disable, dependency details and confirmed deletion. Enabling recursively includes installed required dependencies; missing dependencies offer a download plan. Disabling a required dependency explicitly lists and confirms dependent mods. Deletion cannot leave dependent packages unresolved. Search updates the list without replacing the input or losing its focus.

Downloads: browse the Steam Workshop catalog, search by name or author, sort/filter, inspect metadata, target compatibility and required dependencies. Confirm the complete recursive download plan and size before enqueuing files. Support new downloads, individual updates, update all, pause/resume where the transport supports it, cancellation and recoverable failures. New installs remain disabled; updates retain each existing enabled state. Resolve the selected tML channel/version before installing and preserve the previous usable files if validation fails. Query and download are distinct provider operations; a browsable listing does not prove that file downloads are available.

Packs: save the current enabled internal-name list with its target configuration; rename, import/export, replace or add to the enabled set, update from the current set, list missing entries, download missing packages and remove the list. Removing a pack preserves mod files and saves. The current prototype's exported `tml-mobile-mod-list` JSON is an explicit launcher list format, not a tML frozen snapshot and does not contain `.tmod` binaries. tML snapshot/legacy pack import requires format detection and conversion in native implementation.

Configuration: manage existing `ModConfigs` JSON separately from binary packages. The prototype includes JSON draft validation only. tML's typed configuration screens depend on loaded mod assemblies; leave those screens in game. Do not execute mod code merely to display a launcher settings form or pretend syntax validation is a mod-specific schema check.

Prototype coverage: searchable/filterable illustrative catalog, details, incompatible-version recovery, dependency download plan, a shared simulated task, explicit simulated installation, local dependency actions, bulk changes, deletion, list packs and JSON draft editing. All online names, versions and sizes are examples; only a user-triggered JSON list export/import handles actual browser files. No real Workshop calls, `.tmod` parsing or installation is performed yet.

#### Local tModLoader source references

Reviewed the user-provided tree `D:\tModLoader\src\tModLoader\Terraria`:

| Source | Observed behavior | Launcher adaptation |
| --- | --- | --- |
| `ModLoader/UI/ModBrowser/UIModBrowser.Elements.cs` | Search scope, ordering, time period, installed/update state, side, tags and update all | Search row plus filter sheet and update action |
| `ModLoader/UI/ModBrowser/UIModBrowser.cs`, `UIModDownloadItem.cs` | Recursive dependencies, update/download plans, tML version requirements | Confirm dependencies and compatibility before shared transfer task |
| `ModLoader/UI/UIMods.cs`, `UIModItem.cs` | Batch enable/disable, dependency/dependent changes, deletion and loaded config screens | Offline enabled-list edits; explicit cascade confirmation; no reload button |
| `ModLoader/UI/UIModPacks.cs`, `UIModPackItem.cs` | Save enabled list, apply/add, missing downloads, snapshots | Enabled-list pack workflow; binary snapshots remain a separate format |
| `ModLoader/Core/TmodFile.cs`, `BuildProperties.cs`, `LocalMod.cs`, `ModOrganizer.cs` | TMOD header/table, `Info` entry metadata, dependency properties, `enabled.json` | Independent metadata reader and versioned local index; no assembly execution |
| `Social/Steam/WorkshopBrowserModule.cs` | Query catalog, cached install state and download through Steam backend | Separate Workshop provider; desktop Steam integration cannot be assumed available on Android |

Native work must implement the metadata reader, index/config persistence, compatible catalog and transfer provider, staged verification/install and recovery. The referenced classes depend on the game and desktop services; do not copy their initialization or load the full game into the launcher to reuse the menus. Keep downloadable Workshop IDs and package source identity in the native index, rather than trusting an arbitrary matching display name.

### Patches

Separate built-in adapters and optional packages into two counted tabs. Keep import in the shared header and show compatibility rules through its help button. Preserve the selected category and optional enabled state across navigation. Support arrow keys, Home/End and selected-tab focus. The three built-in rows fit in the first screen of the 932 × 430 landscape preview.

Required Android adapters are read-only entries with purpose, shipped version and diagnostic status. They are part of the launch environment and must not have a destructive disable switch.

Optional patch details include identifier, source, version, compatible tML/runtime versions, dependencies, affected files, conflicts and restore behavior. Validate a manifest before changing any files. Core compatibility hooks and external patch packages need an explicit supported contract; arbitrary ZIPs or DLLs are not implicitly compatible. Stage replacements, preserve originals, record patch state and recover interrupted changes.

### Settings

Appearance: system/light/dark theme, motion preference, default plain background, optional local wallpaper and panel opacity. Keep text contrast measurable after compositing; old devices can use static translucent surfaces.

The browser prototype stores validated visual preferences under the versioned `tml-launcher-ui-v1` localStorage key: theme, motion, glass material, panel opacity, plain/soft background and four shortcuts. Corrupt, unknown-version or unavailable storage falls back to defaults. Local image contents, account information, sample installs and activity are excluded from persistence.

Game and runtime: pre-launch validation, supported renderer choices, and advanced launch arguments. Do not add controls whose changes cannot reach the runtime, such as a fictitious JVM/RAM slider for a CoreCLR game.

Input: virtual controls visibility, dedicated layout editor, opacity and system keyboard integration. Storage: paths, network preferences, tasks and scoped cache cleanup. Cache actions must explicitly preserve saves, mods and account sessions. About: build version, dependency notices, diagnostics and log export.

## Visual system

- Default palette: charcoal surfaces, cool white text and ice-blue accents; a separately defined light palette.
- Use a 4/8 spacing rhythm, consistent outline icons, clear labels and Android touch areas of at least 48 dp in the native implementation.
- Restrained glass treatment on navigation, sheets and optional wallpaper overlays. Text and primary controls stay on legible surfaces.
- Keep real branding proportions. Pixel art belongs to optional backgrounds and game assets, not body type or every control.
- Support reduced motion, large text, keyboard navigation and focus restoration after modal dismissal.

## Implementation boundaries

### Motion and interaction prototype

Historical static prototype: `launcher-motion.js` owned animation lifecycle and `launcher-extras.js` owned shortcut/preferences/activity; `mods-workspace.js` held mod operations. These scripts are superseded by the React source modules described above and are not shipped by the active Vite build. The following timing notes describe that earlier reference prototype; Animate UI now owns component presence and shared motion behavior.

CSS handles press and switch feedback; WAAPI handles interruptible page, navigation/tab marker and sheet transitions. No animation dependency was added. Animate only opacity and transforms, with no permanent `will-change`, pointer tracking loop or animated blur. Page changes take 180 ms, marker changes 220 ms, modal entry 220 ms, drawer entry 300 ms and exit 200 ms. Curves are shared ease-out `(.23,1,.32,1)` and drawer `(.32,.72,0,1)` tokens. Buttons keep their 48 px targets during press; only icons scale and button opacity changes.

Keyboard actions are immediate. Both the system reduced-motion preference and the user setting cancel running animations and disable subsequent motion. Rapid navigation cancels prior transitions. Modal backgrounds are inert, focus stays inside, Escape restores the trigger immediately, and exiting sheets are removed from the accessibility tree before their visual exit. Opening another sheet during an exit cannot remove the replacement sheet. Scroll positions are retained independently for each page/category and for same-page updates.

Verification: Edge/Playwright browser checks cover the existing five pages and mod workflows plus motion interruption, reduced motion, keyboard/focus/inert behavior, scroll restoration, shortcut save/cancel/persistence, linked counts, activity filtering/clearing and malformed preference recovery. Layout checks cover 320×568, 390×844, 480×800, 600×320, 740×360 and 932×430. Geometry checks wait for animation completion; active animation properties are checked separately. This verifies desktop-browser behavior and responsive layouts, not actual Android frame rate, native glass rendering or game integration. Validate motion, large text, system insets and performance on real Android devices during native implementation.

Current code provides local resource/tML ZIP import, official release downloading, launch-time Android adapters, local mod/pack management, declarative launch patches, persistent settings and SteamKit/GOG resource transports. Owned-account store transfers and actual game-menu startup still need validation. Prototype sample data is labeled and is not presented as installed data in native mode.

Keep the native `MainActivity` as navigation/lifecycle host. Existing installer and release clients remain feature-owned. Add focused feature controllers for accounts, tasks, mods, patches and settings when their real data flows are implemented. Persist shared preferences/configuration through a versioned schema; do not store state in Views. Evolve existing package metadata compatibly.

Suggested implementation order: navigation and design tokens; existing import/version/launch integration; persisted settings; local mods; supported patch manifests and rollback; Steam and GOG integrations with session and download lifecycle tests.
