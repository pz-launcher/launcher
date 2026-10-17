# Design: mod collection management

Requirements and decisions: [prd.md](./prd.md). Game behaviour quoted there is from
`<game>/media/lua/client/OptionScreens/ModSelector/*.lua` (build 42.21.0).

## Boundaries

Everything a web UI would also need lives in Core (`core/service-api.md`): file formats, mod enumeration, ordering,
apply. Desktop owns the page, dialogs, clipboard, the default-collection preference and the launch orchestration.

```
Desktop                                   Core (PzLauncher.Core/Mod/Collections/)
ModCollectionsViewModel ──┐               IModCollectionService ── ModCollectionService
HomeViewModel.LaunchAsync ┼──────────────►   ├─ ModCatalog            (installed mods)
IModCollectionDefaults ◄──┘                  ├─ ModListSettingsFile   (Lua/pz_modlist_settings.cfg)
  (preferences.json)                         ├─ ModCollectionOrderFile(launcher order sidecar)
                                             ├─ ActiveModsFile        (mods/default.txt)
                                             ├─ ModLoadOrder          (auto sort, apply resolution) — public static
                                             └─ ModCollectionShareText(format/parse)            — public static
```

## Core

### Service — `IModCollectionService`

Each call takes the install root and the install's extra game args (for `-cachedir=`), resolved through
`ModLocator.CacheFolder` + `PzLaunchFiles.ResolveGameFolder`, the way `ZabricService.EnsurePreloadAsync` does. A cache
folder that doesn't resolve is an error, not an empty list.

| Method | Returns | Notes |
|---|---|---|
| `GetAsync(installRoot, extraGameArgs)` | `ModCollectionState` | collections (name, ordered ids, `IsActive`), installed mods, active ids (`default.txt` order, empty when missing), `ActiveMatchesCollection` |
| `SaveAsync(SaveModCollectionRequest)` | `Result` | `{InstallRoot, ExtraGameArgs, OriginalName?, Name, ModIds}`: create, edit, reorder, rename (`OriginalName` ≠ `Name`), import/overwrite |
| `DeleteAsync(installRoot, extraGameArgs, name)` | `Result` | |
| `ApplyAsync(installRoot, extraGameArgs, name)` | `ApplyModCollectionResult` | `{EnabledIds, MissingIds, SkippedIncompatible: [{Id, ConflictsWith}]}`; same shape `ModLoadOrder.Resolve` returns |

DTOs are immutable records registered in `CoreJsonContext` with round-trip tests. `InstalledMod`:
`{Id, Name, WorkshopId?, Source, Require, LoadAfter, LoadBefore, Incompatible}` (lists of bare ids; `Source`:
`SteamWorkshop` / `StagedWorkshop` / `Local`).

`ModLoadOrder.AutoSort(ids, installedMods)` and `ModCollectionShareText.Format/Parse` are pure public statics: no I/O,
nothing for an endpoint to add.

A `SemaphoreSlim` gate serialises the service's writes (same reason as `ZabricService._gate`).

### Installed mods — `ModCatalog`

- Roots and order exactly as `ModLocator.Candidates` (staged Workshop items → Steam Workshop library items → `<cache>/mods`);
  first copy of an id wins. Refactor `ModLocator` so the candidate roots and the version-folder pick
  are shared, not copied. The catalog reads the game's B42 layout (`ReadGameModInfoAsync`): the highest numeric
  version folder's `mod.info`, else `common/mod.info`; neither means not a B42 mod. Zabric/ZombieBuddy keep `ReadAsync`.
- The Workshop id is the item folder name for Workshop roots, `null` for `<cache>/mods`.
- Id lists: split on `,`, trim, take the part after the last `\` (`\damnlib` → `damnlib`), drop empties.
- An unreadable folder is skipped and logged at debug, like `ModLocator.LogSkipped`.

### Collections file — `ModListSettingsFile`

- Read: UTF-8; split lines on `\n`, strip a trailing `\r`. Line 0 starting with `!fav!` is favourites (kept, never
  parsed further). Other lines: split at the first `:`; empty name ignored; ids split on `;`, empties dropped.
  Duplicate names: the last line wins (the game's table assignment).
- Write is read-modify-write of the **current** file: replace the target line in place (first line with that name;
  any later duplicates removed), append a new one at the end, or drop it. Every other line is kept byte-for-byte.
  Newline: the file's own (`\n` when new), UTF-8 without BOM, atomic replace (temp + move, as `AtomicJsonFile`).
- Before a write, the current file is copied to `pz_modlist_settings.cfg.pzlauncher.bak` (one rolling backup).
- Line format written: `name:id1;id2;...;` — identical to the game's `saveModDataToFile`.
- Name rule (approximating Java `SandboxOptions.isValidPresetName`): non-blank after trim; no `:`; no control chars;
  none of `\ / : * ? " < > |`. Exposed as a public static check so the UI validates before calling.

### Launcher order — `ModCollectionOrderFile`

- `<cache>/Lua/pz_modlist_settings.pzlauncher.json`: `{ "collections": { "<name>": ["id", ...] } }`, written with
  `AtomicJsonFile` and an internal source-generated context. It travels with the cache folder.
- Written on every save/rename/delete/import (rename moves the key, delete removes it).
- Reconcile on read, per collection: ids from the sidecar that are still in the cfg line, in sidecar order, then cfg ids
  not in the sidecar, in cfg order. No sidecar entry → cfg order. An unreadable sidecar is ignored (logged).

### Active list — `ActiveModsFile`

- Parse `mods { }` entries `mod = <id>,`; everything outside that block (the `VERSION` line, `maps { }`, blank lines) is
  kept as text. Regenerate the block in the game's shape (`    mod = id,`), file newline preserved (`\r\n` today on
  Windows; `Environment.NewLine` for a new file). Missing file → written from scratch with `VERSION = 1,` and an empty
  `maps` block.
- Rolling backup `default.txt.pzlauncher.bak` before each write; atomic replace. When `default.txt.pzlauncher.orig`
  doesn't exist yet and `default.txt` does, copy it there first (never overwritten afterwards).

### Apply resolution — `ModLoadOrder.Resolve`

Mirrors `choosePreset` + `forceActivateMods` + `correctAndSaveModOrder`, with recursion where the game's result
depends on hash order:

1. Walk collection ids in order; skip ids not installed (→ `MissingIds`).
2. Before emitting a mod, emit its installed `require` ids depth-first (cycle-safe via a visiting set).
3. A mod incompatible with an already emitted mod is not emitted (→ `SkippedIncompatible`, with the emitted mod it conflicts with); a skipped dependency
   does not block its dependent (the game doesn't either).
4. Each id emitted once.

A collection `IsActive` when its `Resolve` result enables at least one mod and its id set equals the installed part
of the `default.txt` id set (order ignored).
`ActiveMatchesCollection` = the active set is empty or some collection `IsActive`; when false, Desktop shows the R6a
prompt before applying.

### Auto sort — `ModLoadOrder.AutoSort`

Port of `ModLoadOrderPanel.autoSort` step by step (ordinal sort by id, then the `temp` / insert-around logic with
`loadAfter`+`require` before and `loadBefore` after). Ids not installed keep their relative order and go last.
Tests pin it against hand-computed outputs of the Lua algorithm.

### ZombieBuddy

- Add `ZombieBuddyFiles.ModId = "ZombieBuddy"` (the Workshop mod's `mod.info` id).
- No Core change to the remove flow: `IZombieBuddyService.RemoveAsync` already deletes the files and every agent arg.

### Errors

New `ErrorCode`s with wire names and `ErrorMessages` texts: cache folder unresolved, collection name invalid,
collection not found, mod list file unreadable/unwritable (IO and access errors at the boundary, per
`core/error-handling.md`). Missing mods on apply are data, not an error.

## Desktop

### Default collection preference

- New `preferences.json` field `defaultModCollections`: object, absolute install root → collection name (string,
  non-empty). Invalid entries dropped, the rest kept (same rules as `zombieBuddyChannels`). Contract row added to
  `desktop/preferences.md`.
- `IModCollectionDefaultsService` (`Get` / `Save` / `Clear`), modelled on `ZombieBuddyChannelService`.
- Rename of the marked collection updates the entry; delete clears it. The mark is per install root (collections are
  per cache folder; two installs sharing a cache folder may mark different defaults).

### Page — `ShellPage.Mods`

- New top-level tab "模组" between Installs and Settings; `ModsViewModel` (singleton, owns `ModsSection { Mods,
  Collections }` and the shared `ModCollectionState`) + `ModsView` with a left nav sidebar of the two sections, as
  `SettingsViewModel` / `SettingsSection` / `SettingsView` do. Each section is its own VM + view
  (`InstalledModsViewModel`, `ModCollectionsViewModel`) fed from the same loaded state, so both refresh together.
- 模组管理: one list row per installed mod (name, id, Workshop id, source, enabled badge, collection names), search
  box, filter radios bound to an enum through `EnumMatchConverter`, multi-select checkboxes + "添加到合集…" (pick a
  collection, then `SaveAsync` with the ids appended). No `default.txt` writes.
- 合集管理: a collections list column (rows with mod count, default and in-use markers; "新建 / 从当前启用新建 /
  导入" actions) next to the selected collection: header (name, default + in-use markers,
  actions: 应用为启用列表, 设为默认/取消默认, 分享, 重命名, 删除, 自动排序), mod rows (name, id, missing badge, remove
  button as a sibling of the row), and an "添加 mod" search box listing installed mods not yet in the collection.
- Drag reorder: pointer drag on a row handle in the view's code-behind computes the target index and calls
  `ModCollectionsViewModel.Move(from, to)`; the VM saves the new order (debounced, as `InstallOverridesService`, so a
  drag doesn't write per step). Keyboard alternative: move up / move down on the focused row.
- Loads on tab activation and on `InstallsViewModel.SelectedInstall` change; a "刷新" action re-reads files.
- Every write command's `CanExecute` includes `!GameRunState.IsGameRunning` and a game install being selected.
- New `IDialogService` methods: text prompt (name, import text, with validation message), confirm (overwrite/delete),
  info (apply result: missing/skipped ids). New `IClipboardService` (Avalonia `TopLevel.Clipboard`) for share.

### Launch — `HomeViewModel.LaunchAsync`

New order:

1. `default = defaults.Get(root)`; when set → the shared apply flow (below). Cancel → no launch. Failure (including
   "not found") → show error, continue with `removeZombieBuddy = false`. Success →
   `removeZombieBuddy = !EnabledIds.Contains(ZombieBuddy)`.
2. `PrepareZombieBuddyAsync(root, removeZombieBuddy)`: when `removeZombieBuddy`, open the setup dialog in
   `ZombieBuddySetupMode.Remove` if `status.HasLeftovers`, else nothing; otherwise today's logic. Cancel → no launch.
3. Zabric preload only when `!removeZombieBuddy`.
4. Launch as today.

Shared apply flow (page button and launch use the same Desktop helper, e.g. `ModCollectionApplier`):
`GetAsync` → when `!ActiveMatchesCollection`, text prompt prefilled with the localized "原启用列表" and Save / Skip /
Cancel (Save → `SaveAsync` with the active ids in `default.txt` order, clash → confirm overwrite) → preview with
`ModLoadOrder.Resolve(collection ids, state.InstalledMods)`; when it drops or skips anything, one confirm dialog
listing missing ids and each skipped id with its conflicting mod (继续 / 取消) → `ApplyAsync`. Any Cancel stops the
flow without writing. Same flow on the page and at launch. `ApplyModCollectionResult.SkippedIncompatible` carries
`{Id, ConflictsWith}` so the preview and the result agree.

`ZombieBuddyFiles.ModId` is internal; expose the id through a public constant (e.g. on `ZombieBuddyStatus` or a public
`ZombieBuddyMod.Id`) rather than duplicating the string in Desktop.

## Compatibility and risks

- Game files are only ever rewritten in the game's own format; the launcher's extra data sits in its own sidecar and
  backups, which the game ignores. Order inside a cfg line is ignored by the game, so writing an order is harmless.
- A game not started by the launcher can't be detected; re-read-before-write limits damage to the one collection being
  edited. Rolling backups allow manual recovery.
- `isValidPresetName` is approximated; a name the launcher accepts but the game rejects still loads in-game (the check
  only guards the game's save dialog).
- Uninstall on launch (D5) is destructive but reversible: the next launch with ZombieBuddy in the default collection
  re-initializes through the existing flow.

## Rollback

Feature is additive: removing the tab, the launch hook and the preference field restores today's behaviour; the sidecar
and `.pzlauncher.bak` files can be left or deleted by hand.
