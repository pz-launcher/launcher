# Preferences

> Choices saved across restarts. Reference: `PzLauncher.Desktop/Services/IPreferenceStore.cs`, `JsonPreferenceStore.cs`.

---

## Rules

- `<ApplicationData>/PzLauncher/preferences.json` is read and written only through `IPreferenceStore`.
- A missing or unknown value loads as `null`; the caller picks the fallback (`App.axaml.cs`).
- Callers fire and forget saves; a failed save never undoes the applied choice.

## File contract (persisted: never change an existing value)

| Field | Values | Mapping |
|---|---|---|
| `language` | culture name, e.g. `zh-Hans` | `AppLanguages.CultureName` |
| `theme` | `light`, `dark` | `AppThemes.PreferenceName` |

A new preference gets a nullable field, its own save method and a stable string mapping (never an enum name).

## Tests

Reference: `PzLauncher.Desktop.Tests/Services/JsonPreferenceStoreTests.cs`. Each field: round-trip, missing, unknown value, saving it keeps the other fields, unwritable file.
