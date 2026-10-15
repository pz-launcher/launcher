# Implement plan: Home shell

## Checklist (in order)

1. **Theme resources** — add `Themes/ShellTheme.axaml` (Light/Dark theme dictionaries, keys from design.md), merge in `App.axaml`, set Fluent accent to PZ red, `RequestedThemeVariant="Light"`.
   - `AppTheme`, `IThemeService` + Avalonia implementation; preference store generalized to language + theme (old-file compatible); startup applies saved theme.
2. **Sidebar nav control** — reusable grouped nav (`Views/Controls/` control or `ListBox` `ControlTheme`) with group headers and the selected-item style; sidebar container style.
3. **View models**
   - `InstallsViewModel`: `SelectedInstall` + restore-after-scan logic; `Filter` enum + `VisibleInstalls`; list-selection property that ignores filter-induced nulls.
   - `SettingsViewModel`: move language logic out of `MainViewModel`; `SettingsSection` (Language).
   - `HomeViewModel`: selected-install display state, `ActivateCommand` delegating to Installs, disabled `LaunchCommand`, select-version navigation.
   - `MainViewModel`: `ShellPage`, `CurrentPageKind`, `CurrentPage`, `NavigateCommand`, child VMs, current theme + `ToggleThemeCommand`.
   - `App.axaml.cs` DI registrations (InstallsViewModel singleton; Home/Settings VMs); `DesignData` updates.
4. **Views** — `HomeView` (content sidebar), `SettingsView` and `InstallsView` (nav sidebars) (+ DataTemplates in `App.axaml`); `MainView` becomes shell with title bar, nav, theme toggle icon button, caption buttons; `MainWindow` extended client area + min size.
5. **Localization** — new `Shell_*` / `Home_*` / `Settings_*` keys in all 10 `UiText*.resx` (same key order as `UiText.resx`).
6. **Tests** (`PzLauncher.Desktop.Tests`)
   - `InstallsViewModelTests`: selection defaults to first, kept across rescan by path, falls back when gone, null when empty; filter limits `VisibleInstalls` per kind and never changes `SelectedInstall`.
   - `HomeViewModelTests`: reflects selection changes; activate triggers one scan; launch can't execute; select-version navigates.
   - `MainViewModelTests`: starts on Home; navigate switches `CurrentPage`; toggle flips theme via the service and saves it.
   - Preferences store tests: round-trip both fields; language-only file loads with null theme; unknown theme → null + no throw; saving one field keeps the other.
   - `SettingsViewModelTests`: the two existing language tests moved from `MainViewModelTests`; default section is Language.
7. **Manual run on Windows** — AC1–AC6a, AC7/AC7a (toggle in title bar, restart keeps it; OS theme has no effect), language switch on Settings (AC3).

## Validation commands

```bash
dotnet build PzLauncher.slnx
dotnet test
dotnet format --include <changed .cs files>
```



## Risky points

- `MainWindow` decoration mode (resize/snap regressions) — check first by running the app right after step 4's window change.
- Filter changes and two-way `SelectedItem` binding + `Installs.Clear()` during rescan pushes null into `SelectedInstall`; the restore logic must run after repopulation and use the path captured before clearing.
- `MainView` rebuild on language switch: verify caption-button handlers and maximize icon state re-sync from `WindowState`.

## Before `task.py start`

- User approves the planning summary.
- `implement.jsonl` / `check.jsonl` contain the desktop + common specs.
