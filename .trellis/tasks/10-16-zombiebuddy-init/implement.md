# Implement: Initialize ZombieBuddy for subscribed installs

## Checklist

### Core

1. Move `ResolveGameFolder` to `PzLaunchFiles`; `GameLaunchService` uses it.
   Add `JavaRuntime.HasInstrumentLibrary` (`JavaLocations` names, discovery,
   `JavaHomeBuilder`, inspect and round-trip tests).
2. `ZombieBuddy/ZombieBuddyFiles.cs`, `ZombieBuddyAgentArgs.cs` + tests
   (add/remove/has, `=options` kept, both OSes, Linux-style arg on Windows
   counts as leftover; policy on/off with no options, with `verbosity=1`
   before/after, replacing `deny-new`, last item removed → bare arg).
3. `Launch/PzLaunchConfigEditor.cs` + tests (fixtures: shipped json, json
   already with `-agentlib:zbNative=policy=allow-all`, unknown fields,
   per-Windows `vmArgs`, missing file, malformed).
4. `ErrorCode` `zombiebuddy` section, `ZombieBuddyErrors`, `ErrorCodeTests`.
5. DTOs + `CoreJsonContext` registration + round-trip tests.
6. `IZombieBuddyService` / `ZombieBuddyService`, DI registration; tests with
   `SteamLayoutBuilder` / `GameInstallBuilder` / `TempDirectory`: status for
   each state, own-library-first lookup, both channels, initialize/remove file
   and json results, idempotent re-run, progress sequence, cancellation,
   locked-file error, Linux variant, macOS/server unsupported;
   `SetAllowAllAsync` write / no-op / `not_initialized`; `IsAllowAll` in
   status.

### Desktop

7. Preferences: `zombieBuddyChannels` field, mapping, save method,
   `JsonPreferenceStoreTests` (round-trip, missing, unknown, keeps others,
   unwritable); `IZombieBuddyChannelService`; update
   `.trellis/spec/desktop/preferences.md` table.
8. `GameRunState`; Home uses it (existing Home tests stay green).
9. `ZombieBuddySetupViewModel` + tests (auto start, step states, success →
   Launch enabled, failure → failed step + Retry, cancel, close-only mode).
10. `ZombieBuddySetupView` + `IDialogService.ShowZombieBuddySetupAsync` in
    `AvaloniaDialogService`; test fake in `TestSupport/Fakes.cs`.
11. Home launch gate + tests (no dialog when `None`; dialog false → no launch;
    true → launch; status error → launch path).
12. 启动设置 section + tests (visibility, buttons per state, channel choice
    saved, refresh after dialog, disabled while running, instrument-library warning,
    allow-all switch: loads from status, toggling calls the service, error
    reverts, warning while on).
13. Resx keys in all 10 files; `DesignData` entries for the new UI.

## Validation

```
dotnet build PzLauncher.slnx -warnaserror
dotnet test PzLauncher.slnx
```

Manual (Windows, this machine): rename the game folder's `zbNative.dll` →
启动 shows the dialog → finishes → 启动 starts the game with the ZombieBuddy
banner; 启动设置 shows 已初始化.

## Risky points

- `ProjectZomboid64.json` is the user's file: atomic write, and tests prove
  unrelated content survives (AC3).
- Moving `IsGameRunning` out of Home touches existing Home tests.
- `ZombieBuddy.jar` may be locked by a game started outside the launcher →
  `copy_failed`, covered by 重试.
