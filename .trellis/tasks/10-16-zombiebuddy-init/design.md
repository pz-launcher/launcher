# Design: Initialize ZombieBuddy for subscribed installs

## Boundaries

- Core owns detection, file copy/delete and the json edit (a web UI would need
  them too: `.trellis/spec/core/service-api.md`). Desktop owns the channel
  preference, the setup dialog, the 启动设置 section and the Home launch gate.
- No change to how `GameLaunchService` builds the command: the agent reaches
  the JVM through the json's `vmArgs`, which it already reads.

## Core: `PzLauncher.Core/ZombieBuddy/` (new feature folder)

### Constants (`ZombieBuddyFiles`, internal)

- Item ids: stable `3619862853`, beta `3812624292`; Workshop content path
  `<library>/steamapps/workshop/content/<PzSteamApps.Game>/<id>`; source
  folder `mods/ZombieBuddy/libs`.
- Per OS (`PlatformKind`):
  - Windows: required `zbNative.dll`, `ZombieBuddy.jar`; agent arg
    `-agentlib:zbNative`.
  - Linux: required `ZombieBuddy.jar`; agent arg `-javaagent:ZombieBuddy.jar`.
- Leftover files (both OSes): `zbNative.dll`, `ZombieBuddy.jar`,
  `ZombieBuddy.jar.new`.

### Agent args (`ZombieBuddyAgentArgs`, internal static, pure)

- `IsAgentArg(arg, os)`: equals the OS's agent arg or starts with it + `=`.
  For removal, both forms are matched on either OS (a Linux-style arg left on
  Windows is also a leftover).
- `Add(vmArgs, os)` → prepends when none present (installer behaviour);
  `Remove(vmArgs)` → drops every match. Both report whether they changed.
- Options: the text after the first `=` of the agent entry, split on `,`
  into `key=value` items (ZombieBuddy's own parsing: `Agent.java:30`).
  `IsAllowAll(vmArgs, os)` → the first agent entry has `policy=allow-all`.
  `WithPolicy(vmArgs, os, allowAll)` → on the first agent entry only, replace
  or append the `policy` item, or drop it when off; other items keep their
  order; no items left → bare agent arg. Unknown, valueless items survive
  verbatim.

### Json edit (`PzLaunchConfigEditor`, internal, `Launch/`)

- Reads `ProjectZomboid64.json` as a `JsonNode` with the same leniency as
  `PzLaunchConfigJsonContext` (comments skipped, trailing commas, case-insensitive
  `vmArgs` lookup), edits only the top-level `vmArgs` array, writes indented
  UTF-8 (no BOM).
- Unknown fields and per-Windows `vmArgs` survive; comments and the original
  whitespace style do not (the shipped file has neither comments nor anything
  that relies on spacing).
- Write is atomic: temp file in the same folder, then `File.Move(overwrite)`.
- Missing file → `LaunchErrors.ConfigMissing`; not an object / `vmArgs` not an
  array of strings → `LaunchErrors.ConfigMalformed`.

### Java runtime: instrument library

- `JavaRuntime` gains `bool HasInstrumentLibrary`, filled in
  `JavaDiscoveryService` next to `HasGraalCompiler`: Windows
  `bin/instrument.dll`, Linux `lib/libinstrument.so`, macOS
  `lib/libinstrument.dylib` (names in `JavaLocations`). Round-trip test
  updated; inspect tests cover present and missing.
- Only this check is needed: the launcher starts `<JavaHome>/bin/javaw.exe`,
  so `zbNative.dll`'s bare-name `LoadLibraryA("instrument.dll")` finds the
  selected Java's own copy first (the exe folder precedes the
  `SetDllDirectory` folder); the bundled-`jre64` fallback only applies when
  that copy is absent.

### Game folder

- Move `GameLaunchService.ResolveGameFolder` to `PzLaunchFiles.ResolveGameFolder`
  (internal static) so both services use the folder that holds the json (Linux
  `projectzomboid/`). Copy targets and leftover checks use that folder.

### Workshop lookup

- Candidate libraries, deduplicated with `platform.PathComparison`, in order:
  the install's own library (`RootPath` is `<lib>/steamapps/common/<dir>`),
  then each `SteamLocator.FindSteamRoots()` root and its
  `SteamLibraryReader.ReadLibraryPathsAsync` libraries. Read failures are
  ignored (logged at debug); no Steam → not subscribed.
- An item counts as subscribed when its folder exists; whether `libs/` has the
  files is checked at initialize time (`zombiebuddy.source_file_missing`).

### Service (`IZombieBuddyService` / `ZombieBuddyService`, singleton)

```
Task<Result<ZombieBuddyStatus>> GetStatusAsync(string installRoot, CancellationToken ct = default);
Task<Result> InitializeAsync(string installRoot, ZombieBuddyChannel channel,
    IProgress<ZombieBuddyProgress>? progress = null, CancellationToken ct = default);
Task<Result> RemoveAsync(string installRoot,
    IProgress<ZombieBuddyProgress>? progress = null, CancellationToken ct = default);
Task<Result> SetAllowAllAsync(string installRoot, bool allowAll, CancellationToken ct = default);
```

- `SetAllowAllAsync` requires an agent entry in the json (else
  `zombiebuddy.not_initialized`, Conflict) and writes through
  `PzLaunchConfigEditor`; no change → no write.

- `InstallDetectionService.InspectAsync` errors pass through. A non-Game
  install or macOS → status with `IsSupported = false` (Initialize/Remove
  return `LaunchErrors.NotAGame` / `LaunchErrors.PlatformUnsupported`).
- Initialize steps: `FindItem` → `Copy` per required file (`CopyNativeLoader`,
  `CopyAgentJar`) → `AddAgentArg`. Remove steps: `DeleteFiles` →
  `RemoveAgentArg`. Each step is idempotent so 重试 re-runs from the start.
  `ct` is checked between steps.
- Copy is `File.Copy(overwrite: true)`; `IOException` /
  `UnauthorizedAccessException` → `zombiebuddy.copy_failed` (path, reason).
  Same mapping for delete (`zombiebuddy.delete_failed`) and json write
  (`zombiebuddy.config_write_failed`).

### DTOs (records, registered in `CoreJsonContext`, round-trip tested)

- `ZombieBuddyChannel { Stable, Beta }`.
- `ZombieBuddyItem(ZombieBuddyChannel Channel, string Path)`.
- `ZombieBuddyStatus(bool IsSupported, IReadOnlyList<ZombieBuddyItem> Subscribed,
  bool IsInitialized, bool HasLeftovers, bool IsAllowAll, ZombieBuddyAction NeededAction)` —
  `Subscribed` stable first; `NeededAction`: `None`, `Initialize`
  (subscribed ∧ ¬initialized), `Remove` (¬subscribed ∧ leftovers).
- `ZombieBuddyStep { FindItem, CopyNativeLoader, CopyAgentJar, AddAgentArg,
  DeleteFiles, RemoveAgentArg }`.
- `ZombieBuddyProgress(IReadOnlyList<ZombieBuddyStep> Steps, int Current, bool IsComplete)`
  — reported once before the first step (so the dialog can list them all),
  at each step start, and once on success. On failure the method returns the
  error; the step that failed is the last reported `Current`.

### Errors (new `zombiebuddy` section in `ErrorCode`, `ZombieBuddyErrors`)

| Wire name | Kind | When |
|---|---|---|
| `zombiebuddy.not_subscribed` | NotFound | requested channel's item folder is gone |
| `zombiebuddy.source_file_missing` | NotFound | `libs/<file>` missing in the item |
| `zombiebuddy.copy_failed` | External | copy into the game folder failed |
| `zombiebuddy.delete_failed` | External | deleting a leftover failed |
| `zombiebuddy.config_write_failed` | External | writing the json failed |
| `zombiebuddy.not_initialized` | Conflict | allow-all switched with no agent entry |

Each gets a factory with `Details` (`path`, `reason`), an `Error_<Member>`
text in all 10 resx files, an `ErrorMessages` mapping and Kind pinning in
`ErrorCodeTests` (`.trellis/spec/core/error-handling.md`).

## Desktop

### Preferences

- New top-level field `zombieBuddyChannels`: object, absolute install root →
  `"stable"` | `"beta"`; unknown value / non-absolute key dropped. Own save
  method; mapping class with stable strings
  (`.trellis/spec/desktop/preferences.md`); spec table updated.
- Read/write through a small `IZombieBuddyChannelService` (path-comparison
  lookup, fire-and-forget save) modelled on `InstallOverridesService`.
  Missing → `Stable`. The effective channel is the saved one if subscribed,
  else whichever item is subscribed.

### Game running state

- Home currently owns `IsGameRunning`. Move it into an app-lifetime
  `GameRunState` (`ObservableObject`, Desktop service) that Home sets around
  the session and both Home and 启动设置 observe (R9).

### Setup dialog

- `ZombieBuddySetupViewModel` (created per dialog): mode (Initialize/Remove),
  install root, channel, `CanLaunch` (Home: true; 启动设置: false);
  `Steps` rows (localized name + state), `Progress` (0–1), `ErrorText`,
  commands `Launch` (enabled after success), `Retry` (after failure),
  `Cancel`/`Close`. Starts on `Opened`; cancel triggers the `ct`.
- `ZombieBuddySetupView` (UserControl), hosted by
  `IDialogService.ShowZombieBuddySetupAsync(vm)` → `Task<bool>` (true = 启动
  pressed), modal over the main window, same window chrome rules as
  `ShowErrorAsync`. Closing the window = 取消.

### Home launch gate (`HomeViewModel.LaunchAsync`)

1. `GetStatusAsync(root)`; on error or `NeededAction == None` continue to 3.
2. Show the dialog in the needed mode with the effective channel; `false` →
   return without launching.
3. Existing launch path, unchanged.

### 启动设置 section (`LaunchSettingsViewModel` / `LaunchSettingsView.axaml`)

- Loads status on `Open` and after the dialog closes; section visible when
  `IsSupported ∧ (Subscribed.Count > 0 ∨ HasLeftovers)`.
- Status text, source path, channel `ComboBox` (only when both subscribed) +
  hint, 初始化/重新初始化 and 移除 buttons per R8, disabled by `GameRunState`.
- Instrument warning: initialized ∧ the effective Java (`SelectedJava`, or
  the bundled runtime from `GameLaunchDefaults`) has
  `HasInstrumentLibrary == false`.
- Allow-all `ToggleSwitch` (initialized only) bound to `IsAllowAll`; a change
  calls `SetAllowAllAsync` at once (no 使用 button, like a direct setting),
  disabled while the call runs or the game runs; on error the switch reverts
  to the reloaded status and the error shows in the section. Warning text
  under it while on.

### Resx keys (all 10 files)

Section title; status ×3; channel names ×2; channel hint; buttons
(初始化, 重新初始化, 移除, 启动, 取消, 重试, 关闭); dialog titles ×2; step names ×6;
instrument-library warning; allow-all label + warning; error texts ×6.

## Trade-offs

- Json edit vs launch-time injection: the user chose both "copy + json"; with
  the gate always running before launch, the json is guaranteed to carry the
  agent, so launch-time injection would never fire and is left out.
- Initialized ignores file versions (user decision): a stale `zbNative.dll`
  is refreshed only by 重新初始化; the jar updates itself.
- Re-serializing the json loses comments/spacing; acceptable for a file the
  game ships without comments.

## Rollback

- Code: revert the task's commits. The new `zombieBuddyChannels` preference
  field is ignored by older builds.
- User data: 移除 (or the ZombieBuddy CLI uninstaller) reverts the game folder
  and json changes.
