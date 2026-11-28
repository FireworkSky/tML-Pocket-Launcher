# Code conventions

## Names and file ownership

- Kotlin packages follow their directory under `app.com.tmlpocket`. Keep feature code under `feature/<feature>` and shared runtime/platform adapters under `core`.
- Kotlin classes use PascalCase, methods/properties use camelCase, enum constants and immutable compile-time constants use UPPER_SNAKE_CASE.
- C# source and build settings live under `managed/`; Android source sets live under `android/app/src/`. Managed projects and namespaces use `MobileLauncher.<Component>`. Use one main public class per file and file-scoped namespaces.
- Use names that describe behavior: `importPackage`, `loadReleases`, `prepareLauncher`. Avoid template names, numbered variants, generic helpers, and parallel implementations.

## Responsibilities

- Activities render UI and connect Android lifecycle callbacks. Background controllers hold application context rather than Activity/View references.
- File/network operations run off the main thread. Every long operation accepts cancellation and exposes progress without depending on a widget.
- Keep state models separate from parsing, transport and UI rendering. Dependency flow follows [architecture.md](architecture.md).
- Add abstractions when they remove a real coupling; do not create empty layers or pass-through repositories.
- Exceptions report an actionable cause. Catch them where recovery or user feedback is possible; do not silently turn invalid installations into a successful state.
- Keep imported packages, launcher files and user data separate. Validate untrusted archive paths before writes; transaction cleanup must not follow symbolic links.

## Formatting

`.editorconfig` defines UTF-8, LF and indentation; `.gitattributes` keeps repository line endings consistent. Use four spaces for Kotlin/C#/C++/PowerShell and two for JSON/XML project files. Preserve upstream formatting in vendored code. Do not mass-format third-party sources.

Kotlin follows official Kotlin formatting. C# uses braces, file-scoped namespaces and deterministic builds. Comments explain compatibility contracts and non-obvious constraints; they do not narrate each line.

The isolated Web UI in `ui/launcher` uses strict TypeScript, React components and
two-space Prettier formatting. Keep shared primitive adapters in `components`,
page/workflow components in `features`, pure metadata rules in `domain` and
session/persistence logic in `state`. Prefer specific Animate UI primitive imports
and controlled state. Run `npm run check`, `npm run format:check` and `npm run build`
from that workspace for Web-only changes.

## Review and verification

Run `scripts/checks/verify.ps1` before submitting changes. It checks package/namespace paths, dependency boundaries, managed formatting and compilation. Fix managed whitespace with `dotnet format whitespace MobileLauncher.slnx`. Run APK assembly and package inspection for build/layout changes. Native and gameplay behavior require an arm64 Android device and fresh logs.

Keep structural refactors and behavior changes reviewable. Update references, manifest names, JNI symbols, scripts and documentation in the same change as a file move. Never commit credentials, machine paths, generated game assets, downloads, logs or APKs.
