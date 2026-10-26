# Search inside a collection searches its own items

## Goal

> 合集内的搜索框搜索的是合集内的东西，而不是订阅的mod

In the 模组预设 editor, the user can find a mod already in the preset. Today the editor's only search box is the add box,
which searches installed mods **not** in the preset, so a long preset can only be scanned by eye.

## Background

- Add box: `PzLauncher.Desktop/Views/Mods/ModCollectionsView.axaml:180-200`, `AddSearchText` →
  `ModCollectionsViewModel.RefreshCandidates` (`PzLauncher.Desktop/ViewModels/Mods/Collections/ModCollectionsViewModel.cs:477`).
  The UI says 模组预设 (#17); the code still says `ModCollections`.
- Entries are `ModCollectionEntryRow` (Id, Name, IsMissing, Number = load-order position from 1); a missing mod's Name is its id
  (`Rebuild`, `:412-459`). The order is the game's load order.
- Reorder paths: drag handle (`PzLauncher.Desktop/Views/Mods/ListReorderDrag.cs`, rows counted by fixed `EntryRowHeight`,
  calls `IReorderableList.Move(from, to)` with indexes in the shown list) and top/up/down/bottom buttons (`:266-302`).
  `CanMoveEarlier`/`CanMoveLater` compare `Number` with `Entries.Count`.
- Matcher to reuse: `InstalledModsViewModel.Matches` (`PzLauncher.Desktop/ViewModels/Mods/Installed/InstalledModsViewModel.cs:198`).
- Selecting another preset clears `AddSearchText` (`:102`).
- Editor tests: `PzLauncher.Desktop.Tests/ViewModels/Mods/ModsViewModelTests.cs` (e.g. `:365-369` asserts `Entries`).

## Requirements

- R1 Keep the add box unchanged.
- R2 Add a filter box for the selected preset's entries. An entry shows when its name, mod id or Workshop id contains the text,
  ignoring case (same rule as `Matches`); a missing mod matches on its id. Empty or whitespace text shows every entry.
- R3 Each shown row keeps its full load-order `Number`.
- R4 While filtering, reordering stays available and works in the full order, between visible entries only:
  - up / down: the entry moves to just before the previous / just after the next **visible** entry;
  - top / bottom: to just before the first / just after the last **visible** entry;
  - drag: dropped into slot *k* of the visible list means the same rule against the visible neighbour there.
  Hidden entries keep their relative order. Up/top are disabled on the first visible entry, down/bottom on the last.
  The new order is saved exactly as an unfiltered move is (`ScheduleOrderSave`).
- R5 Remove, add, auto-sort and preset refreshes keep the filter applied to the new entries.
- R6 Selecting another preset clears the filter, like the add box.
- R7 When the filter matches nothing in a non-empty preset, show a short hint instead of the empty list.
- R8 Placeholder and hint text exist in every `UiText.*.resx` locale.

## Acceptance Criteria

- AC1 (R2) Preset `[Maps, Core, Gone(missing)]`, filter `core` → only `Core` is shown; `gone` → only `Gone`; Workshop id fragment → that mod.
- AC2 (R3) In AC1 the shown `Core` row has Number 2.
- AC3 (R4) Order `[A, x, B, y, C]`, filter shows `A, B, C`: move-up on `C` → `[A, x, C, B, y]`; move-to-top on `C` → `[C, A, x, B, y]`;
  move-down on `A` → `[x, B, A, y, C]`; drag visible index 2 → 0 equals move-to-top. Numbers are renumbered; the saved order matches.
- AC4 (R4) Up/top disabled for the first visible entry, down/bottom for the last; with no filter, behaviour and saved orders are unchanged.
- AC5 (R5) Removing a shown entry or adding a matching mod updates the filtered list without clearing the filter.
- AC6 (R6) Switching presets empties the filter box.
- AC7 (R7) A filter matching nothing shows the hint; clearing it shows all entries.
- AC8 Build passes; new VM tests cover AC1–AC7 in `ModsViewModelTests`.

## Out of Scope

- Search in 工坊合集 / 本地合集 / 工坊订阅.
- Changing the add box's behaviour or `Matches`.
- Highlighting matched text.
