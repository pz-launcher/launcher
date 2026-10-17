# Mod collection management

## Goal

Bring the game's in-menu mod preset ("collection", in-game CN text: 模组预设) management into the launcher, so a player can
create, edit, order, share and apply collections without opening the game's Mods screen, and have one collection
applied automatically whenever the game is launched from the launcher.

User request (verbatim): 将游戏内的mod合集管理功能添加到启动器中。支持分享，分享格式与游戏内格式一致。支持将某一合集快速设为预设合集。支持mod排序。

## Background (game build 42.21.0 and this repo)

All game files live under the selected install's cache folder (`ModLocator.CacheFolder`,
`PzLauncher.Core/Mod/ModLocator.cs:13`: last `-cachedir=` game arg, else `<home>/Zomboid`).

- **Collections** — `Lua/pz_modlist_settings.cfg`, one line per collection: `name:id1;id2;...;` (trailing `;`). A first
  line starting with `!fav!` holds favourites in the same shape. Parsing splits at the first `:`; an empty name is
  ignored. The game keeps a collection as an id→workshopId table, so every in-game save rewrites all lines (and the ids
  inside them) in hash order. (`media/lua/client/OptionScreens/ModSelector/ModSelectorModel.lua`
  `loadModDataFromFile` / `saveModDataToFile`.)
- **Share text** — exactly one cfg line, `name:id1;id2;...;` plus a newline, copied to the clipboard; in-game import
  pastes such a line and adds/overwrites the collection by name. (`getPresetShareText` / `addSharedPreset`.) Names are
  validated by `SandboxOptions.isValidPresetName` (Java; invalid filename characters).
- **Active mod list** — `mods/default.txt`: `VERSION = 1,`, `mods { mod = <id>, ... }`, `maps { ... }`. Its order is
  the load order. It persists across launches; a loaded save uses its own list (`getSaveInfo(folder).activeMods`).
- **Choosing a collection in-game** enables its available mods plus their `require` deps, skips mods incompatible with
  an enabled one, disables all others, puts each mod's `require` ids before it (`correctAndSaveModOrder`) and lists the
  missing mods (`ModListPresets.lua` `choosePreset`).
- **Auto sort** (`ModLoadOrderPanel.lua` `autoSort`): id-alphabetical, then each mod's `loadAfter` + `require` ids
  before it and its `loadBefore` ids after it.
- **No default-collection concept** in the game: the cfg holds only `!fav!` and collection lines, `options.ini` has no
  mod/preset key, and the preset combo always opens on "-- Choose preset --".
- **mod.info** — `require=` / `loadAfter=` / `loadBefore=` / `incompatible=` are comma lists whose ids may carry a
  `\` prefix (e.g. `require=\damnlib`).
- **ZombieBuddy** — the launcher installs it by adding its agent arg to the install's json vmArgs
  (`ZombieBuddyAgentArgs`, `PzLauncher.Core/Mod/ZombieBuddy/ZombieBuddyAgentArgs.cs`); its mod id is `ZombieBuddy`.
  The user's `default.txt` lists it first; `B42·20通用合集` does not contain it.
- **Launcher today** — no mod list UI; `ModInfo` reads `mod.info` keys, `ModLocator` mirrors the game's mod lookup
  order (staged Workshop → Steam Workshop libraries → `<cache>/mods`). Launch flow: `HomeViewModel.LaunchAsync`
  (ZombieBuddy setup prompt → Zabric preload → `IGameLaunchService.LaunchAsync`). Tabs: Home, Installs, Settings.
- The user already syncs `default.txt` from a collection by hand (`mods/default.txt.bak-2026-10-16-sync-my`).

## Requirements

Collections are those of the install selected in the launcher (game installs only).

- **R0 Navigation (D7)** — a new top-level "模组" tab with a left nav sidebar of two sections, "模组管理" and
  "合集管理" (same pattern as the Settings page's sections).
- **R1a Mod management (D8)** — "模组管理" lists every installed mod (R2) with name, id, Workshop id, source
  (Steam Workshop / staged Workshop / local `mods`), enabled in `default.txt` or not, and the collections containing
  it. Clicking a column header sorts by that column (again: reverses; arrow shows column and direction; default
  name ascending). Search by name/id/Workshop id; filters all / enabled / not enabled. "添加到合集…" adds the selected mod(s) to a
  chosen collection (appended at the end, R3 rules). It never changes `default.txt`.
- **R1 List** — "合集管理" lists the cache folder's collections with mod count, a marker on the default
  collection (R7), and an "in use" marker on every collection that, applied, enables at least one mod and gives the
  current `default.txt` set of installed mods (so nothing is "in use" while no installed mod is enabled).
- **R2 Installed mods** — the launcher enumerates installed mods in the game's lookup order (first copy of an id wins),
  reading id, name, Workshop item id, `require`, `loadAfter`, `loadBefore`, `incompatible`. Shown per collection row
  as name + id; ids not installed are kept in the collection and shown as missing.
- **R3 Edit (D4)** — create (empty), create from the current `default.txt` list, rename, delete, remove mods, add
  installed mods found by name/id search. Names follow the game's rules (non-empty, no `:`, no invalid filename
  characters); a name clash asks before overwriting.
- **R4 Order (D3)** — drag reorder inside a collection, and an auto sort that matches the game's algorithm. The drag
  feels like lifting the row out (D10): the dragged row floats above the list and follows the pointer, a gap shows
  where it will land, the other rows slide aside, and it settles into the gap on release. Each row also has move up /
  move down and move to top / move to bottom. The order is
  written to the cfg line and the share text, and remembered by the launcher so that an in-game rewrite of the cfg
  restores the launcher order for ids still present (new ids keep their cfg order, at the end).
- **R5 Share / import** — share copies the game's exact share text to the clipboard. Import accepts pasted text of one
  or more such lines (`!fav!` lines ignored), keeps the pasted order, and adds or (after confirm) overwrites by name.
  Import persists immediately (unlike the game).
- **R6 Apply (D1)** — "应用为启用列表" rewrites `default.txt` `mods { }` the way choosing the collection in-game would:
  installed mods only, in collection order, each mod's installed `require` deps (recursively) placed before it, a mod
  incompatible with an already-enabled one skipped. The `maps { }` block and `VERSION` line are kept as they were.
  Before writing, the previous file is kept as one rolling backup, and the very first launcher write also keeps a
  permanent copy `default.txt.pzlauncher.orig` that is never overwritten.
- **R6b Drop notice (D9)** — whenever an apply (manual or R7 at launch) would drop missing mods or skip incompatible
  ones, one combined dialog lists them before `default.txt` is written: missing ids, and each skipped id with the
  enabled mod it conflicts with; buttons 继续 / 取消. Continue writes; Cancel aborts the apply (at launch: aborts the
  launch). No dialog when nothing is dropped.
- **R6a Keep the current list (D6)** — before any apply (manual or R7 at launch), when the current `default.txt`
  enables at least one mod and its id set equals no collection's applied id set (the R1 "in use" test), the launcher
  offers to save it as a new collection: name prefilled "原启用列表" (localized), editable, with Save / Skip / Cancel.
  Save stores it in `default.txt` order and then applies; Skip applies without saving; Cancel aborts the apply (at
  launch: aborts the launch). A clashing name asks before overwriting, as in R3.
- **R7 Default collection (D2)** — at most one collection per install is marked default; marking another moves the
  mark. Every launch from the launcher applies it (R6, after R6a) before the game starts; a failed apply is reported
  and the launch goes on with the current `default.txt`. Unmarking only stops the auto-apply (`default.txt` stays as
  last applied). Renaming the collection keeps the mark; deleting it clears the mark.
- **R8 ZombieBuddy follows the default collection (D5)** — on a launch from the launcher that applied a default
  collection (R7) whose applied list does not enable `ZombieBuddy`, the ZombieBuddy step runs the existing remove flow instead of the
  initialize flow: when the install has ZombieBuddy leftovers (files or agent args), the ZombieBuddy setup dialog
  opens in remove mode (`ZombieBuddySetupMode.Remove`, the same dialog the launch shows today for unsubscribed
  leftovers), and the Zabric preload is skipped. Nothing ZombieBuddy-related happens when there are no leftovers.
  A default collection containing `ZombieBuddy`, a failed apply, or no default collection keeps today's flow
  (initialize when subscribed and not initialized).
- **R9 Safety** — while a game started by the launcher is running, every write (cfg, `default.txt`) is disabled.
  Every write re-reads the file first and changes only the affected collection/block, so edits made in-game since the
  last refresh survive; the `!fav!` line and the other collections' text are kept byte-for-byte. Writes are atomic.
- **R10 Localization** — every new UI string exists in all `Lang/UiText*.resx` files.

## Acceptance Criteria

- [ ] AC0 (R0, R1a) The "模组" tab shows the two sections; 模组管理 lists the user's installed mods with the six
      columns, search and the three filters narrow the list, "添加到合集…" appends to the chosen collection's cfg line,
      and `default.txt` is unchanged by anything on this section.
- [ ] AC1 (R1, R2) With the user's cache folder, the tab lists `B42·20通用合集` and `my合集` with their mod counts; mod
      rows show names from `mod.info`; an id with no installed copy shows as missing.
- [ ] AC2 (R3) Create, create-from-current, rename, delete, add (search by name or id) and remove each change
      `pz_modlist_settings.cfg` as expected; the game's Mods screen then shows the same collections.
- [ ] AC3 (R4) After a drag reorder the cfg line lists ids in the new order; auto sort on a fixture gives the order the
      game's `autoSort` gives; after shuffling the ids in a cfg line (simulating an in-game save) the launcher still
      shows its remembered order, and an id added in-game appears at the end.
- [ ] AC4 (R5) Shared text equals `name:id1;id2;...;` plus a newline and imports into the game's "ADD" dialog;
      text copied from the game imports into the launcher with the same name and ids; overwriting asks first.
- [ ] AC5 (R6) Applying a collection writes `default.txt` with the expected enabled ids and order (deps before
      dependents, missing ids dropped, incompatible ids skipped), keeps `maps { }`, leaves the rolling backup, and
      creates `default.txt.pzlauncher.orig` on the first write only (a second apply leaves it unchanged).
- [ ] AC5b (R6b) Applying a collection with a missing id and an incompatible pair shows one dialog naming both before
      any write, manually and at launch; Cancel leaves `default.txt` untouched (and doesn't launch); a collection with
      nothing to drop shows no dialog.
- [ ] AC5a (R6a) With an enabled list matching no collection, apply (manual and at launch) shows the save prompt
      prefilled "原启用列表"; Save creates that collection in `default.txt` order then applies, Skip applies only,
      Cancel changes nothing (and doesn't launch). With a matching collection or an empty list, no prompt shows.
- [ ] AC6 (R7) With a default collection marked, launching from Home applies it before the process starts; a launch
      after an in-game change to the enabled list restores the collection; a failed apply shows an error and the game
      still starts.
- [ ] AC7 (R8) With ZombieBuddy initialized and a default collection without `ZombieBuddy`, launching opens the remove
      dialog, after which `ProjectZomboid64.json` has no ZombieBuddy agent arg, the ZombieBuddy files are gone and no
      initialize dialog shows on this or the next such launch; cancelling the dialog cancels the launch (as today).
      With `ZombieBuddy` in the default collection, or no default collection, the launch is as today.
- [ ] AC8 (R9) While a launcher-started game runs, every write command is disabled; a cfg line edited externally
      between refresh and a launcher write survives the write; `!fav!` survives byte-for-byte.
- [ ] AC9 (R10) Localization resource tests pass with the new keys in every resx.

## Decisions

- D1: "设为预设合集" = apply a collection as the game's active mod list (`default.txt`), with a backup.
- D2: the launcher can mark one default collection that is applied before every launcher launch. Accepted cost:
  in-game changes to the enabled list are overwritten at the next launcher launch.
- D3: sorting is per collection; the order lives in the cfg line and share text and is remembered by the launcher.
- D4: full editing, including add/remove of single mods via search over installed mods.
- D5: a default collection without ZombieBuddy uninstalls ZombieBuddy at launch through the existing remove flow
  (user, 2026-10-17). Adding `ZombieBuddy` back to the default collection makes the next launch initialize it again.
- D6: the pre-existing enabled list is protected by a save-as-collection prompt before apply plus a permanent first-write
  backup (user, 2026-10-17).
- D9: dropped (missing) and skipped (incompatible) mods are announced in one dialog before every apply, launch
  included, with the choice to cancel (user, 2026-10-17).
- D10: drag reorder lifts the row out (floating row, landing gap, rows slide aside), plus move to top / bottom
  buttons (user, 2026-10-17).
- D7: navigation is a "模组" tab with "模组管理" and "合集管理" sections (user, 2026-10-17).
- D8: 模组管理 is a read-only browser of installed mods plus "添加到合集"; the enabled list changes only through
  collections, so it never fights the default collection (user, 2026-10-17).

## Out of Scope

- Downloading or subscribing to missing Workshop mods (no Workshop links in this task).
- Favourites, map order editing, mod details/posters.
- Enabling/disabling single mods or editing load order directly in `default.txt` (D8).
- Per-save (`currentGame`) and server-settings mod lists; dedicated server installs.
- Detecting a game started outside the launcher (R9's re-read-before-write is the mitigation).
- Keeping order when someone imports shared text in-game (the game drops it).
