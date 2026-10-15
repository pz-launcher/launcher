# Design: Home shell (title-bar nav + sidebar)

## Boundaries

All work is in `PzLauncher.Desktop` (+ its tests). Core is untouched: install data is the existing `PzInstall`/`IInstallDetectionService`. No Avalonia types in view models (`.trellis/spec/desktop/directory-structure.md`).

## View model structure

```
MainViewModel (singleton)
├── CurrentPageKind : ShellPage { Home, Installs, Settings }   ← nav state, survives MainView rebuild
├── CurrentPage     : ViewModelBase (derived from CurrentPageKind)
├── NavigateCommand(ShellPage)
├── Home     : HomeViewModel      (new)  ── reads InstallsViewModel.SelectedInstall
├── Installs : InstallsViewModel  (existing, + SelectedInstall, + kind filter)
└── Settings : SettingsViewModel  (new; language picker moved from MainViewModel, + section)
```

### Sidebar pattern

- Layout: each page view is a two-column `Grid` — `Auto,*` for left nav sidebars, `*,Auto` for Home's right sidebar; the `Auto` column is the sidebar, so its width comes from its content (optionally a `MinWidth` per page). A shared `Shell.Sidebar` style/theme resource gives the sidebar background, with the divider on the edge facing the content (right edge for left sidebars, left edge for Home), so pages don't repeat brushes. No global sidebar host in `MainView`: Home's sidebar is page content, not navigation.
- Nav sidebar items are plain data, built per page in the VM: a small record (e.g. `SidebarItem(Key, Icon, Label, Group?)` or a group/items pair) — the label is resolved from `UiText` in the view or re-rendered on `LanguageChanged`, never cached across a language switch (localization spec: text that changes while a view lives is a VM property). Simplest compliant option: the view declares the items in XAML with `{x:Static}` labels and binds selection to an enum on the VM (`InstallsFilter { All, Game, DedicatedServer }`, `SettingsSection { Language }`), since `MainView` (and thus the page view) is rebuilt on a language switch anyway.
- Reusable control: `Views/Controls/SidebarNav` (or a styled `ListBox` + `ControlTheme`) rendering group headers and items with the selected style (accent left bar, tinted background, accent text). Must stay compiled-binding/AOT-safe.

### Installs filter

- `InstallsViewModel` gets `Filter` (enum incl. `All`) and a filtered collection bound by the list (`VisibleInstalls`), rebuilt when `Filter` or `Installs` changes. `Installs` stays the full set (Home and selection logic use it). Empty state per filter.
- Selection vs filter: the `ListBox` binds to `VisibleInstalls`; when the filter hides the selected install, the `ListBox` pushes `null` through the two-way `SelectedItem` binding. `SelectedInstall` must ignore that (only a real item from the list or the rescan-restore logic sets it), e.g. bind the ListBox to a separate `ListSelection` property that forwards non-null values to `SelectedInstall` and is re-synced when the filter changes.

- `ShellPage` enum lives next to `MainViewModel`. The three child view models are constructor-injected; `CurrentPage` returns the one matching `CurrentPageKind` (exhaustive switch). Nav tab highlight binds to per-page booleans or an enum-equality converter — pick whatever stays compiled-binding/AOT-safe; no reflection.
- `HomeViewModel(InstallsViewModel installs, …)`:
  - Exposes the selected install for display (forwarded from `installs.SelectedInstall`, raising change notifications when it changes) and an `IsEmpty`-style state for "no install".
  - `ActivateCommand` → delegates to `installs.ActivateCommand` (run-once rule stays in `InstallsViewModel`). `HomeView.OnLoaded` calls it, per the existing page-activation convention — so the scan starts at app launch because Home is the start page.
  - `SelectVersion` navigation: Home must ask the shell to switch to Installs without referencing `MainViewModel` circularly. Use a small callback/`Action` or a navigation service interface injected into `HomeViewModel` (e.g. `IShellNavigator` implemented by `MainViewModel`); choose the simplest that keeps tests easy.
  - `LaunchCommand` exists with `CanExecute == false` (button disabled through the command, not a hard-coded `IsEnabled`).
- `InstallsViewModel`: add `[ObservableProperty] PzInstall? SelectedInstall`, bound two-way to the ListBox `SelectedItem`. After `ScanAsync` repopulates `Installs`, restore the previous selection by root path (`_platform.PathComparison`) else first item else null. `AddFolderAsync` doesn't change the selection.
- `SettingsViewModel`: takes over `Languages`, `SelectedLanguage`, and `OnSelectedLanguageChanged` from `MainViewModel` unchanged (including the null guard comment).
- DI (`App.axaml.cs`): `InstallsViewModel` must be a single instance shared by Home and Installs → register as singleton (it is already effectively app-lifetime via the singleton `MainViewModel`). Register `HomeViewModel`, `SettingsViewModel`.
- `DesignData`: add `Home`, `Settings`; update `Main` construction.

## Views

- `App.axaml`: add DataTemplates for `HomeViewModel` → `Views/Home/HomeView`, `SettingsViewModel` → `Views/Settings/SettingsView`.
- `MainWindow.axaml`: `ExtendClientAreaToDecorationsHint="True"`, decoration mode chosen so that Windows keeps edge resize + snap (start with `WindowDecorations="BorderOnly"`; if that still draws OS caption buttons, use `None` and add `WindowDecorationProperties.ElementRole` resize grips). `MinWidth`/`MinHeight` set. No localized content on the window (localization spec).
- `MainView.axaml` becomes the shell: `Grid` rows = title bar (fixed height) + page `ContentControl`.
  - Title bar `Border` has `WindowDecorationProperties.ElementRole="TitleBar"` (native drag + double-click maximize).
  - Nav tabs: three toggle-style buttons (icon + `{x:Static}` label) with `NavigateCommand` + `CommandParameter`; selected style = pill.
  - Caption buttons: `ElementRole="MinimizeButton" / "MaximizeButton" / "CloseButton"` (gives Win11 snap-layout flyout on maximize); click handlers in `MainView` code-behind act on `TopLevel.GetTopLevel(this) as Window` (view concern). Maximize icon toggles with `WindowState`.
  - macOS: hide caption buttons and add left padding for the traffic lights (`OperatingSystem.IsMacOS()` in code-behind, or `OnPlatform` in XAML).
  - Root sets `FontFamily` from `Main_FontFamily` (already does).
- `Views/Home/HomeView.axaml`: two columns — content area (left, background brush only) and content-sized sidebar on the right (`Auto` column with a comfortable `MinWidth`; selected install block centered, buttons docked bottom: 启动游戏 large primary, 版本选择 secondary).
- `Views/Settings/SettingsView.axaml`: nav sidebar (group 启动器 → 语言) + content area showing the selected section; the 语言 section holds the language picker (moved from `MainView`).
- `InstallsView.axaml`: nav sidebar (全部; group 类型 → 游戏, 专用服务器) + existing content bound to `VisibleInstalls` and the selection property.

## Theme resources

- New `PzLauncher.Desktop/Themes/ShellTheme.axaml` (`ResourceDictionary` with `ThemeDictionaries` → `Light` / `Dark`), merged in `App.axaml`. Named brushes such as `Shell.SidebarItemSelectedBackground`, `Shell.TitleBarBackground`, `Shell.TitleBarForeground`, `Shell.NavSelectedBackground/Foreground`, `Shell.SidebarBackground`, `Shell.ContentBackground`, `Shell.Accent`, `Shell.TextMuted`, `Shell.Border`. Views use `{DynamicResource}` only, so the light/dark toggle applies live and a future custom-color feature can override the same keys.
- Starting values (from the approved mockups; tune for contrast when rendering):

| Key | Dark (A) | Light (B) |
|---|---|---|
| TitleBarBackground | `#1B1917` | `#7A1C1C` |
| TitleBarForeground | `#B8AFA2` | `#EBD9CF` |
| NavSelectedBackground | `#9B2226` | `#F7F0E6` |
| NavSelectedForeground | `#F5EFE6` | `#7A1C1C` |
| SidebarBackground | `#25221F` | `#FBF8F3` |
| ContentBackground | `#2F2B27` | `#EDE6DB` |
| Accent | `#9B2226` | `#9B2226` |
| TextPrimary | `#E8E2D6` | `#2B2622` |
| TextMuted | `#9C9488` | `#7D746A` |
| Border | `#4A443D` | `#CFC6BA` |
| SidebarItemSelectedBackground | `#3A2624` | `#F3E4E0` |
| SidebarGroupHeaderForeground | `#857D72` | `#8C8378` |

- Also set FluentTheme's accent (`SystemAccentColor`) to the PZ red so stock controls (ListBox selection, ComboBox focus) match.
- Add a "Styling and theming" spec (`.trellis/spec/desktop/`) in Phase 3 — the desktop index lists it as not written yet.

## Revision (after first implementation, user-directed)

- Theme choice moved from the title bar to 设置 → 外观 (`SettingsSection.Appearance`, replacing `Language`); radio buttons bound to the theme; `SettingsViewModel` owns theme selection + save (moved from `MainViewModel.ToggleThemeCommand`).
- Language picker moved from 设置 to a title-bar globe button with a dropdown (flyout/menu) of native names, current checked; language selection state + save move back to `MainViewModel` (or a small title-bar VM). The rebuild of `MainView` on language change happens while the flyout is open — close it / rely on the posted rebuild so nothing touches a torn-down control.
- Sections below that mention the title-bar theme toggle or the 设置 language section are superseded by this revision.

## Light/dark toggle and preferences

- `AppTheme { Light, Dark }` (Desktop, next to the theme code). `App.axaml` sets `RequestedThemeVariant="Light"` (no longer `Default`), so the OS theme is ignored.
- `IThemeService` (interface in `Services/`, VM-facing, no Avalonia types) with `Current` and `SetTheme(AppTheme)`; the implementation maps to `Application.RequestedThemeVariant` (`ThemeVariant.Light/Dark`). Startup applies the saved theme before the first window is created, like the language.
- `MainViewModel` exposes the current theme (for the icon: moon in light = "switch to dark", sun in dark) and `ToggleThemeCommand` → `SetTheme` + fire-and-forget save (same pattern and "a failed save must not undo the switch" rule as the language).
- Theme state lives in the service/VM, so a `MainView` rebuild re-reads it.

### Preference store generalization

- Replace `ILanguagePreferenceStore`/`JsonLanguagePreferenceStore` with one preferences store (e.g. `IPreferenceStore` → `Load()` returns language and theme, each nullable; `SaveLanguageAsync`, `SaveThemeAsync`). Each save updates one field of an in-memory record (seeded by `Load`) and writes the whole record under the existing save gate + temp-file rename, so concurrent fire-and-forget saves never drop the other field.
- JSON: `{ "language": "<culture name>", "theme": "light" | "dark" }`. `theme` is a new persisted contract: lowercase stable strings, not enum names. Missing `theme` (old files) → null → Light; unknown value → Light + warning log, mirroring the unknown-language path. `language` contract unchanged.
- Rename `LanguagePreference` record accordingly in `DesktopJsonContext`; update DI in `App.axaml.cs`, `DesignData`, test fakes (`FakeLanguagePreferenceStore`), and the store tests (`JsonLanguagePreferenceStoreTests` → preferences store tests).
- Spec follow-up (Phase 3): localization.md's preference-file note moves/links to the store's new owner doc.

## Compatibility / risks

- Avalonia 12 custom chrome: verify on Windows that resize borders, snap, and drag work with the chosen `WindowDecorations` mode. Linux extended-client-area support is "limited" per Avalonia docs; acceptable since Windows is the primary target, but the window must still be usable (fallback: OS decorations on Linux if extending fails).
- Language switch rebuilds `MainView`: nav state is in `MainViewModel`, selection in `InstallsViewModel` → both survive.
- `MainViewModelTests` change (language tests move to `SettingsViewModelTests`).

## Rollback

Single feature commit; reverting it restores the previous window/MainView. The preference file gains an optional `theme` field; the previous version ignores unknown JSON properties when reading, so rolling back keeps the saved language.
