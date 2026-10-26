# Design

## View model (`ModCollectionsViewModel`)

- `Entries` stays the full, ordered list (tests and `Rebuild` keep using it).
- New `FilterText` (observable) and `VisibleEntries` (observable collection) holding the `Entries` rows that match, in order.
  `RefreshVisibleEntries()` runs from `Rebuild` and `OnFilterTextChanged`; `Select` clears `FilterText` next to `AddSearchText`.
- New `IsFilterEmpty`: filter text set, `Entries` non-empty, `VisibleEntries` empty.
- Matching an entry: rows don't carry `InstalledMod`, so the filter looks the id up in `_page.State.InstalledMods` and calls
  `InstalledModsViewModel.Matches`; a missing id matches on the id alone. Build the lookup once per refresh.

## Moves

`IReorderableList.Move(from, to)` is now in **visible** indexes (that is what the drag and the bound list see).
With no filter, visible == full, so the mapping is the identity and today's behaviour is kept.

1. `row = VisibleEntries[from]`, `target = VisibleEntries[to]`; full indexes `f = Entries.IndexOf(row)`.
2. Remove `row` from `_ids`/`Entries`, then `t = Entries.IndexOf(target)`; insert at `t` when moving up (`to < from`), else `t + 1`.
3. Renumber full-list rows between the old and new positions; `VisibleEntries.Move(from, to)`.
4. `NotifyMoveCommands`, `ScheduleOrderSave`, `RefreshSelectedInUse` as today.

Rows are located by reference, not id, so a preset listing an id twice still moves the right row.

Buttons pass visible indexes: up/down = ±1, top = 0, bottom = `VisibleEntries.Count - 1`.
`CanMoveEarlier`/`CanMoveLater` use the row's index in `VisibleEntries` (> 0, < Count - 1) instead of `Number`.

## View (`ModCollectionsView.axaml`)

- Filter `TextBox` bound to `FilterText` above the entries list (below the add box), its own placeholder.
- `EntriesList.ItemsSource` → `VisibleEntries`. `ListReorderDrag` needs no change: it counts rows of the bound list.
- Hint `TextBlock` bound to `IsFilterEmpty`, overlaying the list like other empty hints.

## Strings

`ModCollections_FilterPlaceholder`, `ModCollections_FilterEmpty` in every `UiText*.resx` (base + 9 locales) (see `.trellis/spec/desktop/localization.md`).

## Risk / rollback

Only the preset editor VM, its view and resx change; reverting the commit restores the old editor. The main risk is the index
mapping in `Move`, covered by AC3/AC4 tests.
