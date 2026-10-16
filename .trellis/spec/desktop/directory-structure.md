# Directory Structure (desktop app)

## Layout

```
PzLauncher.Desktop/
├── App.axaml(.cs)       # composition root, VM→View DataTemplates
├── Views/<Feature>/     # one UserControl per page
├── Views/Controls/      # shared controls and converters (NavItem, EnumMatchConverter)
├── ViewModels/<Feature>/
├── ViewModels/Design/   # previewer-only sample data
├── Services/            # UI-only services (interface + Avalonia implementation)
├── Lang/                # UiText resx files only (see localization.md)
├── Localization/        # language model, LocalizationService, ErrorMessages, DisplayNames
├── Themes/              # AppTheme, Shell.* color resources, shell control themes (see shell-and-theming.md)
└── Assets/
```

`<Feature>` matches the Core feature folder (`Installs`, ...), or names a desktop-only page (`Home`, `Settings`).

## ViewModels

Reference: `PzLauncher.Desktop/ViewModels/Installs/InstallsViewModel.cs`.

- CommunityToolkit source generators; dependencies via constructor (DI).
- Page state is explicit (busy, loaded, empty, error). Don't infer it from item counts.
- Bind to Core DTOs directly unless a row needs its own commands or state.

## Views

Reference: `PzLauncher.Desktop/Views/Installs/`.

- Every `.axaml` sets a `Design.DataContext`.
- Code-behind: view concerns only. Page activation calls the ViewModel's `ActivateCommand` from `OnLoaded`; the run-once rule lives in the ViewModel.
- VM→View mapping: explicit `DataTemplate`s in `PzLauncher.Desktop/App.axaml`, never a reflection `ViewLocator`.
- Previewer data: `PzLauncher.Desktop/ViewModels/Design/DesignData.cs`. Fictional, portable values only, never data from a real machine.
- Range controls (`Slider`, ...): bind `Minimum`/`Maximum` before `Value`, and the bound range must include the loaded value; otherwise the control coerces the value and writes it back, which saves a change the user never made. Reference: `PzLauncher.Desktop/Views/VersionSettings/VersionSettingsView.axaml`, `VersionSettingsViewModel.MaxMemorySliderMaximum`.
- Dialogs go through `IDialogService` (`PzLauncher.Desktop/Services/IDialogService.cs`); no ad-hoc windows in ViewModels or `App`.
- Icons: `FluentIcons.Avalonia` (`<ic:FluentIcon Icon="…" IconVariant="Regular" />`, reference `PzLauncher.Desktop/Views/MainView.axaml`), matching the Fluent theme. Never paste SVG path data into XAML. Verified AOT-clean with a Native AOT publish (0 IL warnings).

## Composition root

`PzLauncher.Desktop/App.axaml.cs` is the only place that builds the DI container.

## Forbidden

- Logic a web UI would also need. It goes in Core.
