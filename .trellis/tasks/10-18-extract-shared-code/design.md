# Design: extract shared code and split oversized files

Every item is a refactor with no behaviour change, except the R3 error code. Names below are proposals. The implementer may adjust them to match surrounding code, but must keep each helper's owner folder and contract.

## Task shape

One task with grouped steps, not a parent/child tree. Every item is checked the same way (build + the existing tests), and none needs its own acceptance gate. The groups in `implement.md` are rollback points.

## Core

### R1 Strict UTF-8 text (`Common/`)

- New internal static helper in `Common/`, e.g. `Utf8TextFile`. It owns the strict encoding and the BOM bytes, and provides:
  - decode: bytes → text plus `hasBom`, or a failure reason taken from `DecoderFallbackException`;
  - encode: text plus `hasBom` → bytes.
- The backup-then-write sequence moves to `AtomicFile`, e.g. `ReplaceWithBackupAsync(path, original, content, ct)`: back up `original` to `path + BackupSuffix`, then write `content`. It throws the same `IOException`/`UnauthorizedAccessException` it throws today, and callers keep their own catch → feature error.
- `GameTextFile` and `LocalConfigEditor` keep their line/AppBlock logic and error factories. Only the fields, decode, encode and the write pair move.
- Contract: the invalid-byte rule stays. A file that isn't valid UTF-8 is never rewritten, because replacement characters would change bytes we didn't touch.

### R2 Launch config read (`Launch/`)

- One `Result<string>` reader for `ProjectZomboid64.json`. It may live on `PzLaunchFiles` or as a small internal static in `Launch/`, and holds the not-found → `ConfigMissing` and IO → `ConfigMalformed` mapping.
- `PzLaunchConfig.ReadAsync` and `PzLaunchConfigEditor.LoadAsync` call it, then parse as they do today. Their parse rules differ and stay separate.

### R3 `launch.config_write_failed`

- `ErrorCode`: add `LaunchConfigWriteFailed` in the launch block with `[JsonStringEnumMemberName("launch.config_write_failed")]`, and remove `ZombieBuddyConfigWriteFailed`. Its only producer was `PzLaunchConfigEditor`, so after R3 it is never produced. The `ErrorCode.cs` comment forbids changing a wire name; retiring a code that is no longer produced is the accepted contract change.
- `LaunchErrors.ConfigWriteFailed(path, reason)` has the same message shape, kind (`External`) and details (`path`, `reason`) as the removed factory.
- In all 10 `Lang/UiText*.resx` files, add `Error_LaunchConfigWriteFailed` with each language's current `Error_ZombieBuddyConfigWriteFailed` text and the same `{0}`, and remove the old key. Key order follows `UiText.resx` (localization spec), so the new key goes in the launch error group.
- `Localization/ErrorMessages.cs`: map the new code with the same `Format(…, path)` and remove the old arm.
- Tests to update:
  - `Core.Tests/Common/ErrorCodeTests.cs`: the factory map and the wire-name InlineData.
  - `Core.Tests/Launch/PzLaunchConfigEditorTests.cs`: the locked-file case.
  - `Desktop.Tests/ViewModels/LaunchSettings/LaunchSettingsViewModelTests.cs`: the `TestErrors.Create` code.
  - Any ZombieBuddy service test that asserts the old code on a vmArgs write.

### R4 `FileContent.TryEqualAsync`

- `FileContent.TryEqualAsync(path, other, ct)` returns false when either file is missing or reading throws `IOException`/`UnauthorizedAccessException`, and otherwise defers to `EqualAsync`.
- The existing callers in `ZombieBuddyService` and `ZombieBuddyPreloadConfig` replace their own exists + catch with it. Before replacing, confirm each copy checks the same file (target vs source) for existence; if they differ, keep the extra check at the call site.

### R5 `ErrorDetails` (`Common/`)

- Internal static `ErrorDetails` with `Path(string path)` and `Path(string path, string reason)`. Each returns a new `Dictionary<string,string>` with the keys `"path"`/`"reason"` exactly as today.
- Only factories whose details are exactly those key sets switch over. Factories with other keys (`modId`, `value`, `exitCode`, `output`, …) stay inline. A `GcIncompatible` that puts a Java home under `path` may use `Path(javaHome)`.

## Desktop

### R6 Localized message holders (`Localization/`)

- `LocalizedText` holds the current source, `Func<string>?`, and a setter callback the VM passes in, e.g. `text => ErrorText = text`.
  - `Show(Func<string>? source)` stores the source and pushes the text.
  - `Show(Error? error)` is a convenience over `ErrorMessages.ToUserText`.
  - `Render()` re-invokes the source.

  The VM keeps its `[ObservableProperty]` text properties, so XAML bindings and tests are unchanged.
- `LocalizedErrorList` owns `List<Error>` sources plus the `ObservableCollection<string>` it renders. It provides `Items`, `Add(Error)`, `Clear()`, `Render()` and `Contains(ErrorCode)`. The last one is for LaunchSettings' "already shown as a warning" check. VMs expose `Warnings => _warnings.Items`.
- `LaunchSettingsViewModel.ShowLaunchProblem` splits one source into `GcProblemText` or `LaunchProblemText`. It keeps that routing as a setter callback over one `LocalizedText`.
- Each VM still subscribes to `LanguageChanged` and calls `Render()` on its holders. `ModsViewModel` still forwards to `Collections.RenderTexts()`.

### R7 Text classes (`Themes/ShellStyles.axaml`)

- Global `TextBlock` style classes:
  - `pageTitle`: FontSize 22, SemiBold.
  - `sectionTitle`: FontSize 16, SemiBold.
  - `error`: `Shell.ErrorForeground`.
  - `warning`: `Shell.WarningForeground`.
  - `hint`: `Shell.TextMuted`, wrap.
- Sites drop the matching inline setters and keep everything else inline (alignment, `DockPanel.Dock`, margins, `IsVisible`, other setters).
- `TextWrapping="Wrap"` stays inline where a site sets it; `error`/`warning` don't force wrap, because some sites don't wrap (e.g. `ModCollectionsView` warnings).
- `LaunchSettingsView` keeps a local `TextBlock.hint` style that sets only `FontSize=12`; it stacks on the global class.
- Remove the local `error`/`hint` styles in the Proxy, Steam and Update settings views. `ProxySettingsView`'s `fieldLabel` stays local, because it has one consumer.
- Icons (`ic:FluentIcon FontSize="16"`) are not text classes and are untouched.

### R8 Per-install preferences

- Store side, in `JsonPreferenceStore`:
  - `ReadPerInstall<T>(entries, tryRead)` keeps the `IsAbsolutePath(root)` filter and ordinal keys, and drops entries the reader rejects. The caller's count comparison still drives the `LogInvalid*` calls.
  - `WithPerInstall(entries, root, JsonElement? entry)` returns a new ordinal dictionary with the entry set, or removed when `null`. That covers the default-collection clear.
- Service side, `Services/Preferences/InstallKeyedMap<T>`:
  - built from the saved dictionary + `IPlatformEnvironment.PathComparison` (seeded with `TryAdd`);
  - `TryGetValue`/`GetValueOrDefault`;
  - `Set(root, value)` and `Remove(root)` return the key to persist, which is the saved spelling when one exists;
  - `Keys` is exposed for `InstallOverridesService`'s pending-write flush.
- `InstallOverridesService` keeps its own `_pendingWrites` timer map; only `_overrides` moves to the new type.

### R9 ZombieBuddy child VM

- New `ViewModels/LaunchSettings/LaunchSettingsZombieBuddyViewModel` (name may be shortened). It owns:
  - the status;
  - the channel and allow-all state and their change handlers;
  - the applying flag;
  - the Initialize/Remove commands and the setup dialog;
  - the ZombieBuddy error text (a `LocalizedText` from R6);
  - its version counter and the derived `Show…`/`Is…`/`Can…` properties.
- Parent → child: `Clear()`, `LoadAsync(installRoot, ct)` and `RenderTexts()`. The child needs the current install and the game-run state for `CanEdit`. It takes `GameRunState` via its constructor and the install via `LoadAsync`/`Clear`.
- `ShowZombieBuddyInstrumentWarning` needs the parent's `SelectedJava`, so it stays on the parent. The parent raises it when the child's initialized state changes (subscribe to the child's `PropertyChanged`) and when `SelectedJava` changes.
- The parent exposes `ZombieBuddy { get; }`. `LaunchSettingsView.axaml` binds `ZombieBuddy.X`. `DesignData` and the tests follow the new member paths, and test names may stay.
- The parent creates the child, as `ModsViewModel` does today; no DI registration change unless `ModsViewModel`'s children are registered.

### R10 Split `Fakes.cs`

Files live under `Desktop.Tests/TestSupport/`. The namespace is unchanged.

| File | Contents |
|---|---|
| `Fakes.Installs.cs` | install detection, folder picker, `TestInstalls` |
| `Fakes.Java.cs` | Java discovery, `TestJava` |
| `Fakes.Launch.cs` | game launch, install overrides, `TestLaunch` |
| `Fakes.Mods.cs` | mod collections, defaults, `TestMods` |
| `Fakes.ZombieBuddy.cs` | ZombieBuddy service, mods, channels, `TestZombieBuddy` |
| `Fakes.Shell.cs` | dialogs, clipboard, theme, window placement, app lifetime, URL launcher |
| `Fakes.Preferences.cs` | preference store |
| `Fakes.Update.cs` | update service, `TestUpdates` |
| `Fakes.Net.cs` | network, `TestProxy` |
| `Fakes.Steam.cs` | Steam hook, `TestSteam` |
| `Fakes.Platform.cs` | `FakePlatformEnvironment`, `ManualTimeProvider` |
| `TestErrors.cs` | `TestErrors` |

The exact grouping may follow the actual usings, but no type is renamed or changed.

### R11 Generic enum parse

- `PreferenceNames.Parse<T>(string? name, Func<T, string> nameOf) where T : struct, Enum` returns `T?`. It loops `Enum.GetValues<T>()`, which is AOT-safe and generic, with the same comparison the current loops use (check: ordinal vs ignore-case).
- It lives in `Services/Preferences/`. `Themes/AppTheme.cs` references it from there, and Themes already depends on Preferences for persistence; if it doesn't, the helper goes in a neutral place in `Localization/` or the project root instead (decide by existing usings).
- Each `FromPreferenceName` becomes a one-liner. The `PreferenceName` switches are untouched.

### R12 Dialog service

- `ConfirmAsync` calls the same private builder as `ConfirmWithOptionAsync`, without the option checkbox, and returns `.Confirmed`. Alternatively, both call a private core that takes `string? optionText`.
- A private helper creates a button that sets the dialog result and closes, used by all 4 sites.
- Window titles, sizes, default/cancel buttons and the "close = cancel" rule are unchanged.

### R13 Activation attached property (`Views/Controls/`)

- `PageActivation.Command` (attached `ICommand`) on `Control`. On attach it hooks `Loaded`; on `Loaded`, if `CanExecute(null)`, it calls `Execute(null)`. The run-once rule stays in each VM, as the spec requires.
- The four views set `controls:PageActivation.Command="{Binding ActivateCommand}"` and delete their `OnLoaded` override (and code-behind usings that become unused).
- Compiled bindings: confirm the views' `x:DataType` makes the binding compile. AOT: no reflection.

## Spec updates (Phase 3)

| Spec | Update |
|---|---|
| desktop `directory-structure.md` | the activation rule now names the attached property; the per-install map; the LaunchSettings child VM |
| desktop `shell-and-theming.md` | the text classes |
| desktop `localization.md` | `LocalizedText`/`LocalizedErrorList` as the way VMs keep re-renderable text |
| core `directory-structure.md` | `Common/` gains the UTF-8 text and `ErrorDetails` helpers |
| core `error-handling.md` | details via `ErrorDetails` for path/reason |
| common `test-strategy.md` | the `Fakes.cs` → `TestSupport/Fakes.*.cs` reference |

## Rollback

Each group in `implement.md` is self-contained. Reverting one group's files restores the old code without touching the others. The exceptions are R9, which depends on R6 for its error text (revert both or adjust), and R3, whose resx and test edits must be reverted with the enum change.
