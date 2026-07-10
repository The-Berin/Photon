# Photon

**The fastest way to sort a lifetime of photos.**

[![Build](https://github.com/The-Berin/Photon/actions/workflows/build.yml/badge.svg)](https://github.com/The-Berin/Photon/actions/workflows/build.yml)

Point Photon at a folder of twenty years of camera dumps, phone backups, and SD-card chaos, and it files every picture and video into a clean date-structured library — copied or moved, previewed before it touches anything, and fully undoable afterwards. It is a native Windows utility in the classic pro-tool mold: fast, dense with options, and honest about exactly what it is going to do to your files before it does it.

<!-- SCREENSHOTS: main window, batch renamer, duplicate finder, history/undo -->

## Features

### Sorting

- **Date-structured sorting** — file everything into `Year`, `Year\Month`, or `Year\Month\Day` trees. Month folders as numbers (`03`) or names (`March`), optional `HH-MM` time subfolders under the day for burst-heavy shoots, and optional camera `Make\Model` grouping beneath the date folders.
- **EXIF-first date intelligence** — the date that drives sorting comes from real photo metadata, not filesystem timestamps, with four selectable policies: *EXIF then file date*, *EXIF only*, *file date then EXIF*, and *file date only*. Files with no resolvable date land in a configurable `Unknown Date` folder instead of being guessed.
- **Duplicate handling** — when a destination name already exists, choose *rename* (`_1`, `_2`, …), *skip*, or *overwrite* (the displaced file is preserved for undo). Optionally hash file contents to catch exact duplicates regardless of name and divert them into a `Duplicates` folder instead of cluttering the library.
- **Copy or move, never half of either** — a pre-flight free-space check (with a safety margin) refuses to start a sort the destination volume cannot hold, so you never come back to a half-finished library and a full disk.
- **Ghost "preview sort"** — build a mirror of the destination tree out of `.lnk` shortcuts showing where every file *would* go. Browse the future library in Explorer, then run the real sort — or don't.

### Safety

- **Full undo journal** — every sort, batch rename, flatten, and duplicate move is journaled with enough detail to reverse it exactly: moves are moved back, copies deleted, overwritten files restored from backup, and created-then-emptied folders pruned. All of it is one click away in the History window.

### Tools

- **Batch-rename control center** — a full rename pipeline: token pattern, chained find/replace steps (regex supported), character-range removal, text insertion, whitespace trimming/collapsing, diacritic removal, character stripping, case transforms for name and extension, prefix/suffix, and configurable counters (start, step, padding, per-folder restart) — all with a live preview grid and journaled execution. Pattern tokens (dates honor the selected date-source policy; unknown tokens are left verbatim):

  | Token | Meaning |
  |---|---|
  | `{name}` | Original file name (without extension) |
  | `{ext}` | Original file extension |
  | `{counter}` | Sequential counter (start, step, padding, per-folder restart configurable) |
  | `{yyyy}` / `{yy}` | 4-digit / 2-digit year |
  | `{MM}` | 2-digit month number |
  | `{MMM}` / `{MMMM}` | Abbreviated (`Mar`) / full (`March`) month name |
  | `{dd}` | 2-digit day of month |
  | `{ddd}` | Abbreviated day-of-week name (`Mon`) |
  | `{HH}` / `{mm}` / `{ss}` | Hour (24 h) / minute / second |
  | `{date}` | Full date stamp |
  | `{time}` | Full time stamp |
  | `{camera}` | Camera make and model |
  | `{make}` / `{model}` | Camera make / camera model |
  | `{width}` / `{height}` | Pixel dimensions |
  | `{mp}` | Megapixels |
  | `{size}` / `{sizeMB}` | File size (auto unit / MB) |
  | `{parent}` / `{parent2}` | Parent / grandparent folder name |
  | `{hash8}` | First 8 hex characters of the content hash |
  | `{guid}` | A freshly generated GUID |
  | `{rand4}` / `{rand8}` | Random 4- / 8-character string |

- **Duplicate finder** — scan any set of folders with four compare modes (*size only*, *quick hash* of first+last 64 KB, *full SHA-256*, *name + size*), pick which copy survives (*oldest*, *newest*, *shortest path*, *longest path*, *first alphabetical*), and resolve by report, move-to-folder, or journaled soft-delete — so even deduplication is undoable.
- **Folder flatten** — collapse a nested folder maze into one level, with conflict policies (append number, append folder name, skip), optional media-only mode, and empty-folder cleanup. Journaled.
- **Quick folder scan** — a fast reconnaissance pass over any folder or drive: file/folder counts, picture/video/other breakdown, per-extension totals, largest file, date range, depth — and an estimated sort time before you commit to one.
- **Drive inspector** — volume vitals plus physical-disk health (SSD/HDD, bus type, health status via Windows storage WMI) and an on-demand sequential read/write speed test, so you know what a sort is up against.

### Quality of life

- **Keep-awake** during long runs — no more waking up to a sort that stopped at 40% because Windows went to sleep.
- **When-done actions** — open the output folder, close the app, sleep, or shut down when the sort finishes.
- **Dark mode** — light, dark, or follow-system.
- **CSV + log export** — a per-run log file and a CSV summary of every operation, for the record.

## Download

- **Portable (latest `main` build):** grab the `Photon-portable-win-x64` artifact from the most recent [Build workflow run](https://github.com/The-Berin/Photon/actions/workflows/build.yml) — a single self-contained `Photon.exe`, no install, no .NET runtime needed.
- **Releases:** the [Releases page](https://github.com/The-Berin/Photon/releases) has the installer (`PhotonSetup-<version>.exe`, per-user, no admin prompt) and the portable exe for every tagged version.

Windows 10/11, x64.

## Building

```
dotnet build Photon.sln
dotnet test Photon.sln
```

The WinForms project sets `EnableWindowsTargeting`, so the whole solution *compiles* on macOS and Linux too — only running the app requires Windows. The test suite targets the platform-neutral `Photon.Core` and runs anywhere.

To produce the single-file portable exe:

```
dotnet publish src/Photon/Photon.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## Project layout

```
Photon/
├── Photon.sln
├── src/
│   ├── Photon.Core/          # platform-neutral engine: scanning, metadata, planning,
│   │                         # execution, journaling/undo, rename/duplicate/flatten tools
│   └── Photon/               # WinForms UI (net9.0-windows), Windows-only integrations
├── tests/
│   └── Photon.Tests/         # xunit suite for Photon.Core (runs on any OS)
├── installer/
│   └── photon.iss            # Inno Setup 6 script
└── .github/
    └── workflows/build.yml   # CI: test, portable artifact, tagged releases
```

## Tech notes

- .NET 9, C#; UI is classic WinForms (`net9.0-windows`) — native controls, no custom-drawn chrome.
- [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet) reads EXIF and video-container metadata.
- `System.Management` (WMI) powers the drive inspector's health readout.
- All disk work runs off the UI thread with progress reporting and prompt cancellation; every destructive or creating operation is journaled for undo.

## License

[MIT](LICENSE) © 2026 The-Berin

## Lineage

Photon is a from-scratch rebuild of a lost in-house tool called **Picture Sorter** — same job, new bones.
