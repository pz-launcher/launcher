# Implement: extract shared code and split oversized files

Implement runs `dotnet build` only. Tests run in check (`trellis-check`). Each group ends with a clean build and is a rollback point.

## Group A: Core helpers (R1, R2, R4, R5)

1. R1: add the strict UTF-8 helper in `Common/` and `AtomicFile.ReplaceWithBackupAsync`; switch `Mod/Collections/GameTextFile.cs` and `Steam/LocalConfigEditor.cs` to them.
2. R2: add the `ProjectZomboid64.json` `Result<string>` reader in `Launch/`; switch `PzLaunchConfig.ReadAsync` and `PzLaunchConfigEditor.LoadAsync` to it.
3. R4: add `FileContent.TryEqualAsync`; switch `ZombieBuddyService.SameContentAsync` and `ZombieBuddyPreloadConfig.IsCurrentAsync` to it, keeping any different existence check at the call site.
4. R5: add `Common/ErrorDetails`; switch every `*Errors.cs` factory whose details are exactly `path` or `path`+`reason`.
5. `dotnet build PzLauncher.slnx`

## Group B: R3 error code

1. `Common/ErrorCode.cs`: add `LaunchConfigWriteFailed` (`launch.config_write_failed`) and remove `ZombieBuddyConfigWriteFailed`.
2. `Launch/LaunchErrors.cs`: add `ConfigWriteFailed`. `Mod/ZombieBuddy/ZombieBuddyErrors.cs`: remove `ConfigWriteFailed`. `Launch/PzLaunchConfigEditor.cs`: use the new factory.
3. In all 10 `PzLauncher.Desktop/Lang/UiText*.resx` files, add `Error_LaunchConfigWriteFailed` with the old text per language, placed in the key order of `UiText.resx`, and remove `Error_ZombieBuddyConfigWriteFailed`.
4. `Localization/ErrorMessages.cs`: change the mapping arm.
5. Update the tests: `Core.Tests/Common/ErrorCodeTests.cs`, `Core.Tests/Launch/PzLaunchConfigEditorTests.cs`, `Desktop.Tests/ViewModels/LaunchSettings/LaunchSettingsViewModelTests.cs`, and any other test that names the old code (grep `ZombieBuddyConfigWriteFailed`, excluding `bin/` and `obj/`).
6. `dotnet build PzLauncher.slnx`

## Group C: Desktop VM and services (R6, R8, R11, R12)

1. R6: add `Localization/LocalizedText` and `LocalizedErrorList`; switch the Installs, JavaRuntimes, Mods, ModCollections and LaunchSettings VMs to them.
2. R8: add `JsonPreferenceStore` `ReadPerInstall`/`WithPerInstall` and `Services/Preferences/InstallKeyedMap<T>`; switch the three services.
3. R11: add the generic `PreferenceNames.Parse<T>`; switch the four `*Preferences.cs` files and `Themes/AppTheme.cs`.
4. R12: unify the confirm dialogs and the close-button wiring in `Services/Shell/AvaloniaDialogService.cs`.
5. `dotnet build PzLauncher.slnx`

## Group D: R9 LaunchSettings split (after C1)

1. Add the ZombieBuddy child VM in `ViewModels/LaunchSettings/`; move its state, commands and handlers out of `LaunchSettingsViewModel`.
2. Rebind `Views/LaunchSettings/LaunchSettingsView.axaml` (≈37 ZombieBuddy references); update `ViewModels/Design/DesignData.cs`.
3. Update `Desktop.Tests/ViewModels/LaunchSettings/LaunchSettingsViewModelTests.cs` to the new member paths (≈90 references); assertions stay the same.
4. `dotnet build PzLauncher.slnx`

## Group E: Views (R7, R13)

1. R7: add the text classes to `Themes/ShellStyles.axaml`; switch the sites; remove the local `error`/`hint` styles; LaunchSettings keeps a local `FontSize=12` hint.
2. R13: add `Views/Controls/PageActivation`; switch the Home, Installs, Mods and LaunchSettings views; delete their `OnLoaded` overrides.
3. `dotnet build PzLauncher.slnx`

## Group F: R10 test file split

1. Split `Desktop.Tests/TestSupport/Fakes.cs` into `Fakes.*.cs` and `TestErrors.cs` per `design.md`; the namespace and types stay unchanged.
2. `dotnet build PzLauncher.slnx`

## Check (trellis-check)

- `dotnet build PzLauncher.slnx`: 0 errors, no new warnings.
- `dotnet test PzLauncher.slnx`: all pass.
- `git grep -n "ZombieBuddyConfigWriteFailed\|config_write_failed"` shows only the new launch code.
- Diff review against AC2 (BOM/backup bytes) and AC4 (details keys).
- AC7: run the app (`run` skill). Every page should look the same in light and dark themes, and each page should activate on first show; switch the language once on Installs and LaunchSettings.

## Finish

- `dotnet format` only the `.cs` files changed in this task (memory rule; not a build/test step).
- Spec updates per `design.md` § Spec updates.
- Commit per group, or one commit, as the user prefers at Phase 3.
