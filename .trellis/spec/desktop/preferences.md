# Preferences

> Choices saved across restarts. Reference: `PzLauncher.Desktop/Services/IPreferenceStore.cs`, `JsonPreferenceStore.cs`.

---

## Rules

- `<ApplicationData>/PzLauncher/preferences.json` is read and written only through `IPreferenceStore`.
- A missing or unknown value loads as `null`; the caller picks the fallback (`App.axaml.cs`).
- Callers fire and forget saves; a failed save never undoes the applied choice.
- Exception: the window placement is saved when the main window closes, so the close waits for that save and for `IInstallOverridesService.FlushAsync` (bounded, never during OS shutdown). Reference: `PzLauncher.Desktop/Views/WindowPlacementPersistence.cs`.
- A value changed in rapid steps (slider, text typed per keystroke) goes through a service that applies it in memory at once and debounces the file write; that service is flushed on close. Reference: `PzLauncher.Desktop/Services/InstallOverridesService.cs`.
- Restoring a saved placement goes through `WindowPlacementRules` (`PzLauncher.Desktop/Views/WindowPlacementRules.cs`): off-screen bounds open centered on the primary screen, the size is clamped to the minimum and the screen.

## File contract (persisted: never change an existing value)

| Field | Values | Mapping |
|---|---|---|
| `language` | culture name, e.g. `zh-Hans` | `AppLanguages.CultureName` |
| `theme` | `light`, `dark` | `AppThemes.PreferenceName` |
| `javaRuntimes` | array of absolute Java home paths (manually added); a non-string or non-absolute entry is dropped, the rest kept | `UserPreferences.ManualJavaHomes` |
| `installs` | object: absolute install root → `{javaHome, maxMemoryMb, minMemoryMb, jvmArgs, gameArgs}` (absolute path or absent; int > 0 or absent, min ≤ max; strings); an invalid entry is dropped, the rest kept; a save replaces only its own key | `UserPreferences.InstallOverrides` (`GameLaunchOverrides`), looked up through `IInstallOverridesService` |
| `window` | `{x, y, width, height, maximized}`: normal-state position (int, physical px), client size (number > 0, DIPs), maximized (bool); invalid if any member is missing | `WindowPlacement` |

A new preference gets a nullable field, its own save method and a stable string mapping (never an enum name).

## Tests

Reference: `PzLauncher.Desktop.Tests/Services/JsonPreferenceStoreTests.cs`. Each field: round-trip, missing, unknown value, saving it keeps the other fields, unwritable file.
