# Directory Structure (desktop app)

## Layout

```
PzLauncher.Desktop/
├── App.axaml(.cs)       # composition root, VM→View DataTemplates
├── Views/<Feature>/     # one UserControl per page
├── ViewModels/<Feature>/
├── ViewModels/Design/   # previewer-only sample data
├── Services/            # UI-only services (interface + Avalonia implementation)
├── Lang/                # UiText resx files only (see localization.md)
├── Localization/        # language model, LocalizationService, ErrorMessages, DisplayNames
└── Assets/
```

`<Feature>` matches the Core feature folder (`Installs`, ...).

## ViewModels

Reference: `PzLauncher.Desktop/ViewModels/Installs/InstallsViewModel.cs`.

- CommunityToolkit source generators; dependencies via constructor (DI).
- **No `Avalonia.*` types.** Windows, dialogs, storage go behind `Services/` interfaces (e.g. `IFolderPicker`), so ViewModels are testable without Avalonia.
- No `Dispatcher` use.
- Page state is explicit (busy, loaded, empty, error). Don't infer it from item counts.
- Bind to Core DTOs directly unless a row needs its own commands or state.

## Views

Reference: `PzLauncher.Desktop/Views/Installs/`.

- Every `.axaml` sets `x:DataType` (compiled bindings, required for AOT) and a `Design.DataContext`.
- Code-behind: view concerns only. Page activation calls the ViewModel's `ActivateCommand` from `OnLoaded`; the run-once rule lives in the ViewModel.
- VM→View mapping: explicit `DataTemplate`s in `PzLauncher.Desktop/App.axaml`, never a reflection `ViewLocator`.
- Previewer data: `PzLauncher.Desktop/ViewModels/Design/DesignData.cs`. Fictional, portable values only, never data from a real machine.

## Composition root

`PzLauncher.Desktop/App.axaml.cs` is the only place that builds the DI container.

## Forbidden

- Logic a web UI would also need. It goes in Core.
