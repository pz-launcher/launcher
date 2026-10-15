# Remember window position and size

## Goal

The main window reopens where the user left it: same position, size and maximized state. The first launch, or a saved position that no longer fits the connected screens, opens centered.

## Background

- `PzLauncher.Desktop/Views/MainWindow.axaml`: `Width="1000" Height="640" MinWidth="820" MinHeight="520"`, `WindowStartupLocation="CenterScreen"` (added by task `10-15-ui-animation-transitions`, same branch). Avalonia's `CenterScreen` centers on the primary screen.
- On Windows `MainWindow.axaml.cs` adds `WS_CAPTION | WS_SYSMENU` and forwards `WM_NCPAINT`; the outer window rect is 1px shorter at the top than plain `BorderOnly`. `Position` (window top-left, physical pixels) and `Width`/`Height` (client size, DIPs) still round-trip consistently.
- Preferences: `.trellis/spec/desktop/preferences.md`. `preferences.json` is read/written only through `IPreferenceStore`; `Load()` is synchronous before the first window; a missing/unknown value loads as `null` and the caller picks the fallback; saves are fire-and-forget, never throw, and keep the other fields. Existing fields `language`, `theme` must not change.
- `App.axaml.cs` creates `MainWindow` after `IPreferenceStore.Load()`; the app exits when the main window closes (`desktop.Exit` disposes the provider).
- Avalonia 12: `Window.Position` (`PixelPoint`), `PositionChanged`, `Resized`, `WindowState`, `Closing` (cancellable), `Screens.All` / `Screen.WorkingArea` (`PixelRect`) / `Screen.Scaling`, `WindowStartupLocation.Manual` uses `Position`.

## Decisions

- Reset in Settings (user, 2026-10-15): a "Window" subsection in Settings › Appearance with a "Reset window size" button. It applies immediately: un-maximize, default size, centered on the screen the window is on. Chosen over "applies next launch" (the close-time save would overwrite it, confusing) and a separate sidebar section (empty for one button).

## Requirements

- R1 On close, the window's normal (restored) bounds and whether it was maximized are saved to `preferences.json`.
- R2 While maximized or minimized, the saved bounds are the last normal bounds, not the maximized/minimized ones. Closing while minimized saves the state the window had before minimizing (maximized or normal).
- R3 On launch, saved bounds are applied before the window is shown (no visible jump), and the window is maximized if it was maximized; restoring from maximized returns to the saved normal bounds.
- R4 Saved bounds are used only if the window's title-bar strip is visible on a connected screen (it intersects some screen's working area by at least a usable area); otherwise the window opens centered on the primary screen with the saved size.
- R5 The restored size is clamped to `MinWidth`/`MinHeight` and to the target screen's working area.
- R6 No saved placement, or an unreadable/invalid one (missing fields, non-positive size, non-finite numbers), behaves exactly like today: default size, centered.
- R7 The save completes before the process exits; a failed save is logged and never blocks closing.
- R8 `preferences.json` stays backward compatible: one new optional field; files without it load as today; saving it keeps `language`/`theme`, and saving those keeps it.
- R10 Settings › Appearance has a "Window" subsection with a "Reset window size" button. Clicking it immediately leaves maximized/minimized state, sets the default size (the `MainWindow.axaml` `Width`/`Height`, 1000×640 DIP, defined once) and centers the window on the screen it is currently on (its working area).
- R11 After a reset, closing saves the reset placement as usual; nothing else in `preferences.json` changes.
- R12 Both new texts (section heading, button) are in every `UiText*.resx`, translated per language.
- R9 Behaviour is the same on Windows, macOS and Linux X11. On Wayland, where apps cannot set their position, only size and maximized state are restored.

## Acceptance Criteria

- [ ] AC1 (R1, R3) Move and resize the window, close, relaunch: it reopens at the same place and size.
- [ ] AC2 (R2, R3) Maximize, close, relaunch: it opens maximized; un-maximizing returns to the pre-maximize bounds. Same after minimizing a maximized window and closing it from the taskbar.
- [ ] AC3 (R4) A placement saved on a monitor that is later disconnected (or edited to far off-screen coordinates) opens centered on the primary screen.
- [ ] AC4 (R5, R6) A saved size larger than the screen or below the minimum is clamped; a corrupt `window` value opens with the default size, centered, and is logged.
- [ ] AC5 (R8) Store tests per the preferences spec: round-trip, missing, invalid value, saving it keeps the other fields (and vice versa), unwritable file.
- [ ] AC6 (R4, R5) Pure placement logic (visibility check, clamping) is unit-tested without a real window.
- [ ] AC9 (R10) From a moved, resized or maximized window, clicking "Reset window size" gives a 1000×640 DIP window centered on the same screen, in normal state.
- [ ] AC10 (R11) After a reset, close and relaunch: the window opens at the reset size and position.
- [ ] AC11 (R10, R12) `SettingsViewModel` test: the reset command calls the window service once; resx parity tests pass for all languages; the centering for a given screen is covered by the placement-rules tests.
- [ ] AC7 `dotnet build` 0 warnings, all tests pass, Native AOT publish 0 IL warnings.
- [ ] AC8 Manual check on Windows at the current DPI, plus a second monitor if available.

## Out of Scope

- Remembering placement per monitor configuration, or of any window other than the main window.
- Saving placement continuously while the app runs (crash recovery).
- A setting to turn the feature off.
- Resetting position without size, or choosing a size other than the default.
- Full-screen state.
