# Preferences

> User choices saved across restarts (language, theme). One file, one store.

---

## Store

Reference: `PzLauncher.Desktop/Services/IPreferenceStore.cs`, `PzLauncher.Desktop/Services/JsonPreferenceStore.cs`; composed in `PzLauncher.Desktop/App.axaml.cs`.

- File: `<ApplicationData>/PzLauncher/preferences.json`.
- `Load()` is synchronous (it runs before the first window exists) and returns `UserPreferences`; each field is `null` when missing, unreadable, or an unknown value (unknown values are logged as warnings). Callers fall back per field: language → `AppLanguages.FromCulture(CurrentUICulture)`, theme → `AppTheme.Light`.
- One save method per field (`SaveLanguageAsync`, `SaveThemeAsync`). Each rewrites the whole file from one in-memory record, under one save gate, via temp file + rename. So saving one field never drops another, the last choice wins when saves overlap, and a crash never leaves a truncated file.
- Saves never throw for I/O; failures are logged. Callers fire and forget, and a failed save must not undo the applied choice (references: `LanguageMenuViewModel`, `SettingsViewModel`).

## File contract (persisted: never change existing values)

| Field | Values | Owner of the mapping |
|---|---|---|
| `language` | culture name, e.g. `zh-Hans` | `AppLanguages.CultureName` / `FromCultureName` |
| `theme` | `light`, `dark` | `AppThemes.PreferenceName` / `FromPreferenceName` |

- Field names are camelCase via `DesktopJsonContext` (source-generated, AOT). Unknown JSON properties are ignored on read, so older builds keep reading newer files.
- Adding a preference: a nullable field in the file record and in `UserPreferences`, a `Save<Field>Async`, a stable string mapping (never enum names), and tests below.

## Tests

Reference: `PzLauncher.Desktop.Tests/Services/JsonPreferenceStoreTests.cs`. Per field: round-trip; missing file / missing field → `null`; unknown value → `null` without throwing; saving it keeps the other fields (either order, and concurrent saves); unwritable target doesn't throw.
