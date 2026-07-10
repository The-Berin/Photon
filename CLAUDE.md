# Photon — agent guide

Windows-only photo/video sorter. C# WinForms, .NET 9. `src/Photon.Core` = all logic
(platform-neutral, tested), `src/Photon` = UI, `tests/Photon.Tests` = xunit (runs on any OS).

## Versioning — REQUIRED

Use semantic versioning (SemVer, Major.Minor.Patch) on every release:

- **Patch** (1.0.0 → 1.0.1): bug fixes, no behavior additions
- **Minor** (1.0.1 → 1.1.0): new backward-compatible features
- **Major** (1.x → 2.0.0): breaking changes (settings/journal format, CLI, file layout)
- Optional prerelease tags (`-alpha.1`, `-beta.2`) for testing builds

Keep in sync on every release: `<Version>` in both csproj files, the git tag (`v1.0.1`),
and the GitHub Release name. Never re-release the same version with different binaries.

## Conventions

- Every destructive file operation must be journaled via `IJournalService` (undo from History).
- Photon.Core stays platform-neutral — Windows APIs (WMI, COM, P/Invoke) live in `src/Photon` only.
- Theming is native-first: `Application.SetColorMode` at startup + `ThemeService.FixGaps(form)`
  for documented .NET 9 dark-mode gaps only. Never hand-recolor whole control trees.
- Cross-compiles on macOS/Linux via `EnableWindowsTargeting` (build/test only; UI needs Windows).
- CI: push to main → portable exe artifact; tag `v*` → GitHub Release (portable + Inno installer).
