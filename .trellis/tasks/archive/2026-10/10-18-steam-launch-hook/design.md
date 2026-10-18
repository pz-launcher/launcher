# Design: Steam launch hook

## Boundaries

| Layer | Owns |
|---|---|
| Core `Steam/` | Current account lookup, `localconfig.vdf` read/edit, launch-option transform, Steam client running/shutdown/start |
| Core `Update/` | Restart argument passed when the updater starts the new launcher |
| Desktop `Services/Shell/` | Single-instance guard + activation (Windows), reminder dialog with a checkbox |
| Desktop `Services/Preferences/` | `steamHookReminder` preference |
| Desktop `ViewModels/Settings/`, `Views/Settings/` | Steam section |
| Desktop `App.axaml.cs` | Startup reminder after `MainWindow.Opened` |

No ViewModel touches files or processes directly (core/service-api.md).

## Core API (`PzLauncher.Core/Steam/`)

`ISteamLaunchHookService` (public, registered in `AddPzLauncherCore`):

- `Task<Result<SteamLaunchHookStatus>> GetStatusAsync(string launcherExe, ct)`
  - `SteamLaunchHookStatus`: `Account` (`AccountId`, `PersonaName`),
    `State` (`Off`, `On`, `OtherLauncher`), `HookedPath` (for
    `OtherLauncher`), `IsSteamRunning`.
  - Unavailable cases (R5) are errors: `steam.not_installed` (existing),
    `steam.no_current_account`, `steam.app_not_in_local_config` (NotFound).
- `Task<Result<SteamLaunchHookApplied>> SetAsync(string launcherExe, bool enabled, bool restartSteam, ct)`
  - If Steam runs and `restartSteam` is false → `steam.client_running`
    (Conflict). With `restartSteam`: start `steam.exe -shutdown`, wait for
    every `steam` process to exit (60 s, `TimeProvider`) → else
    `steam.shutdown_timed_out`, nothing written.
  - Edit (below), then start `steam.exe` again if it was running. A failed
    restart is a warning on the success result (`steam.start_failed`), not a
    failure: the hook is written.
  - Each step idempotent: a retry re-reads and re-applies.

Internals (internal, unit-tested without the service):

- `SteamLoginUsers`: parse `config/loginusers.vdf` with `SteamKeyValues`;
  `MostRecent == "1"` user → `accountId = steamId64 & 0xFFFFFFFF`, persona.
- `SteamLaunchOptions` (pure): `IsHook(value, exe)`, `FindHookPath(value)`,
  `AddHook(value, exe)`, `RemoveHook(value)` per PRD R3. A hook token is a
  quoted absolute path whose file name is `PzLauncher.exe` (case-insensitive)
  immediately followed by `%command%`. Paths compared with
  `IPlatformEnvironment.PathComparison` after `PathNormalizer`.
- `LocalConfigEditor`: locate and replace one value without re-serialising.
  ValveKeyValue is not used for the write because it loses number formatting
  (`SteamKeyValues.AsText` note) and reorders nothing we can rely on.
  - A small non-recursive KV1 text scanner (quoted strings with `\\` `\"`
    escapes, unquoted tokens, `{` `}`, `//` comments; depth-limited) yields
    tokens with byte offsets.
  - Path `UserLocalConfigStore/Software/Valve/Steam/apps/108600`, keys
    case-insensitive. Zero matches → `app_not_in_local_config`; more than one
    match for the app block or for `LaunchOptions` inside it →
    `steam.local_config_malformed`.
  - Existing `LaunchOptions` → replace the value token's span. Missing →
    insert `\n<indent>"LaunchOptions"\t\t"<value>"` before the block's
    closing brace, indent copied from the previous key line, terminator from
    the file.
  - Values written with Steam's escaping (`\` → `\\`, `"` → `\"`).
  - Invalid UTF-8 → `steam.local_config_malformed`. BOM and line terminators
    kept.
  - Write per error-handling.md: re-read just before writing, rolling
    `localconfig.vdf.pzlauncher.bak`, `AtomicFile.WriteAsync`, no write when
    unchanged. I/O → `steam.local_config_unwritable` (External).
- Steam client control via seams:
  - `IProcessRunner` gains `IReadOnlyList<IRunningProcess> FindByName(string)`
    (fakeable); `ProcessRunner` wraps `Process.GetProcessesByName`.
  - Steam exe: `<steam root>/steam.exe` from `SteamLocator` (registry first).

New `ErrorCode` members (wire names, all need resx text + `ErrorCodeTests`):
`steam.no_current_account`, `steam.app_not_in_local_config`,
`steam.local_config_malformed`, `steam.local_config_unwritable`,
`steam.client_running`, `steam.shutdown_timed_out`, `steam.shutdown_unconfirmed`
(listing Steam processes failed while waiting), `steam.start_failed`.

## Self-update restart

`UpdateService.StartInstalledAsync` passes `LauncherReleases.RestartArgument`
(`--pzlauncher-restart`). Desktop's single-instance guard waits for the
mutex (bounded, 2 min — the close flush is bounded) when that argument is
present instead of activating and exiting.

## Single instance (Desktop, Windows only)

`Services/Shell/SingleInstanceGuard` runs in `Program.Main` before
`BuildAvaloniaApp` (no Avalonia types used there):

- Key: `PzLauncher-` + first 16 hex of SHA-256 of the normalised,
  upper-invariant `AppContext.BaseDirectory`. Names under `Local\` (per
  session).
- `Mutex` acquired with timeout 0 → first instance. Otherwise: call
  `AllowSetForegroundWindow(ASFW_ANY)` (`LibraryImport` user32, like the DPAPI
  protector), set the `EventWaitHandle` `<key>-activate`, exit 0.
- First instance: a background thread waits on the event and posts to the UI
  thread: un-minimise, `Activate()` the main window.
- Mutex released at process exit (OS also releases on crash, surfaced as
  `AbandonedMutexException` → treated as acquired).
- Off Windows: no guard (D5).

## Preference

`steamHookReminder`: `true`, `false`; anything else loads as null (remind).
`UserPreferences.SteamHookReminder`, `SaveSteamHookReminderAsync`. Same
pattern as `autoCheckUpdates`. Spec table row added in
`.trellis/spec/desktop/preferences.md`.

## Dialog

`IDialogService.ConfirmWithOptionAsync(ConfirmPrompt, string optionText)` →
`ConfirmOptionResult(bool Confirmed, bool OptionChecked)`. Closing the window
is `Confirmed = false` with the checkbox's current value.

## Desktop flow

- `ViewModels/Settings/SteamSettingsViewModel` (singleton): loads status on
  section open (`SettingsViewModel.OnSectionChanged`), exposes
  `IsAvailable`, `UnavailableText`, `AccountName`, `IsHookOn`,
  `OtherLauncherPath`, `RemindAtStartup`, busy state. Toggling runs
  `SteamLaunchHookFlow`.
- `Services/Steam/SteamLaunchHookFlow` (shared by the switch and the
  reminder): get status → if Steam running, D2 confirm → `SetAsync` →
  report error/warnings via `IDialogService` → return the new status.
  Launcher exe = `Environment.ProcessPath`.
- Startup reminder: `App.axaml.cs` on `MainWindow.Opened`, Windows only,
  after `updates.StartAsync()` is kicked off: status Off/OtherLauncher and
  preference not false → `ConfirmWithOptionAsync`; save the checkbox when
  checked; on 立即开启 run the flow. Status errors are logged only.
- `SettingsSection.Steam`; NavItem under 游戏 group, hidden off Windows.

## Compatibility / rollback

- Disabling restores prior launch options (R3); the `.pzlauncher.bak` holds
  the previous file for manual recovery.
- No preference value changes meaning; one new optional field.
- Removing the feature leaves at most a hook in Steam that points to an exe
  that still works.
