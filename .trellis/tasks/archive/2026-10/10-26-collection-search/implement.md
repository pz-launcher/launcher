# Implement

1. `ModCollectionsViewModel`: `FilterText`, `VisibleEntries`, `IsFilterEmpty`, `RefreshVisibleEntries`; hook into `Rebuild`,
   `Select`, `OnFilterTextChanged`.
2. Rewrite `Move` and the move commands / `CanMove*` for visible indexes (design.md "Moves").
3. `ModCollectionsView.axaml`: filter box, bind list to `VisibleEntries`, empty-filter hint.
4. Add the two strings to every `PzLauncher.Desktop/Lang/UiText*.resx`.
5. Tests in `PzLauncher.Desktop.Tests/ViewModels/Mods/ModsViewModelTests.cs` for AC1–AC7 (Theory for the AC3 move cases).

## Validation

- implement: `dotnet build` only.
- check: `dotnet build` and `dotnet test PzLauncher.Desktop.Tests`.

## Risky spots

- `Move` index mapping and `CanMove*` (drag and buttons both go through it).
- Any other caller of `ModCollectionsViewModel.Entries` expecting it to be what the list shows — none besides the view and tests today.
