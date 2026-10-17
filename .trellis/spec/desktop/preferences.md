# Preferences

> Choices saved across restarts. Reference: `PzLauncher.Desktop/Services/IPreferenceStore.cs`, `JsonPreferenceStore.cs`.

---

## Rules

- `<ApplicationData>/PzLauncher/preferences.json` is read and written only through `IPreferenceStore`.
- A missing or unknown value loads as `null`; the caller picks the fallback (`App.axaml.cs`).
- Callers fire and forget saves; a failed save never undoes the applied choice.
- Exception: the window placement is saved when the main window closes, so the close waits for that save, for pending install-override and collection-order writes, and for a staged launcher update to install (bounded, never during OS shutdown). Reference: `PzLauncher.Desktop/Views/WindowPlacementPersistence.cs`, `PzLauncher.Desktop/Services/ModCollectionOrderWriter.cs`.
- A value that changes per slider step or keystroke debounces its file write. Reference: `PzLauncher.Desktop/Services/InstallOverridesService.cs`.
- Restoring a saved placement goes through `WindowPlacementRules` (`PzLauncher.Desktop/Views/WindowPlacementRules.cs`): off-screen bounds open centered on the primary screen, the size is clamped to the minimum and the screen.

## File contract (persisted: never change an existing value)

| Field | Values | Mapping |
|---|---|---|
| `language` | culture name, e.g. `zh-Hans` | `AppLanguages.CultureName` |
| `theme` | `light`, `dark` | `AppThemes.PreferenceName` |
| `javaRuntimes` | array of absolute Java home paths (manually added); a non-string or non-absolute entry is dropped, the rest kept | `UserPreferences.ManualJavaHomes` |
| `installs` | object: absolute install root → `{javaHome, maxMemoryMb, minMemoryMb, jvmArgs, gameArgs, gc}` (`gc`: `g1`, `zgc`, `shenandoah`, `parallel`, `serial`; unknown loads as null); an invalid entry is dropped, the rest kept | `UserPreferences.InstallOverrides` |
| `zombieBuddyChannels` | object: absolute install root → `stable` or `beta`; an unknown value or non-absolute key is dropped, the rest kept | `UserPreferences.ZombieBuddyChannels` |
| `defaultModCollections` | object: absolute install root → non-empty collection name (the current collection, applied before each launch); a non-string, empty or non-absolute entry is dropped, the rest kept | `UserPreferences.DefaultModCollections` |
| `updateSource` | `auto`, `github`, `gitee`; anything else loads as null (Auto) | `UpdateSourcePreferences.PreferenceName` |
| `autoCheckUpdates` | `true`, `false`; anything else loads as null (on) | `UserPreferences.AutoCheckUpdates` |
| `window` | `{x, y, width, height, maximized}`: normal-state position (int, physical px), client size (number > 0, DIPs), maximized (bool); invalid if any member is missing | `WindowPlacement` |

A new preference gets a nullable field, its own save method and a stable string mapping (never an enum name).

## Tests

Reference: `PzLauncher.Desktop.Tests/Services/JsonPreferenceStoreTests.cs`. Each field: round-trip, missing, unknown value, saving it keeps the other fields, unwritable file.
