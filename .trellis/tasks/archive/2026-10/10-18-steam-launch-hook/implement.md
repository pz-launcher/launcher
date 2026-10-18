# Implement: Steam launch hook

## Checklist

1. Core `Steam/`: `SteamLaunchOptions` (pure transform) + tests (no
   options, plain args, ZombieBuddy `-agentlib:zbNative --`, existing
   `%command%` with env prefix, other-path hook, remove round-trips).
2. Core `Steam/`: KV1 offset scanner + `LocalConfigEditor` + tests with a
   fixture copied from a real `localconfig.vdf` (anonymised): replace,
   insert, duplicate block → malformed, invalid UTF-8, BOM/CRLF kept, `.bak`,
   unchanged → no write, deep nesting bounded.
3. Core `Steam/`: `SteamLoginUsers` + tests (MostRecent, none, malformed).
4. Core `Platform/`: `IProcessRunner.FindByName`; update fakes.
5. Core: `ISteamLaunchHookService` + `SteamLaunchHookService`, errors in
   `SteamErrors`, `ErrorCode` members, DI registration; service tests with
   fake platform/processes/`TimeProvider` (running → conflict, shutdown
   timeout → nothing written, restart failure → warning).
6. Core `Update/`: restart argument in `StartInstalledAsync` + test.
7. Desktop resx (10 files): section/dialog text and `Error_*` keys;
   `ErrorMessages` arms.
8. Desktop preference `steamHookReminder` + `JsonPreferenceStore` tests;
   spec row in `.trellis/spec/desktop/preferences.md`.
9. Desktop `IDialogService.ConfirmWithOptionAsync` + Avalonia impl + fake.
10. Desktop `SteamLaunchHookFlow`, `SteamSettingsViewModel`, Settings view
    section + `DesignData`, `SettingsSection.Steam`; VM tests.
11. Desktop startup reminder in `App.axaml.cs` (logic in a testable
    service/VM method, App only wires it).
12. Desktop `SingleInstanceGuard` in `Program.Main` + activation wiring.
13. Spec updates (Phase 3.3): `core/error-handling.md` file list gains
    `localconfig.vdf`; `desktop/directory-structure.md` notes single
    instance and the Steam section.

## Validation

```bash
dotnet build PzLauncher.slnx -warnaserror
dotnet test PzLauncher.slnx
```

Manual (Windows, real Steam), in check phase:
- AC2/AC3 with Steam closed and running; inspect `localconfig.vdf` and `.bak`.
- AC4 Steam Play opens the launcher; AC5 double-start, second folder,
  restart-and-update.

## Risky points / rollback

- `localconfig.vdf` edit (step 2): highest risk; never re-serialise, keep
  `.bak`. Roll back by reverting the commit; users restore from `.bak`.
- Single instance (step 12) can block startup if wrong: abandoned mutex
  treated as acquired; restart arg waits bounded.
- `steam -shutdown` interrupts downloads: only after D2 confirm.
