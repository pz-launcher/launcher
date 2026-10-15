# Home shell: title-bar nav + sidebar

## Goal

Turn the main window into a PCL-style launcher shell: the top navigation bar is merged into the window title bar (no separate OS title bar), and pages use a sidebar next to a large content area — a content sidebar on the right of Home (selected install + primary actions) and categorized navigation sidebars on the left of 安装 and 设置. Default look follows Project Zomboid's palette instead of PCL's blue.

Reference: PCL screenshot supplied by the user (title bar = app name left, pill nav tabs centered, caption buttons right; home = sidebar with identity, big 启动游戏 button, 版本选择/版本设置 buttons, plus a background-only content area). Palette mockups A (dark) / B (light) were shown in the planning conversation.

## Background (current code)

- `PzLauncher.Desktop/Views/MainWindow.axaml`: OS decorations, 900×600, hosts one `MainView`.
- `PzLauncher.Desktop/Views/MainView.axaml`: top strip with only the language `ComboBox`, then `ContentControl Content="{Binding CurrentPage}"`.
- `PzLauncher.Desktop/ViewModels/MainViewModel.cs`: `CurrentPage` is a fixed `InstallsViewModel`; language selection lives here. No navigation.
- Only page: Installs (`PzLauncher.Desktop/Views/Installs/InstallsView.axaml`); its scan starts from `InstallsView.OnLoaded` → `InstallsViewModel.ActivateCommand` (run once).
- A language switch replaces `MainView` (`App.RebuildMainView`) around the same view models, so page/selection state must live in view models; localized UI belongs in `MainView` or below, never on `MainWindow` (`.trellis/spec/desktop/localization.md`).
- Theme: `FluentTheme`, `RequestedThemeVariant="Default"` (follows OS).
- Preferences: `PzLauncher.Desktop/Services/JsonLanguagePreferenceStore.cs` stores only `{ "language": "<culture name>" }` (`DesktopJsonContext.LanguagePreference`) and rewrites the whole file on each save; the culture name is a persisted contract. Icons: `FluentIcons.Avalonia`. Avalonia 12.1.4, AOT-compatible.
- No game-launch capability and no account system exist.

## Requirements

### R1 Title bar (merged with window chrome)
- The OS title bar is replaced by the app's own title bar row: product name "PzLauncher" on the left, nav tabs centered, language button (R5a) and caption buttons on the right.
- Dragging empty title-bar space moves the window; double-click toggles maximize; nav tabs and caption buttons stay clickable.
- Caption buttons: minimize, maximize/restore, close. The window stays resizable from its edges, with a minimum size that keeps nav and sidebar from overlapping.
- macOS: native traffic-light buttons stay (left), the custom caption buttons are hidden, and the title-bar content leaves room for them. Linux: same custom chrome as Windows.

### R2 Navigation
- Exactly three tabs: 启动 (Home), 安装 (Installs), 设置 (Settings). No placeholder pages.
- The selected tab is visibly highlighted (pill style). App opens on 启动.
- The selected page survives a language switch (user stays on 设置 after changing language).

### R3 Home page
- Content area on the left, sidebar on the right (unlike PCL). The content area is background only in this task.
- Sidebar shows the selected install: kind, Steam branch/build when present, root path. Default selection is the first detected install; a clear empty state when none is detected.
- Large 启动游戏 primary button, disabled (no launch capability yet).
- 版本选择 button navigates to the 安装 tab.
- The install scan runs when the app starts (Home is the start page), not only when 安装 is first opened; still runs once.

### R3a Page sidebar pattern (shell-wide)
- Any page may have its own sidebar; the sidebar is a shared layout, not Home-only. Side per page: Home = right (content sidebar), nav sidebars (安装, 设置) = left.
- Sidebar width is sized by its content (so Home's sidebar and a nav-list sidebar differ in width), not one fixed width for all pages.
- A navigation sidebar supports categories: optional group header (muted small text, e.g. PCL's "社区资源") followed by items (icon + label); ungrouped items allowed at the top. The selected item is highlighted (accent left bar + tinted background + accent text), per the second PCL screenshot (download page).
- The selected sidebar item per page lives in that page's view model, so it survives tab switches and language switches within the session.

### R4 Installs page: filter sidebar + selection
- Nav sidebar: ungrouped 全部 at the top, then group 类型 with 游戏 and 专用服务器 (one item per `PzInstallKind`). Default 全部. The list on the right shows only installs of the chosen kind; a kind with no installs shows an empty state for that filter.
- The page's existing header (title, rescan, add folder), warnings and errors stay in the content area.
- Clicking an install on the 安装 page makes it the selected install shown on Home.
- After a rescan the selection is kept if that install (same root path) still exists, otherwise falls back to the first install, or none.
- Selection lasts for the session only (not persisted). Changing the filter never changes or clears the selected install, even when the filter hides it.

### R5 Settings page
- Nav sidebar: group 启动器 with one item 外观 (selected by default); the content area shows the chosen section.
- 外观 section: heading 主题 with two radio options 浅色 / 深色 reflecting the current theme; choosing one switches immediately and saves (R6). Later custom-color settings go in this section.

### R5a Language button (title bar)
- A globe icon button in the title bar, left of the caption buttons (localized tooltip/automation name). Clicking it opens a dropdown listing every `AppLanguage` by native name, the current one checked; choosing one switches immediately and saves the preference (behaviour of the old picker unchanged).

### R6 Visual style
- PZ-flavoured palette with two variants: light (B: deep-red title bar, warm off-white sidebar, warm grey content) and dark (A: charcoal title bar/surfaces, blood-red accent, parchment text).
- The app starts in light; it does not follow the OS theme. The theme is chosen in 设置 → 外观 (R5) and applies immediately to the whole app including Fluent controls. There is no theme button in the title bar.
- The chosen theme is saved in the existing preferences file (`%APPDATA%/PzLauncher/preferences.json`) and restored on the next start; no saved/unreadable value → light. Existing preference files (language only) keep working, and saving the theme never loses the saved language (and vice versa).
- Every shell color is a named theme resource defined in one place (with Light/Dark variants), never a literal in a view, so a later custom-color feature can override them.

### R7 Localization
- New texts (nav labels, sidebar labels/buttons, sidebar group headers and items, per-filter empty state, empty state, settings title, caption button and language button tooltips/automation names, 外观/主题/浅色/深色) exist in all 10 `UiText*.resx` files.

## Acceptance Criteria

- [ ] AC1 (R1) Window shows no OS title bar on Windows; the custom bar drags the window, double-click maximizes/restores, minimize/maximize/close work, edges resize, minimum size is enforced.
- [ ] AC2 (R2) Exactly three tabs; clicking each shows its page with the tab highlighted; app starts on 启动.
- [ ] AC3 (R2/R5a) Switching language from the title-bar globe dropdown keeps the current page (e.g. 设置) open and re-renders every shell text in the new language; the dropdown shows the new current language checked.
- [ ] AC4 (R3) With installs detected, Home shows the first install's kind/branch/build/path without visiting 安装; with none, Home shows the empty state.
- [ ] AC5 (R3/R4) Selecting another install on 安装 updates Home; 版本选择 opens 安装; 启动游戏 is disabled.
- [ ] AC6 (R4) Rescan keeps a still-present selection and falls back to the first install otherwise; filter changes never alter the selection (covered by ViewModel tests).
- [ ] AC6a (R3/R3a/R4/R5) Home's sidebar is on the right; 安装 and 设置 show a left nav sidebar with group headers and a highlighted selected item; 安装's filter limits the list by kind; Home's sidebar and the nav sidebars have content-determined (different) widths; each page's sidebar selection is kept when switching tabs and languages.
- [ ] AC7 (R6) App starts in palette B regardless of the OS theme; choosing 深色/浅色 in 设置 → 外观 switches the whole app to palette A and back instantly; no color literals in shell views.
- [ ] AC7a (R6) After choosing 深色 and restarting, the app opens in dark; a language-only preference file from the previous version loads with its language and light theme; saving theme then language (or the reverse) leaves both in the file (store tests).
- [ ] AC8 (R7) Resx parity test passes for all languages; build has no new warnings; existing tests pass.

## Out of Scope

- Game launching (button is disabled only), accounts/profiles, 版本设置, 下载/更多 tabs, home content area content (news, background images).
- Custom/user-selectable colors and PCL-style personalization settings — deferred until the user shares PCL's personalization config; this task only prepares overridable theme resources.
- Persisting the selected install or the last opened tab across restarts.
- Restyling the Installs list items beyond adding the filter sidebar and selection.
- More settings sections (personalization etc.) — added by later tasks into the 设置 sidebar.
