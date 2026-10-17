# Implementation plan: mod collection management

Design: [design.md](./design.md). Requirements: [prd.md](./prd.md). Implement runs `dotnet build` only; tests run
in the check phase.

## Ordered checklist

### Phase A — Core formats (pure, no service yet)

1. `ModLocator` refactor: share candidate roots + version-folder pick with the new catalog; `ModLocatorTests` stay green.
2. `ModCatalog` + `InstalledMod` (id lists parsed with the `\` prefix rule). Fixtures via a builder in
   `PzLauncher.Core.Tests/TestSupport/` (staged, library and local copies; first-wins).
3. `ModListSettingsFile` read/modify/write (fav line kept, duplicates, newline preservation, backup, atomic write) +
   name check.
4. `ModCollectionOrderFile` + reconcile rule.
5. `ActiveModsFile` (block rewrite, `maps`/`VERSION` preserved, CRLF kept, missing file, rolling backup, first-write
   `.pzlauncher.orig` never overwritten).
6. `ModLoadOrder.Resolve` and `ModLoadOrder.AutoSort` (cases hand-computed from the Lua algorithms, cycles,
   incompatible, missing).
7. `ModCollectionShareText.Format/Parse` (exact game text; multi-line paste; `!fav!` ignored).

### Phase B — Core service

8. `IModCollectionService` / `ModCollectionService`, DTOs in `CoreJsonContext`, new `ErrorCode`s, DI registration,
   `ZombieBuddyFiles.ModId` + a public id constant for Desktop.
9. Service tests: get / save (create, edit, rename incl. sidecar key move, overwrite) / delete / apply; external edit
   between get and save survives (R9); `IsActive` / `ActiveMatchesCollection`.

### Phase C — Desktop

10. `defaultModCollections` preference (`UserPreferences`, `JsonPreferenceStore`, contract tests) +
    `IModCollectionDefaultsService`.
11. `IDialogService` additions (text prompt, confirm, info) + `IClipboardService`; Avalonia implementations; fakes in
    `PzLauncher.Desktop.Tests/TestSupport/Fakes.cs`.
12. `ShellPage.Mods`, `MainViewModel` wiring, tab in `MainView`, `ModsViewModel` + `ModsSection` + `ModsView` (nav
    sidebar), `InstalledModsViewModel`/view (columns, search, filters, add-to-collection), `ModCollectionsViewModel`/
    view (collection list column, rows, search-add, drag + move up/down); writes disabled while the game runs; design
    data for both sections.
12a. Shared apply flow helper used by the page and by launch: R6a save prompt (Save / Skip / Cancel), then R6b drop
     notice (继续 / 取消) from a `ModLoadOrder.Resolve` preview, then `ApplyAsync`.
13. `HomeViewModel.LaunchAsync`: apply default (via 12a; Cancel aborts the launch) → ZombieBuddy remove-or-today → Zabric preload unless removing → launch.
14. Localization: every new key in all 10 `Lang/UiText*.resx`; `ErrorMessages` for the new codes.
15. Spec updates (phase 3.3): `desktop/preferences.md` contract row; `core/directory-structure.md` if the new
    `Mod/Collections/` folder needs a mention.

## Validation

```bash
dotnet build PzLauncher.slnx
dotnet test PzLauncher.slnx
dotnet format PzLauncher.slnx --include <changed .cs files>
```

Manual (check phase, against a copy of the user's cache folder via `-cachedir=`): AC1–AC8 in the running app, and the
game's Mods screen showing launcher-written collections and the applied enabled list.

## Risky files / rollback points

- `PzLauncher.Core/Mod/ModLocator.cs` (Zabric depends on it) — commit point after step 1.
- `PzLauncher.Desktop/ViewModels/Home/HomeViewModel.cs` launch flow — step 13 isolated so it can be reverted alone.
- `PzLauncher.Desktop/Services/JsonPreferenceStore.cs` persisted contract — additive field only.

## Before `task.py start`

- `implement.jsonl` / `check.jsonl` curated (done with this plan).
- User approved the final planning summary.
