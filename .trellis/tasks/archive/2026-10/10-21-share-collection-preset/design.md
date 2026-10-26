# Design: local collections, modpacks, modpack share text

## Share text format (Core, `Mod/Modpacks/ModpackShareText`)

```
#pzlauncher-modpack 1
#name
Example Server
#steam-collection
3012345678
#items
3012340001
3012340002
#preset
Example Server:ModA;ModB;ModC;
```

or, with a local collection, instead of `#steam-collection`:

```
#local-collection
My list
3012340010
3012340011
```

- Markers are fixed ASCII, matched ignoring case, never localized (as `WorkshopFavoritesShareText`). Every marker line
  starts with `#`; blank lines are ignored; surrounding whitespace is trimmed.
- Line 1 header `#pzlauncher-modpack <version>`; this design is version 1. A higher version is refused (R14); no
  header means "not modpack text".
- `#name`: the next line is the modpack name. `#local-collection`: the next line is the collection name, then ids.
  Names are single-line by construction; a name line is never read as an id, even if numeric.
- Id lines accept ids or Workshop links (`WorkshopLinks.TryParseItemId`); malformed lines are skipped and counted.
- `#preset` holds exactly one game line, parsed by `ModCollectionShareText.ParseLine`; it is last so the user can copy
  it alone into the game.
- Unknown markers: their section is skipped (later versions may add sections).
- Exactly one of `#steam-collection` / `#local-collection` and one `#preset` are required; `#items` is optional.
- Parse result: `SharedModpack(Name, Collection (Steam id | local name + ids), ExtraItems, Preset (SharedModCollection), Skipped)`
  or a typed failure (not modpack text / newer version / missing section). Pure static, unit-tested, no I/O.

## Extra items (D2)

Computed in Desktop from the install's `ModCollectionState.Installed` and the subscriptions join:

- From the preset's mod ids, take each installed mod's Workshop item id (order: first mod in preset order), drop ids
  in the collection. Steam collection contents come from `GetCollectionAsync` (session-cached); if it fails, keep all
  preset items (a superset is harmless: subscribe only touches unsubscribed ids).
- The modpack stores `extraItems`. On recompute, a stored item is kept if it is not installed (its mods are unknown
  here, e.g. a receiver before download); it is dropped if installed and none of its mods is in the preset any more, or
  if it is now in the collection. New computed items are appended.
- Recomputed when the modpack is shown and before sharing.

## Data and persistence (Desktop, `preferences.json`)

| Field | Shape |
|---|---|
| `localCollections` | array, user order: `{id: guid string, name, items: [{id, title}]}`; invalid entries dropped, rest kept |
| `modpacks` | object: absolute install root → array of `{name, preset: {linked: name or null, copy: {name, modIds[]}}, collection: {steam: id} or {local: {linked: guid or null, copy: {name, items[{id,title}]}}}, extraItems: [ids]}` |

- `linked: null` = independent. Local collections are linked by guid so a rename never breaks links; presets are
  linked by name and the launcher's rename updates links.
- Services: `LocalCollections` (like `WorkshopFavorites`: in-memory, `Changed` event, fire-and-forget save) and
  `Modpacks` (per install, keyed with the platform path comparison like `IModCollectionDefaultsService`). Both in
  `Desktop/Services/Mods/`. `WorkshopTitles` also feeds local collection titles.
- Spec: add both fields to `.trellis/spec/desktop/preferences.md`.

## Link maintenance (R9)

- After every successful read of the install's presets (`ModsViewModel` state load): for each modpack, a linked preset
  present → refresh its copy; absent → set `linked = null`. A failed read changes nothing.
- `LocalCollections.Changed`: refresh copies of linked local collections; a guid no longer stored → independent.
- Launcher rename of a preset (`SaveModCollectionRequest` with an original name) → `Modpacks.RenamePreset`.
- Deletes: `ModCollectionsViewModel.DeleteAsync` and the local-collection delete ask `Modpacks.UsersOf(...)` first; if
  any, `IDialogService.ChooseAsync` (confirm = delete and keep modpacks independent, alternative = delete them too,
  cancel). Otherwise today's confirmation.

## UI

- `ModsSection` gains `Modpacks` (after `Collections`) and `LocalCollections` (after `WorkshopCollections`).
- `LocalCollectionsViewModel` (Workshop folder) derives from `WorkshopItemListSectionViewModel` for state, 订阅缺失项,
  更新过期项, 生成本地预设; the left list and reorder follow `ModCollectionsView` (reuse `ListReorderLayout` and the
  drag code by extracting it if it is view-specific — check before copying).
- "添加到本地合集…": new `IDialogService.PickLocalCollectionAsync` (radio list + "new collection" name box) returning
  the target guid or a new name. Row action on `WorkshopItemRow` lists; in 工坊订阅 also a toolbar action on the
  selection.
- `ModpacksViewModel` (new `ViewModels/Mods/Modpacks/`) also derives from `WorkshopItemListSectionViewModel` for the
  item list; edit dialog picks preset + collection; share/import via `IClipboardService` + `PromptTextAsync` +
  `ConfirmAsync`, subscribe via the existing `WorkshopActionFlow.RunAsync`.
- All new strings in every resx; "整合包" EN "Modpack(s)", other languages per `localization.md` terminology rules.

## Import sequence (R12, R13)

1. Parse → confirmation text. 2. Resolve clashes (each `ChooseAsync`: overwrite / new name → `PromptTextAsync`);
   any cancel → stop, nothing written. 3. Write preset (`Page.WriteAsync`, blocked while the game runs). 4. Save
   local collection or `WorkshopCollectionHistory.Remember`. 5. Save modpack linked to both. 6. Missing ids =
   (collection items, from the text or `GetCollectionAsync`) ∪ extra items, minus subscribed; one Subscribe action.
   7. Select the modpack. Errors in 6 are shown; 3–5 stay.

## Compatibility and rollback

- New preference fields only; older launchers ignore them. Preset file format untouched.
  Correction (10-26-modpack-review-fixes): older launchers drop unknown `preferences.json` fields when they save, so both
  lists moved to their own file, `mod-library.json` (`.trellis/spec/desktop/mod-library.md`).
- Existing share texts (preset line, favorites) unchanged. Rollback = revert the commits; leftover fields are ignored.
