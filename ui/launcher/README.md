# tML Pocket Launcher UI

The launcher interface uses React 19, TypeScript, Vite and
[Animate UI 2.4.5](https://animate-ui.com/docs). Gradle packages it offline into the
Android APK. A restricted native bridge connects device state and operations;
ordinary browser sessions retain the interaction preview. Game rendering runs
in the separate native SDL/CoreCLR process. See [Android behavior and limits](../../docs/android-launcher.md).

```sh
npm ci
npm run dev
```

Open `http://127.0.0.1:8769/preview.html` for the phone preview, or `/` for a
responsive workspace. `?page=mods&tab=browse` opens the mod download preview.
The preview includes landscape and portrait phone sizes. Add `&orientation=portrait`
to a preview URL to open the bottom navigation directly.

Settings > Appearance > Launcher orientation defaults to landscape and saves a
landscape/portrait preference. The phone preview rotates its existing iframe when
this setting or the preview toolbar changes, preserving the current page/session.
An explicit preview orientation URL takes precedence on entry; otherwise the saved
preference is used. Regular browser windows remain responsive to their actual
viewport. Inside Android, the same preference rotates the launcher Activity in
place and leaves the game in landscape.

```sh
npm run check
npm run build
```

## Structure

- `src/components/`: shared Animate UI controls and accessible modal host.
- `src/features/`: home, games, installed/downloadable mods, packs, patches and settings.
- `src/domain/`: typed metadata, sample catalog, dependency resolution and pack validation.
- `src/state/`: shared session state and validated visual preferences.
- `src/native/`: typed Android bridge transport, models and background preparation.
- `src/features/native/`: device-backed panels sharing the approved controls/styles.
- `src/styles/`: launcher layout, glass palette and component adaptations.
- `public/`: game identity, icon sprite, phone preview and dependency notice.

Animate UI imports use the specific primitive subpaths. Dialog handles sheets via
`data-side`, Tabs handles category selection/panels, Switch handles checked state,
Progress handles task indication. BottomNavigation supplies the shared selection
spring, with a neutral rounded selection capsule in portrait and on the landscape
rail. All five destinations keep their icons above always-visible labels; the bar
itself has no outer capsule or border.
In Android's download workspace, this navigation switches to Games, Mods and
Mod packs. The resource browser uses broad rounded cards and a right-hand filter
column; narrow screens fold filters and use the bottom category navigation.
Games reads real paginated release metadata, Mods uses existing Workshop
search/details/download APIs, and Mod packs manages local JSON presets. The
regular browser preview remains separate from native data. The resource-browser
test supplies bridge fixtures and checks ten responsive sizes, search/paging,
retries, mod queries and preset operations.
Native local management is separate: Games has Installed and Add game tabs;
Mods has Installed and Mod packs tabs. The game page avoids mirrored status cards
and account sections. The mod header opens one Add mod chooser, or JSON import
when viewing packs. Workshop uses the dedicated download workspace, including
redirects from legacy browse shortcuts. `features/native/games.tsx` owns the game
page and `NativeAcquireAction` shares its acquisition controls with the mod chooser.
Select, Slider and Tooltip provide the filter/settings popups,
opacity adjustment and icon hints. The launch dock has a single version selector,
a standalone primary action and a compact 32 px text entry for logs. Environment
checks remain on Home; portrait logs remain accessible in Settings > About.
Button retains its official
press spring. The primary prepare/launch action uses the official RippleButton
and RippleButtonRipples, with a clipped, pointer-origin wave beneath its label.
`AnimateUIProvider` centralizes pacing and reduced
motion. Primitive implementations are consumed from the package. The Menu, LayoutTemplate and
Settings icon components in `src/components/icons/` come from Animate UI's official
Lucide registry; the Wallpaper SVG comes from Lucide. Their upstream source URLs
and license notices are retained.

Settings categories occupy a dedicated 56 px row below the compact 48 px header.
Full labels, 16 px button padding and 8 px gaps keep the categories comfortably
spaced; narrow screens scroll horizontally. The tablist supports arrow/Home/End
keyboard selection. Content uses the official
[Page Transition registry source](https://animate-ui.com/docs/primitives/animate-ui/page-transition),
adapted to controlled page keys without installing a URL router. Main pages slide;
settings categories push left/right according to their order. Header and launch
controls remain outside the snapshot. Keyboard/reduced-motion navigation and
browsers without view transitions update immediately. Pending route commits make
the old page and header actions inert to prevent stale-page clicks.

Visual preferences retain the existing `tml-launcher-ui-v1` schema.
The optional tModLoader theme in Settings → Appearance applies an offline classic
indigo skin inspired by tModLoader's `UICommon` and `UIPanel` menu colors, with an
original pixel wordmark. It temporarily replaces liquid glass with opaque panels
without changing the saved glass or background preferences.

Sample installations, tasks, account demonstrations and activity remain session-only.
In a regular browser, local mod-pack JSON import/export uses real browser files;
other workflows are illustrative. Android mode uses actual installations, TMOD
metadata, mod enable lists/packs, settings and tasks and never enables sample data.
SteamKit2 authentication and GOG official authorization are connected in Android mode;
Steam-only authenticated UGC transfers remain unavailable. See the Android guide for
live-account verification limits.

See [the UI framework](../../docs/ui-framework.md) for native integration boundaries.
Animate UI's MIT + Commons Clause notice is included in
[`public/animate-ui-license.txt`](public/animate-ui-license.txt).

The control backdrop in `src/components/liquid-glass-surface.tsx` is a WebGL1
lens. It rebuilds the launcher wallpaper (tint gradient or custom image) on a 2D
canvas, derives a rounded-box height field, refracts the backdrop through it with
a thin chromatic fringe and directional rim highlight, then composites the dock
and capsule tints. Browsers without WebGL keep the CSS blur fallback. Text and
ripple layers stay outside the lens, and the glass switch removes the lens and
its observers. The technique is reimplemented from
[Rxing's Liquid Glass WebGL demo](https://github.com/rxing365/html-liquid-glass-effect-webgl).
