# Shell, Navigation and Theming

> The main window's custom title bar, page navigation, page sidebars, and the
> color system. Persisted theme/language choices: [preferences.md](./preferences.md).

---

## Window chrome

Reference: `PzLauncher.Desktop/Views/MainWindow.axaml`, `PzLauncher.Desktop/Views/MainView.axaml`.

- `MainWindow` extends the client area into the decorations: `WindowDecorations` is `BorderOnly` everywhere except macOS (`Full`). `BorderOnly` keeps the native resize frame, shadow and snap on Windows without any OS or Avalonia-drawn title bar; `Full` on Windows makes Avalonia draw its own caption buttons. macOS needs `Full` for the native traffic lights.
- The title bar is drawn by `MainView` (localized UI never sits on `MainWindow`, see [localization.md](./localization.md)). Its root has `WindowDecorationProperties.ElementRole="TitleBar"` (native drag, double-click maximize).
- Chrome hit-testing uses the nearest ancestor with a role, so every interactive control inside the title bar sets a role: `User` for app controls (nav tabs, language button), `MinimizeButton` / `MaximizeButton` / `CloseButton` for caption buttons (the maximize role gives Windows 11 the snap-layout flyout). Without `User`, a click on a tab starts a window drag.
- macOS: custom caption buttons hidden and the title text inset for the traffic lights (`OnPlatform` in `MainView.axaml`).
- `MainWindow` sets `MinWidth`/`MinHeight` so title-bar nav and page sidebars never overlap.

## Navigation

Reference: `PzLauncher.Desktop/ViewModels/MainViewModel.cs` (`CurrentPageKind`, `CurrentPage`), `PzLauncher.Desktop/ViewModels/ShellPage.cs`.

- Pages are an enum (`ShellPage`); `MainViewModel` holds the selected value and maps it to the page ViewModel with an exhaustive switch. Page ViewModels are singletons, so page state survives tab switches and the `MainView` rebuild on a language switch.
- Selectable items (title-bar tabs, sidebar items, option radios) are `RadioButton`s bound two-way through `IsChecked` to the enum with `EnumMatchConverter` (`PzLauncher.Desktop/Views/Controls/EnumMatchConverter.cs`). Don't drive selection with a `Command`: a control can become checked without its command running (UI Automation, keyboard), which leaves a highlighted tab over the wrong page. The converter writes back only for the item becoming checked, so unchecking and view teardown never write `null`/a stale value.
- A page asks the shell to navigate through an event the shell handles (`HomeViewModel.SelectVersionRequested`), not by referencing `MainViewModel` (that would be a DI cycle).

## Page sidebars

Reference: `PzLauncher.Desktop/Views/Home/HomeView.axaml` (content sidebar, right), `PzLauncher.Desktop/Views/Installs/InstallsView.axaml` and `PzLauncher.Desktop/Views/Settings/SettingsView.axaml` (nav sidebars, left).

- A sidebar is page content, not a shell host: each page view is a two-column `Grid` whose `Auto` column is the sidebar, so its width comes from its content. Nav sidebars go left (`Auto,*`); Home's content sidebar goes right (`*,Auto`).
- Sidebar container: `Border` with classes `sidebar left` or `sidebar right` (background plus the divider on the edge facing the content). Group headers: `TextBlock.sidebarHeader`. Items: `NavItem` (`PzLauncher.Desktop/Views/Controls/NavItem.cs`, icon + label; its default theme is the sidebar item with the accent bar when checked; `Shell.TitleBarTab` restyles it as a title-bar pill).
- The selected sidebar item is an enum on the page ViewModel (`InstallsFilter`, `SettingsSection`), bound as in Navigation.
- Lists that are filtered: bind `SelectedItem` to a guard property, never to the real selection. Reference: `InstallsViewModel.ListSelection` is computed from `SelectedInstall` and the visible list and ignores `null`, because a `ListBox` pushes `null` when the filter hides the selected row and when a rebuilt view's old `ListBox` is torn down.
- Dropdowns that change app state (language menu): command per row (`LanguageOption.SelectCommand`), `IsChecked` one-way. No two-way binding, so a closing or torn-down popup can't write back.

## Theming

Reference: `PzLauncher.Desktop/Themes/ShellTheme.axaml` (colors), `PzLauncher.Desktop/Themes/ShellStyles.axaml` (control themes and sidebar styles), `PzLauncher.Desktop/App.axaml`.

- Every shell color is a `Shell.*` brush in `ShellTheme.axaml`, defined once per `Light`/`Dark` theme dictionary. Views reference them with `{DynamicResource}` only; no color literals in views. Add a key to both variants, and check text contrast in both (dark's selected sidebar text uses a lighter red than `Shell.Accent` for that reason).
- The one literal outside `ShellTheme.axaml` is the Fluent `ColorPaletteResources` accent in `App.axaml` (Fluent needs a color there); keep it equal to `Shell.Accent`.
- The app's theme does not follow the OS: `App.axaml` sets `RequestedThemeVariant="Light"`, and the startup theme comes from preferences. ViewModels change it only through `IThemeService` (`PzLauncher.Desktop/Services/IThemeService.cs`; Avalonia implementation `AvaloniaThemeService`). The user picks it in 设置 → 外观 (`SettingsViewModel.Theme`).
- `AppTheme` (`PzLauncher.Desktop/Themes/AppTheme.cs`) maps to persisted names exhaustively; a new member without a name is a build error.

## Tests

- Navigation, filter/selection guards, theme and language choice: ViewModel tests with fakes (`FakeThemeService`, `FakePreferenceStore` in `PzLauncher.Desktop.Tests/TestSupport/Fakes.cs`). References: `PzLauncher.Desktop.Tests/ViewModels/Installs/InstallsViewModelTests.cs`, `.../Settings/SettingsViewModelTests.cs`, `.../LanguageMenuViewModelTests.cs`.
- Window chrome (drag, resize, snap, traffic lights) has no automated test; check it manually on each OS after changing `MainWindow`/the title bar.
