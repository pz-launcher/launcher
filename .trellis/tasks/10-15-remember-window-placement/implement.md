# Implementation plan

1. Store contract: `WindowPlacement` record, `UserPreferences.Window`, `PreferencesFile.Window` (+ `WindowFile`), validation on load with a `LoggerMessage` for invalid values, `SaveWindowPlacementAsync`. Register any new type in `DesktopJsonContext`.
2. Store tests in `JsonPreferenceStoreTests`: round-trip, written JSON shape, missing, invalid (missing member, zero/negative/NaN size), saving it keeps `language`/`theme`, saving those keeps it, unwritable file. Update existing `UserPreferences` constructions.
3. Pure placement rules + tests (visible on a screen, off every screen → center on primary, multi-screen with negative coordinates, clamp to min and to working area, maximized flag carried through, no saved placement → default).
4. View helper: apply before show, track normal bounds and last non-minimized state, awaited bounded save on first `Closing`. Wire it in `App.axaml.cs` next to `desktop.MainWindow = new MainWindow { ... }`.
5. Spec: `.trellis/spec/desktop/preferences.md` — add the `window` row to the file contract and the close-time awaited-save exception; reference the helper.
6. Validate: `dotnet build PzLauncher.slnx` (0 warnings), `dotnet test PzLauncher.slnx`, Native AOT publish `-c Release -r win-x64 -p:PublishAot=true` (needs `C:\Program Files (x86)\Microsoft Visual Studio\Installer` on PATH) with 0 IL warnings; run the app, move/resize/maximize/close/relaunch and inspect `%APPDATA%\PzLauncher\preferences.json`.

7. Reset (R10–R12): `IWindowPlacementService` + view-layer implementation via `WindowPlacementPersistence`; DI registration in `App.axaml.cs`; `SettingsViewModel.ResetWindowSizeCommand`; `SettingsView` heading + button; two resx keys in all 10 files; fakes/design data; tests (ViewModel command, rules centering on a given screen). Update `preferences.md` only if the rules text changes; document the service in `directory-structure.md` only if a convention changes.
8. Re-validate as in step 6, plus run the app: maximize → Settings → Reset → expect 1000×640 DIP centered; close/relaunch.

Risky files: `App.axaml.cs` (startup order), `JsonPreferenceStore.cs` (persisted contract). Rollback point: step 4 wiring is one call site.
