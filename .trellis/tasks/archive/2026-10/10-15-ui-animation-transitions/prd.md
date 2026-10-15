# Add UI animation transitions

## Goal

Make the launcher shell feel smoother: page switches and shell control state changes animate instead of snapping.

## Background

- Avalonia 12.1.4 + FluentTheme. `PzLauncher.Desktop` has no `Transitions`, animations or page transitions today.
- Page switch: `PzLauncher.Desktop/Views/MainView.axaml` shows pages through a plain `ContentControl` bound to `MainViewModel.CurrentPage`; views come from the `App.axaml` DataTemplates. Each switch builds a fresh view, and page views call `ActivateCommand` from `OnLoaded`.
- Shell control themes in `PzLauncher.Desktop/Themes/ShellStyles.axaml` change instantly on `:pointerover` / `:checked`:
  - `NavItem` (sidebar): `Background`, `Foreground`, `PART_Indicator` `Opacity`.
  - `Shell.TitleBarTab`: `Background`, `Foreground`, `FontWeight`.
  - `Shell.CaptionButton`: `Background`, `Foreground` (incl. the red close hover).
- Fluent's own controls (Button, RadioButton, ListBoxItem, ...) already animate their states.
- Language switch rebuilds the whole `MainView` (`PzLauncher.Desktop/App.axaml.cs:82`); theme switch swaps `Shell.*` DynamicResource brushes.
- Window chrome: `PzLauncher.Desktop/Views/MainWindow.axaml` uses `ExtendClientAreaToDecorationsHint` with `WindowDecorations` `BorderOnly` (Windows/Linux) / `Full` (macOS). In Avalonia 12.1.4 `WindowImpl.UpdateWindowProperties`, `BorderOnly` sets only `WS_BORDER` (`Full` adds `WS_CAPTION | WS_SYSMENU`), and DWM skips minimize/maximize/restore/close animations for windows without a caption. Avalonia's extended-client-area `WM_NCCALCSIZE` handling reads the real window style, so a caption added through the public `Win32Properties.AddWindowStylesCallback` is not drawn.
- `MainWindow` sets no `WindowStartupLocation`, so it opens at the OS default (cascaded) position instead of centered.
- Avalonia provides `TransitioningContentControl` with a `PageTransition` (`IPageTransition.Start(from, to, forward, ct)`), and per-property `Transitions` (`BrushTransition`, `DoubleTransition`).

## Decisions

- Scope (user, 2026-10-15): page switch + shell control states (`NavItem`, `Shell.TitleBarTab`, `Shell.CaptionButton`). In-page `IsVisible` toggles are out of scope.
- Window animations (user, 2026-10-15): added to scope after the first implementation; on Windows, restore the caption style with `Win32Properties.AddWindowStylesCallback` adding `WS_CAPTION | WS_SYSMENU`. Cost is a bitwise OR per style update; DWM plays the animation.
- Centered startup (user, 2026-10-15): added to scope; `WindowStartupLocation="CenterScreen"`. Remembering window position/size is a separate task (needs a preference).
- Page-switch style (user, 2026-10-15): old page removed at once; new page fades in from transparent while rising ~8px, ~200ms. Chosen over crossfade (messy overlap between dissimilar layouts) and horizontal slide (too much motion for a launcher).

## Requirements

- R1 Page switch: when the page changes (title-bar tab, or Home "select version" jumping to Installs), the old page disappears at once and the new page fades in from transparent while rising ~8px into place, ~200ms, ease-out.
- R2 Rapid switching: a new switch interrupts the running one; the final page always ends fully opaque and in place.
- R3 No page animation when `MainView` is first shown: app launch and the language-switch rebuild show the page directly.
- R4 `NavItem`, `Shell.TitleBarTab` and `Shell.CaptionButton` animate `Background` and `Foreground` changes (hover and checked), ~150ms.
- R5 The sidebar `NavItem` selected indicator (`PART_Indicator`) fades in/out with the same timing.
- R6 `FontWeight` on the checked title-bar tab stays instant (not animatable).
- R7 Durations and easing are defined once, not repeated per control.
- R8 Native AOT stays clean (no new IL warnings); no reflection-based lookups.
- R9 On Windows, minimize, maximize, restore and close play the native OS window animations. The self-drawn title bar, edge resize, snap and drag behave as before; the native caption is never drawn. Alt+Space opening the system menu is an accepted side effect. macOS and Linux are unchanged.
- R10 The main window opens centered on the screen.

## Acceptance Criteria

- [ ] AC1 (R1) Clicking Home → Installs → Settings shows each new page fading/rising in; no frame shows two pages overlapping.
- [ ] AC2 (R2) Clicking tabs rapidly ends on the last clicked page, fully visible, with no leftover offset or transparency.
- [ ] AC3 (R3) Launching the app and switching language show the current page immediately, without the fade-in.
- [ ] AC4 (R4, R5) Hovering and selecting sidebar items, title-bar tabs and caption buttons (incl. close-button red) blend colors smoothly; the sidebar indicator fades.
- [ ] AC5 Page state survives switches as before (selected install, filter, settings section); existing tests pass.
- [ ] AC6 (R8) `dotnet build` with 0 warnings; Native AOT publish has 0 IL warnings.
- [ ] AC7 Behaviour checked by hand in light and dark theme.
- [ ] AC8 (R9) On Windows, minimize/maximize/restore (caption buttons, title-bar double-click, Win+Up/Down) and close animate; no native title bar or caption buttons appear; the maximized window has no gap or overhang at the screen edges; drag, snap and edge resize still work; the client area still shrinks to exactly `MinWidth`×`MinHeight` (820×520 DIP).
- [ ] AC9 (R10) Launching the app opens the window centered on the current screen.

## Out of Scope

- In-page `IsVisible` toggles (empty states, Installs list, Home install card, Settings sections, error/warning text, progress bars).
- Animating the theme switch itself or the language rebuild.
- Remembering the window position and size between runs.
- Window animations on macOS (already native) and Linux (window-manager dependent).
- A user setting or OS "reduce motion" detection to disable animations.
- Restyling Fluent's built-in control animations.

## Risks

- Theme switch: the R4 brush transitions also run when `Shell.*` brushes change, so shell controls blend over ~150ms while the rest of the window snaps. Accepted as negligible at that duration.
- `TransitioningContentControl`'s handling of initial content must be verified on Avalonia 12 for R3.
- Phase 3 spec update: add the animation rules (where durations live, what animates) to `.trellis/spec/desktop/shell-and-theming.md`.
