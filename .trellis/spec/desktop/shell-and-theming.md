# Shell, Navigation and Theming

> Title bar, page navigation, page sidebars, colors. Saved choices: [preferences.md](./preferences.md).

---

## Window chrome

Reference: `PzLauncher.Desktop/Views/MainWindow.axaml`, `PzLauncher.Desktop/Views/MainView.axaml`.

- The title bar lives in `MainView`, never on `MainWindow`.
- Every interactive control in the title bar sets `WindowDecorationProperties.ElementRole` (`User`, or the caption-button role); without one, clicking it drags the window.
- On Windows, `MainWindow` adds `WS_CAPTION | WS_SYSMENU` back to the `BorderOnly` style (`Win32Properties.AddWindowStylesCallback`) so DWM plays the minimize/maximize/restore/close animations. It must also forward `WM_NCPAINT` to `DefWindowProc` (`AddWndProcHookCallback`): Avalonia swallows that message for non-`Full` decorations, and with a caption DWM then drops the shadow, border and rounded corners. The same hook computes `WM_GETMINMAXINFO`'s minimum track size: Avalonia's version counts the caption as non-client, so the client minimum would grow by the caption height. Keep or remove all three together.
- After changing `MainWindow` or the title bar, check drag, resize, snap, the window animations and the macOS traffic lights by hand on each OS; there is no automated test.

## Navigation

Reference: `MainViewModel.CurrentPageKind`, `ShellPage`, `PzLauncher.Desktop/Views/Controls/EnumMatchConverter.cs`.

- Tabs, sidebar items and option radios bind `IsChecked` to an enum through `EnumMatchConverter`. Never select through a `Command`.
- Page state lives in the page ViewModels (singletons), never in views.
- A page asks for navigation through an event the shell handles, never by referencing `MainViewModel`. Reference: `HomeViewModel.ChooseInstallRequested`.
- A sub-page keeps the tab it was opened from highlighted, and has a back button; back and clicking that tab return to the page it was opened from. Reference: `ShellPage.LaunchSettings`, `MainViewModel.CurrentTab`.
- A list whose row click acts on the row uses `Shell.ListRow` button rows, never a selection; another action on the row is a sibling button, never nested in the row. Reference: `PzLauncher.Desktop/Views/Installs/InstallsView.axaml`.
- A sortable column header is a `Shell.ColumnHeader` button whose command sets the sort on the page ViewModel (column enum + direction, `SortedRows`); the arrow shows only on the sorted column. Reference: `PzLauncher.Desktop/Views/Mods/InstalledModsView.axaml`, `InstalledModsViewModel.SortByCommand`.

## Page sidebars

Reference: `PzLauncher.Desktop/Views/Home/HomeView.axaml` (content sidebar), `PzLauncher.Desktop/Views/Installs/InstallsView.axaml` and `PzLauncher.Desktop/Views/Settings/SettingsView.axaml` (nav sidebars).

- A sidebar is page content in an `Auto` grid column: nav sidebars on the left, content sidebars on the right.
- Build them from `Border.sidebar` (`left`/`right`), `TextBlock.sidebarHeader` and `NavItem` (`PzLauncher.Desktop/Themes/ShellStyles.axaml`).
- The selected sidebar item is an enum on the page ViewModel.
- A selector whose items can be replaced binds `SelectedItem` through a guard property that ignores `null`, never to the real selection. Reference: `LaunchSettingsViewModel.JavaSelection`.
- Menus that change app state use a command per row and one-way `IsChecked`. Reference: `LanguageMenuViewModel`, `LanguageOption`.

## Theming

Reference: `PzLauncher.Desktop/Themes/ShellTheme.axaml`, `PzLauncher.Desktop/App.axaml`.

- Shell colors are `Shell.*` brushes with a `Light` and a `Dark` value each (shadows: `Shell.*` `BoxShadows`, e.g. `Shell.LiftedShadow`). Views use `{DynamicResource}`; no color literals in views.
- The Fluent accent in `App.axaml` equals `Shell.Accent`.
- Page titles, section titles, error and warning text and hints use the `TextBlock` classes `pageTitle`, `sectionTitle`, `error`, `warning`, `hint` (`PzLauncher.Desktop/Themes/ShellStyles.axaml`). Never repeat their size, weight or color inline or in a view's local styles; a local style may only add a property (LaunchSettings' 12 px `hint`). `error`/`warning` don't wrap: each site sets `TextWrapping`.
- ViewModels change the theme only through `IThemeService`.

## Motion

Reference: `PzLauncher.Desktop/Themes/ShellStyles.axaml` (timing resources, control transitions), `PzLauncher.Desktop/Views/Controls/FadeRiseTransition.cs`, `PzLauncher.Desktop/Views/Controls/PremultipliedBrushTransition.cs`.

- Durations and easing are the `Shell.*Duration` / `Shell.MotionEasing` resources in `ShellStyles.axaml`; never write a literal duration on a control.
- Pages switch through the `TransitioningContentControl` in `MainView` with `FadeRiseTransition`. The first content after `MainView` loads (launch, language rebuild) is not animated.
- Shell control themes animate brushes with `PremultipliedBrushTransition`, never `BrushTransition`: the built-in one blends straight alpha, so a fade from `Transparent` (`#00FFFFFF`) flashes light in dark theme.
- In-page `IsVisible` toggles are not animated.
- A drag reorder lifts a copy of the row into an overlay and slides the other rows with render-transform transitions; the list itself changes once, after the drop settles. Reference: `PzLauncher.Desktop/Views/Mods/ModCollectionsView.axaml.cs`, geometry in `ListReorderLayout.cs`.
- A reorderable list is virtualized with a fixed row height: the drag geometry comes from index × row height and the scroll offset, never from containers. Only realized containers get offsets: set on `ContainerPrepared` without a transition, reset on `ContainerClearing` and when the drag ends.
