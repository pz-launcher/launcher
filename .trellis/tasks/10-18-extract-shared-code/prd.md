# Extract shared code and split oversized files

## Goal

Remove duplicated code from Core and Desktop by moving it into shared helpers, and split the two oversized files. The next change to one of these patterns (a new per-install preference, a new localized error, a new text file we rewrite) should then have one place to go. User-visible behaviour, on-disk formats and UI text stay the same. The one exception is the C3 error code (R3).

## Background (survey 2026-10-18)

- **No folder needs moving.** The largest folders are `Core/Mod/ZombieBuddy` (20 files), `Desktop/Services/Shell` (19), `Core/Mod/Collections` (18), `Core/Steam` (17) and `Core.Tests/TestSupport` (17). They are mostly small DTO/interface files that follow the feature-folder rule (`.trellis/spec/core/directory-structure.md`). The size problem is two single files: `Desktop.Tests/TestSupport/Fakes.cs` (1021 lines) and `Desktop/ViewModels/LaunchSettings/LaunchSettingsViewModel.cs` (687 lines).
- These files are large but cohesive, so they are not split: `ModCollectionsViewModel.cs` (548), `DesignData.cs` (547), `JsonPreferenceStore.cs` (479, ≈110 of them log declarations), `ModCollectionsView.axaml.cs` (396).
- **Rejected.** These were judged not worth extracting because the copies differ on purpose:
  - key=value and line parsing (`JavaReleaseFile`, `ModInfo`, `ActiveModsFile`, `ModListSettingsFile`)
  - the path-list dedup variants
  - JSON write and error mapping
  - JSON object parsing
  - process-launch exception mapping
  - delete helpers
  - drag-reorder behaviour (it has one consumer)
  - `DesignData`
- No `Common/` helper is bypassed by a hand-rolled copy.
- **Out of scope (user decision).**
  - C6: a shared `Logged(Error)` would change the log text.
  - C7: subfolder listing and `ResolveLink`.
  - D9: the folder-pick and scan scaffolding in the Installs and JavaRuntimes VMs.
  - D10: VM logic that may belong in Core — the default-collection forget and ZombieBuddy setup-mode choice in HomeViewModel, the Workshop-id sort and add-to-collection merge in InstalledModsViewModel, and the missing-mod count in ModCollectionsViewModel.

## Requirements

### Core

- **R1 (C1)** Strict UTF-8 text round-trip. `Mod/Collections/GameTextFile.cs` and `Steam/LocalConfigEditor.cs` both have:
  - identical `StrictUtf8`/`Bom` fields,
  - the same BOM detection and strict decode, where `DecoderFallbackException` becomes a malformed error,
  - the same BOM-preserving encode,
  - the same backup-then-write via `AtomicFile.BackupSuffix`.

  Move all of this into `Common/`. Each caller keeps its own error mapping. Bytes on disk stay identical, including the BOM and the backup.
- **R2 (C2)** Reading `ProjectZomboid64.json` into a `Result`. The try/catch in `Launch/PzLaunchConfig.ReadAsync` and `Launch/PzLaunchConfigEditor.LoadAsync` is the same in both: not-found → `LaunchErrors.ConfigMissing`, other IO → `ConfigMalformed`. Replace both with one reader in `Launch/`.
- **R3 (C3)** Launch stops depending on ZombieBuddy errors. `PzLaunchConfigEditor.SaveVmArgsAsync` currently returns `ZombieBuddyErrors.ConfigWriteFailed`. Change it to:
  - add `ErrorCode.LaunchConfigWriteFailed`, wire name `launch.config_write_failed`, `ErrorKind.External`, details `path` + `reason`;
  - add the factory `LaunchErrors.ConfigWriteFailed`;
  - add the resx key `Error_LaunchConfigWriteFailed` to all 10 `Lang/UiText*.resx` files, with the same text as today's `Error_ZombieBuddyConfigWriteFailed` per language;
  - add the `ErrorMessages` mapping;
  - update the tests.

  `ZombieBuddyConfigWriteFailed` has no other producer, so remove it with its resx key and mapping. The same write failure is now reported as `launch.config_write_failed`; the user accepted this contract change.
- **R4 (C4)** Add `FileContent.TryEqualAsync` in `Common/`, where an unreadable or missing file counts as different. `ZombieBuddyService.SameContentAsync` and the tail of `ZombieBuddyPreloadConfig.IsCurrentAsync` use it.
- **R5 (C5)** Add `ErrorDetails` helpers in `Common/` for the hand-built `path` and `path`+`reason` detail dictionaries in the `*Errors.cs` factories (≈38 of 59). Detail key names and values stay byte-identical, because they are a wire contract. Other key sets stay inline.

### Desktop

- **R6 (D1)** Localized message that re-renders on a language switch. Today `_errorSource`/`_statusSource`/`_warningSources`, `ShowError`/`ShowStatus` and `RenderTexts` are hand-written in `InstallsViewModel`, `JavaRuntimesViewModel`, `ModsViewModel`, `ModCollectionsViewModel` and `LaunchSettingsViewModel`. Move them into shared types in `Localization/`. The VMs' public text properties (`ErrorText`, `StatusText`, `Warnings`, …) keep their names and behaviour.
- **R7 (D2)** Shared text classes in `Themes/ShellStyles.axaml`:
  - `pageTitle`: 22/SemiBold, 10 sites.
  - `sectionTitle`: 16/SemiBold, 5 sites.
  - `error` and `warning`: foreground, with wrap where the sites wrap.
  - `hint`: muted + wrap.

  Local duplicates in the Proxy, Steam, Update and LaunchSettings views are removed. LaunchSettings keeps a local `FontSize=12` for its hints. Rendering stays identical: sites keep their own alignment and margins.
- **R8 (D3)** Per-install preference plumbing:
  - In `JsonPreferenceStore`, one generic reader replaces `ToInstallOverrides`/`ToZombieBuddyChannels`/`ToDefaultModCollections`, and one generic update replaces the three save blocks.
  - The path-comparer map with "saved spelling" key lookup becomes one type in `Services/Preferences/`, used by `InstallOverridesService`, `ZombieBuddyChannelService` and `ModCollectionDefaultsService`.
  - `preferences.json` content is unchanged.
- **R9 (D4)** Split the ZombieBuddy section of `LaunchSettingsViewModel` into a child VM in `ViewModels/LaunchSettings/`, exposed as a property, the same way `ModsViewModel` owns its children. The view binds through it, and the tests move to the child's members. Behaviour is unchanged.
- **R10 (D5)** Split `Desktop.Tests/TestSupport/Fakes.cs` into files grouped like `Services/` and Core features. Types and namespaces stay the same; only files move.
- **R11 (D6)** Generic enum parse for the `FromPreferenceName` loop in `GarbageCollectorPreferences`, `ProxyModePreferences`, `UpdateSourcePreferences`, `ZombieBuddyChannelPreferences` and `Themes/AppTheme.cs`. Each keeps its explicit `PreferenceName` switch, because that switch is the on-disk contract.
- **R12 (D7)** `AvaloniaDialogService.ConfirmAsync` and `ConfirmWithOptionAsync` share one dialog builder, and the "set result, then close" button wiring is written once.
- **R13 (D8)** The `OnLoaded → ActivateCommand` code-behind in the Home, Installs, Mods and LaunchSettings views becomes an attached property in `Views/Controls/`. It keeps the same `CanExecute` → `Execute` on `Loaded` semantics.

## Acceptance Criteria

- [ ] AC1 (R1–R5, R11): Core builds, and all Core tests pass. Assertions change only where R3 requires the new code.
- [ ] AC2 (R1): a round-trip test that rewrites a file with a BOM and one without, through each caller, shows unchanged BOM presence and an identical backup. Existing tests cover this, or a test is added in the helper's owner layer.
- [ ] AC3 (R3):
  - `SaveVmArgsAsync` on a locked file fails with `ErrorCode.LaunchConfigWriteFailed`.
  - `ErrorCodeTests` pins the wire name `launch.config_write_failed`.
  - `UiTextResourcesTests` passes with the new key in every language.
  - `zombiebuddy.config_write_failed` no longer exists in code, resx or tests.
- [ ] AC4 (R5): every changed factory produces the same `Details` keys and values as before. `ErrorCodeTests` or a factory test pins this.
- [ ] AC5 (R6–R13): Desktop builds, and all Desktop tests pass. Test edits are limited to the R9 member moves and the R10 file moves.
- [ ] AC6 (R6): switching the language re-renders the error, status and warning texts on every affected page. Existing VM language tests cover this.
- [ ] AC7 (R7, R13): every page looks the same as before in light and dark themes, and pages still activate once on first show. Checked by running the app.
- [ ] AC8: No new IL/AOT warnings in the build output.
- [ ] AC9: the specs are updated where a rule moved:
  - desktop `directory-structure.md`: activation via the attached property, the `LaunchSettings` child VM, the per-install map;
  - desktop `shell-and-theming.md`: the text classes;
  - core `directory-structure.md` / `error-handling.md`: the `Common/` text-file and `ErrorDetails` helpers;
  - common `test-strategy.md`: the `Fakes.cs` reference.
