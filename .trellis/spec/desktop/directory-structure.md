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

`<Feature>` matches the Core feature folder (`Installs`, ...), or names a desktop-only page (`Home`, `Settings`, `Mods`, which spans Core's `Mod/` and `Mod/Collections/`).

## ViewModels

Reference: `PzLauncher.Desktop/ViewModels/Installs/InstallsViewModel.cs`.

- CommunityToolkit source generators; dependencies via constructor (DI).
- Page state is explicit (busy, loaded, empty, error). Don't infer it from item counts.
- Bind to Core DTOs directly unless a row needs its own commands or state.
- State several pages observe (e.g. whether a game from this session runs) is an app-lifetime `ObservableObject` service, not a property one page owns. Reference: `PzLauncher.Desktop/Services/GameRunState.cs`.
- Core `IProgress<T>` reports are consumed in report order on the UI thread: report inline when already on the captured context, post otherwise. Not `Progress<T>`, which uses the thread pool without a context (tests) and can reorder or report after the result. Reference: `PzLauncher.Desktop/Services/ContextProgress.cs`.

## Views

Reference: `PzLauncher.Desktop/Views/Installs/`.

- Every `.axaml` sets a `Design.DataContext`.
- Code-behind: view concerns only. Page activation calls the ViewModel's `ActivateCommand` from `OnLoaded`; the run-once rule lives in the ViewModel.
- VM→View mapping: explicit `DataTemplate`s in `PzLauncher.Desktop/App.axaml`, never a reflection `ViewLocator`.
- Previewer data: `PzLauncher.Desktop/ViewModels/Design/DesignData.cs`. Fictional, portable values only, never data from a real machine.
- Range controls (`Slider`, ...) bind `Minimum`/`Maximum` before `Value`, with a range that includes the loaded value, or the control rewrites the saved value. Reference: `PzLauncher.Desktop/Views/LaunchSettings/LaunchSettingsView.axaml`.
- Dialogs go through `IDialogService` (`PzLauncher.Desktop/Services/IDialogService.cs`). A dialog that runs an operation takes a per-dialog ViewModel with `StartAsync` (once, on `Opened`) and `Abort` (on `Closed`, cancels the token); closing the window is cancel, and the follow-up action (e.g. launch) happens only when the dialog returns `true`. Reference: `ShowZombieBuddySetupAsync` in `PzLauncher.Desktop/Services/AvaloniaDialogService.cs`.
- Icons: `FluentIcons.Avalonia` (`<ic:FluentIcon Icon="…" IconVariant="Regular" />`, reference `PzLauncher.Desktop/Views/MainView.axaml`), matching the Fluent theme. Never paste SVG path data into XAML. Verified AOT-clean with a Native AOT publish (0 IL warnings).

## Launcher self-update

Reference: `PzLauncher.Desktop/Services/UpdateCoordinator.cs`, `PzLauncher.Desktop/ViewModels/Update/`; Core API `IUpdateService`.

- `UpdateCoordinator` is the only caller of `IUpdateService`: app-lifetime state the banner (`MainView`, between title bar and page) and the Settings "Updates" section observe.
- Startup (`MainWindow.Opened`, once): a development build (`0.0.0-*`, the csproj default; `release.yml` passes `-p:Version`) does nothing; otherwise clean up, offer a staged package without network, else check when `autoCheckUpdates` is on. Startup failures are only logged (by Core); "Check now" shows them.
- A staged package installs on "Restart and update" (install, `StartInstalledAsync`, then `IAppLifetime.Close` so the normal close flush runs) or in the close flush (`finishOnClose` of `WindowPlacementPersistence`, skipped on OS shutdown). Restart is disabled while `GameRunState.IsGameRunning`.

## Proxy settings

Reference: `PzLauncher.Desktop/Services/ProxySettingsService.cs`, `PzLauncher.Desktop/ViewModels/Net/ProxySettingsViewModel.cs`; Core API `INetworkService`.

- `ProxySettingsService` applies the saved proxy in its constructor; `App.axaml.cs` resolves it before anything that can send a request (the update coordinator's startup check included).
- Mode changes apply and save at once; field edits validate per keystroke and apply + save after a 300 ms debounce (`TimeProvider`). An invalid edit shows the error and leaves the last valid setting in effect and saved. The close flush includes a pending proxy change.
- Secrets in the preference file go through `ISecretProtector` (`WindowsDpapiSecretProtector`: hand-written `LibraryImport` of crypt32, hence `AllowUnsafeBlocks` in the csproj; `PlainSecretProtector` elsewhere), chosen by `IPlatformEnvironment.Os`.

## Composition root

`PzLauncher.Desktop/App.axaml.cs` is the only place that builds the DI container.

## Forbidden

- Logic a web UI would also need. It goes in Core.
