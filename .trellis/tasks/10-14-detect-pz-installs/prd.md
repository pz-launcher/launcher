# Detect Project Zomboid installs

## Context

PzLauncher is a PCL2-style launcher for Project Zomboid that will manage both
the game client and dedicated servers. Targets: Windows first, then macOS and
Linux desktop (Avalonia), and later a headless Linux server host with a web UI.

This is the first feature task (week 1). It's small on purpose: it sets up
the solution structure and runs one read-only feature through every layer
(Core service → ViewModel → View → tests), following the conventions in
`.trellis/spec/`.

## Goal

When the launcher starts, it shows every Project Zomboid **game** and
**dedicated server** install it can find through Steam. The user can also
point it at a folder by hand.

## Requirements

1. The solution is split into `PzLauncher.Core`, `PzLauncher.Desktop` (app),
   `PzLauncher.Core.Tests`, and `PzLauncher.Desktop.Tests`, as in
   `spec/core/directory-structure.md`.
2. Core finds the Steam root(s) for the current OS, reads every Steam library
   listed in `libraryfolders.vdf`, and reports installs of app `108600` (game)
   and `380870` (dedicated server) that have an app manifest and an existing
   install folder.
3. Each install reports: kind, absolute root path, source (Steam library /
   manual), and for Steam installs the build id and beta branch (null when on
   the default branch).
4. Problems that don't stop the scan (Steam not installed, unreadable or
   malformed library/manifest file, manifest whose folder is missing) are
   returned as warnings, and the scan still returns everything it found.
5. Given a folder path, Core identifies it as a game install, a server
   install, or neither, and returns a typed error for invalid, missing,
   unreadable, or non-PZ paths.
6. The app's main window shows an Installs page that scans automatically on
   startup and has: a list of installs, a **Rescan** button, an **Add
   folder…** button (folder picker → inspect → add to the list), a busy
   indicator, an empty state, and non-blocking warning/error text built from
   error codes.
7. Manually added folders show up in the list for the current session only.

## Constraints

- Core stays portable and AOT-compatible. Windows-only APIs (registry) only
  go behind `IPlatformEnvironment` with OS guards.
- Detection is strictly read-only. It never writes into Steam or game folders.
- No new third-party packages except `Microsoft.Extensions.*`, xUnit v3
  test packages, and `ValveKeyValue` (VDF/ACF parsing, Core only; approved).

## Acceptance criteria

- [x] `dotnet build PzLauncher.slnx -warnaserror` passes.
- [x] `dotnet test PzLauncher.slnx` passes.
- [x] Core tests cover: VDF parsing (incl. escaped backslash paths, nested
      blocks, malformed input), scan with multiple libraries, game + server in
      different libraries, beta branch read, Steam missing → warning,
      unreadable/malformed library file → warning + other libraries still
      scanned, manifest with missing folder → warning, duplicate library
      paths de-duplicated, and every `InspectAsync` error code.
- [x] ViewModel tests cover: busy → loaded, busy → empty, warnings shown,
      add-folder success, add-folder error text, folder picker cancelled → no
      change.
- [x] `PzLauncher.Core` has no reference to Avalonia, and no ViewModel
      references `Avalonia.*`.
- [x] Manual smoke (Windows, PZ installed via Steam): launch the app and the
      game (and server, if installed) appear with the correct path and branch.
      Add the game folder by hand and it shows as Game / Manual.

## Out of scope

- Persisting manual paths or any settings (next task: settings store).
- Installing or updating via SteamCMD, launching the game, starting/stopping
  the server.
- Navigation rail / multiple pages, theming, localization beyond English.
- Non-Steam installs (GOG etc.) unless found through a manual path.
- `PzLauncher.Server` / web UI.
