# Design: remember window placement

## Persisted contract (`preferences.json`)

New optional field `window`:

| Key | Type | Meaning |
|---|---|---|
| `x`, `y` | int | Normal-state window top-left, physical screen pixels (`Window.Position`) |
| `width`, `height` | number | Normal-state client size, DIPs (`Window.Width`/`Height`) |
| `maximized` | bool | Reopen maximized |

The whole object is invalid (loads as `null`, logged) if any member is missing, a size is not finite or not > 0. Written camelCase via `DesktopJsonContext`; absent when never saved (`WhenWritingNull`). Existing keys unchanged.

## Types and boundaries

- `Services/WindowPlacement.cs`: `sealed record WindowPlacement(int X, int Y, double Width, double Height, bool IsMaximized)`. Primitive fields only, so the store stays free of Avalonia types and the JSON shape is explicit.
- `UserPreferences` gains `WindowPlacement? Window`; `IPreferenceStore` gains `Task SaveWindowPlacementAsync(WindowPlacement placement, CancellationToken ct = default)` with the same never-throws / keeps-other-fields contract. `PreferencesFile` gains a nullable `WindowFile` record.
- Placement rules (pure, unit-tested): a static class under `Views/` that takes the saved placement, the screens' working areas and scaling, the minimum size and the default size, and returns either "use these bounds (+ maximized)" or "center with this size (+ maximized)". Visibility rule: the top 44 DIP title-bar strip of the saved rect must overlap some working area by at least ~100×20 DIP; otherwise center on the primary screen. Size is clamped to the minimum and to the chosen screen's working area (converted with that screen's `Scaling`).
- Window wiring: a small view-layer helper (e.g. `Views/WindowPlacementPersistence.cs`) attached from `App.axaml.cs` after creating `MainWindow`, given the window, `IPreferenceStore` and `UserPreferences.Window`. It:
  - applies the resolved placement before `Show` (`WindowStartupLocation.Manual` + `Position` + `Width`/`Height`, then `WindowState`); falls back to the XAML defaults (`CenterScreen`) when nothing usable is saved;
  - tracks the last normal bounds on `PositionChanged` / `Resized` while `WindowState == Normal`, and the last non-minimized state;
  - on the first `Closing`, cancels, awaits `SaveWindowPlacementAsync` (bounded by a ~2 s `CancellationTokenSource`), then closes again; the second `Closing` passes through.
- `MainWindow` itself is unchanged except for whatever the helper needs; no ViewModel involvement (window placement is a view concern; ViewModels stay Avalonia-free).

## Reset from Settings (R10–R12)

- `Services/IWindowPlacementService.cs`: `void ResetToDefault()`. ViewModels are Avalonia-free and reach windows only through `Services/` interfaces (`BannedSymbols.txt`, `directory-structure.md`).
- Implementation in the view layer, backed by `WindowPlacementPersistence` (it already owns the window, the default size and the rules): registered in `App.axaml.cs` the same way `AvaloniaFolderPicker` gets `desktop.MainWindow` lazily. Default size = the window's XAML `Width`/`Height`, captured in `Attach` before any saved placement is applied (single source). Centering reuses `WindowPlacementRules` with the working area of `Screens.ScreenFromWindow(window)` (primary if null).
- Reset order: `WindowState = Normal`, then size, then `Position`; the existing normal-bounds tracking picks up the result, so the close-time save needs no change.
- `SettingsViewModel` gets `[RelayCommand] ResetWindowSize` calling the service. `SettingsView` adds, under the theme radios, a "Window" heading (same style as the Theme heading) and a button.
- Resx keys: `Settings_Window`, `Settings_ResetWindowSize`, in every `UiText*.resx` in key order.

## Trade-offs

- Save on close only (not continuously): one write per run; a crash loses the last placement (out of scope).
- Awaited close-time save is a deliberate exception to "callers fire and forget saves": a fire-and-forget save at exit can be cut off when the process ends. Recorded in `preferences.md`.
- Mixed-DPI monitors: position is physical pixels, size is DIPs; the OS rescales the window when it lands on a monitor with different scaling, which is the expected behaviour.
- Wayland ignores `Position`; size and maximized still apply.

## Compatibility / rollback

- Old files load unchanged (`window` absent → `null`). A newer file read by an older build ignores the unknown `window` key (System.Text.Json default), so rollback is safe.
- Rollback = revert the helper wiring in `App.axaml.cs`; the stored field is harmless.
