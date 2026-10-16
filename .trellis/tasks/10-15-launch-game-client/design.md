# Design: Launch the game client

## Boundaries

```
HomeView ─> HomeViewModel ──────────┐
VersionSettingsView ─> VersionSettingsViewModel ─┤
                                    ├─> IGameLaunchService (Core, Launch/)
                                    │      ├─> IInstallDetectionService.InspectAsync
                                    │      ├─> IJavaDiscoveryService.InspectAsync (bundled jre64 info)
                                    │      ├─> IPlatformEnvironment (Os, OsVersion, TotalMemoryBytes)
                                    │      └─> IProcessRunner (Platform/, process seam)
                                    ├─> IPreferenceStore (per-install overrides)
                                    └─> IDialogService (error dialog)
```

- Core owns: platform check, re-inspection, reading `ProjectZomboid64.json`,
  OS-version vmArgs, applying overrides, splitting argument strings, building
  the command, process start, the single-running-game rule, exit tracking.
- Desktop owns: storing overrides per install, the 版本设置 page, button
  state, error text (`ErrorMessages`), the dialog.
- Overrides are stored by the host and passed in on each call: Core has no
  settings persistence yet (`.trellis/spec/core/index.md`), and a future
  server host will store its own. Moving storage to Core later doesn't change
  the Core launch API.

## Core: `PzLauncher.Core/Launch/` (new feature folder)

`IGameLaunchService` (public, singleton `GameLaunchService`):

| Member | Contract | Future HTTP |
|---|---|---|
| `Task<Result<GameLaunchDefaults>> GetDefaultsAsync(string installRoot, CancellationToken ct = default)` | json values for the settings page + machine memory | `GET /api/v1/game/defaults` |
| `Task<Result<GameSession>> LaunchAsync(LaunchGameRequest request, CancellationToken ct = default)` | Validates, builds, starts; `launch.already_running` while a tracked game runs | `POST /api/v1/game/launch` |
| `Task WaitForExitAsync(CancellationToken ct = default)` | Completes when the tracked game exits, immediately when none; cancellation throws | long-poll / SSE |

DTOs (records, in `CoreJsonContext`, round-trip tested):

- `LaunchGameRequest(string InstallRoot, GameLaunchOverrides Overrides)`
- `GameLaunchOverrides(string? JavaHome, int? MaxMemoryMb, int? MinMemoryMb, string ExtraJvmArgs, string ExtraGameArgs)`
  — `null` Java = the install's bundled `jre64`; `null` memory = follow json;
  empty strings = none. `Empty` static.
- `GameLaunchDefaults(int? MaxMemoryMb, int? MinMemoryMb, IReadOnlyList<string> VmArgs, JavaRuntime? BundledJava, int SystemMemoryMb, IReadOnlyList<Error> Warnings)`
  — memory from the json's effective `-Xmx`/`-Xms` (null when absent or
  unparseable); `VmArgs` = json vmArgs + matching `windows` vmArgs;
  `BundledJava` = `IJavaDiscoveryService.InspectAsync(<game>/jre64)` (null on
  failure), used for 游戏自带's label and the D17 comparison. A json that
  can't be read is a `Warnings` entry on a successful result (json values
  null/empty), so the page still gets `SystemMemoryMb` for 自定义 (R4);
  `install.*` and `launch.not_a_game` still fail the call.
- `GameSession(string InstallRoot, int ProcessId)`

`LaunchAsync` order (first failure wins):

1. Os is macOS → `launch.platform_unsupported`.
2. `MaxMemoryMb` or `MinMemoryMb` ≤ 0 → `launch.invalid_memory`.
3. A game is tracked → `launch.already_running`.
4. `InspectAsync(root)` failure → returned unchanged (`install.*`).
5. Kind is `DedicatedServer` → `launch.not_a_game`.
6. Resolve `<game>` (D18): `<root>` if `<root>/ProjectZomboid64.json`
   exists, else `<root>/projectzomboid` if its json exists, else `<root>`
   (so the error names the expected file). Read `<game>/ProjectZomboid64.json`
   → `launch.config_missing` / `launch.config_malformed` (also when
   `mainClass` or `classpath` is missing/empty).
7. Java: `JavaHome` set → `<JavaHome>/bin/<exe>`, else
   `<game>/jre64/bin/<exe>` (`<exe>` = `javaw.exe` on Windows, `java` on
   Linux); missing → `launch.java_missing`. No version
   check here (D17 is a UI warning only).
7a. Effective min > effective max (override, else json value) →
   `launch.invalid_memory`.
8. Build args and environment (below), `IProcessRunner.Start(java, args, env, workingDirectory: <game>)`;
   `Win32Exception` / `IOException` / `UnauthorizedAccessException` →
   `launch.start_failed` (details `path`, `reason`), logged once at Warning.
9. Track the process; return `GameSession`.

Steps 3–9 run under a `SemaphoreSlim(1)`. A background continuation awaits
process exit, clears the session and disposes the process;
`WaitForExitAsync` awaits that task with `WaitAsync(ct)`.

### Launch config (`PzLaunchConfig`, internal, reusable by the server child)

- Source-generated `System.Text.Json` model of `ProjectZomboid64.json`:
  `mainClass`, `classpath[]`, `vmArgs[]`, `windows: { "<version>": { vmArgs[] } }`.
  Lenient reader (comments, trailing commas, case-insensitive names); the
  file is untrusted external data (`.trellis/spec/core/error-handling.md`).
- OS-version entry: parse keys with `Version.TryParse` (a bare `"7"` /
  `"10"` is read as `7.0` / `10.0`); pick the highest key ≤
  `IPlatformEnvironment.OsVersion`; unparseable keys ignored; none matching →
  no extra args. Picking one (not all ≤) avoids two GC flags, which the JVM
  rejects. Assumption, not documented by the game: matches observed files
  (game `6.1`/`10.0.17134`, server `7`/`10`); note it in a code comment.

### Argument building

```
<java>  <json vmArgs + windows vmArgs (Windows only), -Xmx/-Xms replaced/added>  <extra JVM args>
        -cp <classpath joined with ';' (Windows) / ':' (Linux)>  <mainClass with '/'→'.'>  <extra game args>
```

- `-Xmx` / `-Xms` overrides: remove every `-Xmx…` / `-Xms…` token from the
  json part, then add `-Xmx<N>m` / `-Xms<N>m`. Parsing for defaults accepts
  `k`/`m`/`g` (any case) and plain bytes.
- Extra-arg splitting (`LaunchArguments.Split`): whitespace separates, `"…"`
  groups (quotes removed), an unclosed quote runs to the end. No escapes.
- Arguments go through `ProcessStartInfo.ArgumentList` (OS quoting handled).

### Errors (`LaunchErrors`, new `launch` section in `ErrorCode`)

| Member | Wire name | Kind | Details |
|---|---|---|---|
| `LaunchPlatformUnsupported` | `launch.platform_unsupported` | Validation | — |
| `LaunchInvalidMemory` | `launch.invalid_memory` | Validation | `value` |
| `LaunchAlreadyRunning` | `launch.already_running` | Conflict | — |
| `LaunchNotAGame` | `launch.not_a_game` | Validation | `path` |
| `LaunchConfigMissing` | `launch.config_missing` | NotFound | `path` |
| `LaunchConfigMalformed` | `launch.config_malformed` | External | `path`, `reason` (also unreadable file) |
| `LaunchJavaMissing` | `launch.java_missing` | NotFound | `path` |
| `LaunchStartFailed` | `launch.start_failed` | External | `path`, `reason` |

File names (`ProjectZomboid64.json`, `jre64`, the `projectzomboid` subfolder,
`natives`, per-OS Java executable, the Linux preload libraries) are domain constants in
one place: `Launch/PzLaunchFiles.cs`. Game-folder resolution (step 6) is one
internal function shared by `LaunchAsync` and `GetDefaultsAsync`. The separator comes
from the target OS (`IPlatformEnvironment.Os`), not `Path.PathSeparator`, so
Linux behaviour is testable on Windows.

### Linux environment

Mirrors the shipped B42 `projectzomboid.sh` (copied from the user's Ubuntu
install, `research/linux-launch.md`):

- `LD_LIBRARY_PATH` = `<game>/natives:<game>:<java home>/lib` + existing value
  (from `IPlatformEnvironment.GetEnvironmentVariable`). `<java home>/lib` is
  the script's `jre64/lib`, following the chosen Java.
- `LD_PRELOAD` = existing value + `libjsig.so` + `libPZXInitThreads64.so`,
  `:`-joined, empty parts skipped. Bare names as in the script: the loader
  finds them through `LD_LIBRARY_PATH`, so `libjsig.so` follows the chosen
  Java. No existence checks.
- `XMODIFIERS` = empty (the script clears it so SCIM doesn't break keyboard input).
- The script's stdout redirect to `~/Zomboid/projectzomboid.sh.log` is not
  replicated (no output redirection in this task).
- `PATH` is not changed: the script prepends `jre64/bin` only so the native
  `ProjectZomboid64` launcher can find `libjvm.so`.
- The stock Linux json has no OS-version map; the `windows` map is applied
  only on Windows. `-Djava.library.path` comes from the json, never
  hard-coded.

## Core: platform additions

- `IPlatformEnvironment` gains `Version OsVersion` (`Environment.OSVersion.Version`)
  and `long TotalMemoryBytes` (`GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`).
  Fakes in both test projects get settable values.
- `internal interface IProcessRunner { IRunningProcess Start(ProcessStartRequest request); }`
  with `ProcessStartRequest(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory, IReadOnlyDictionary<string, string> Environment)`
  (environment entries are added/overridden on top of the launcher's own).
- `internal interface IRunningProcess : IDisposable { int Id { get; } Task WaitForExitAsync(CancellationToken ct); }`
- `ProcessRunner`: `Process` with `UseShellExecute = false`, no redirection.
  Throws OS exceptions; the service converts them. `TryAddSingleton` so tests
  register a fake first. The server child extends this seam with stdio.

## Desktop

### Preferences (`preferences.json`)

New field `installs`: object keyed by install root path →
`{ "javaHome": string|null, "maxMemoryMb": int|null, "minMemoryMb": int|null, "jvmArgs": string, "gameArgs": string }`.

- `UserPreferences` gains `IReadOnlyDictionary<string, GameLaunchOverrides> InstallOverrides`
  (empty when missing; entries with invalid values dropped, others kept,
  logged once).
- `IPreferenceStore.SaveInstallOverridesAsync(string installRoot, GameLaunchOverrides overrides, ct)`
  replaces that one entry, keeps everything else.
- Lookup uses the platform path comparison (`IPlatformEnvironment.PathComparison`)
  through one small desktop service, `IInstallOverridesService`
  (`Get(root)`, `Save(root, overrides)`), which owns the in-memory map so
  Home and 版本设置 see the same values.
- `preferences.md` file-contract table gains the `installs` row.

### Navigation

- `ShellPage` gains `VersionSettings`. `MainViewModel.CurrentPage` maps it to
  `VersionSettingsViewModel`. Title-bar tabs bind to a derived
  `MainViewModel.CurrentTab` (`VersionSettings` → `Home`); clicking the Home
  tab while on 版本设置 returns to Home.
- `HomeViewModel.VersionSettingsRequested` / `VersionSettingsViewModel.BackRequested`
  events, handled by `MainViewModel` (spec: navigation via events).

### `HomeViewModel`

- Adds `IGameLaunchService`, `IInstallOverridesService`, `IDialogService`.
- `IsGameRunning`; `LaunchCommand` async with `CanExecute` = Game selected &&
  !running; `VersionSettingsCommand` with `CanExecute` = Game selected; both
  re-evaluated in `OnInstallsPropertyChanged` and on `IsGameRunning` change.
- Launch: request with the selected install's stored overrides; success →
  running, await `WaitForExitAsync`, not running; failure →
  `IDialogService.ShowErrorAsync(ErrorMessages.ToUserText(error))`.
- Button label: two `{x:Static}` TextBlocks (`Home_Launch`,
  `Home_GameRunning`) toggled by `IsGameRunning`.

### `VersionSettingsViewModel` (`ViewModels/VersionSettings/`, singleton)

- Reloads on every view load for the current `SelectedInstall` (a newer load
  replaces one still running; loading never saves): stored overrides and
  `GetDefaultsAsync` (errors/warnings kept as `Error`, re-rendered on
  `LanguageChanged`). `JavaOption.Label` reads `UiText` at bind time; a
  language switch rebuilds `MainView`.
- Java dropdown: `JavaOption` rows = 游戏自带 (label from `BundledJava`,
  `JavaHome = null`) + `JavaRuntimesViewModel`'s available runtimes (calls
  its `EnsureScannedAsync`). A saved `JavaHome` not in the list is added as a
  找不到 row so the selection stays visible. `ShowJavaVersionWarning` = chosen
  major < bundled major (both known).
- Memory: `MaxMemoryMode` / `MinMemoryMode` enums (`FollowJson`, `Custom`)
  bound to radios via `EnumMatchConverter`; `CustomMaxMemoryMb` slider (min
  2048, max `SystemMemoryMb` rounded down to 512, step 512, snap);
  `CustomMinMemoryMb` slider (min 512, max = effective max, step 512).
  Switching to Custom starts from the json value clamped, else 8192 (max) /
  the clamped json min or 512 (min). When effective max drops below a custom
  min, min is set to max (D15).
- `ExtraJvmArgs` / `ExtraGameArgs` text boxes update on every keystroke (the
  service's write debounce coalesces them), so closing the window while
  typing keeps the text.
- Saving: each change calls `IInstallOverridesService.Save`, which updates the
  in-memory map at once (Home always launches with current values) and
  debounces the file write per install by 300 ms (`TimeProvider`, so tests
  can drive it) to avoid a write per slider step; pending writes are flushed
  on shutdown.
- View: page with back button + title + install path, then a 启动参数 card.
  No sidebar.

### `IDialogService` + `AvaloniaDialogService` (`Services/`)

- `Task ShowErrorAsync(string message)`: the window currently built in
  `App.ShowUnexpectedErrorDialogAsync`, moved here. `App` keeps its re-entry
  guard and calls the service. Owner resolved lazily (`() => desktop.MainWindow`).

### Resx keys (all 10 files)

`Home_GameRunning`, `Home_VersionSettings`, `VersionSettings_Title`,
`VersionSettings_Back`, `VersionSettings_LaunchSection`,
`VersionSettings_Java`, `VersionSettings_JavaBundled` (`{0}` = vendor/version),
`VersionSettings_JavaVersionWarning` (`{0}` = bundled major),
`VersionSettings_Memory`, `VersionSettings_MinMemory`,
`VersionSettings_MemoryFollowJson` (`{0}` MB),
`VersionSettings_MemoryNotSet`, `VersionSettings_MemoryCustom`,
`VersionSettings_MemoryValue` (`{0}`), `VersionSettings_SystemMemory` (`{0}`),
`VersionSettings_JvmArgs`, `VersionSettings_JsonVmArgs`, `VersionSettings_GameArgs`,
`VersionSettings_ArgsHint`, and one `Error_<Member>` per new code.

## Trade-offs

- Build the command vs run the exe: chosen by the user for control over
  arguments; cost: we replicate the exe's json handling (OS-version entry
  rule is inferred) and must follow future json format changes.
- Per-install overrides keyed by path: a moved install loses its settings
  (accepted with D10).
- Debounced slider saves vs save-on-release: debounce works for keyboard and
  pointer alike and is testable with `TimeProvider`.

## Rollback

Revert the commit. The new `installs` field in `preferences.json` is ignored
by older builds (unknown field), so no migration is needed either way.
