# Implement: Preload Zabric in allow-all mode

## Checklist

### Core

1. `ZombieBuddyAgentArgs.Option` (+ `IsAllowAll` on top of it) + tests:
   `config_dir` read, last wins, key case-insensitive, absent → null.
2. `ZabricFiles` constants; `ZabricModInfo` (value lookup, preloadable check,
   manifest read) + tests with temp jars built via `ZipFile` (manifest with
   and without `ZB-Preload`, continuation line, lower-case name, missing jar,
   no `javaPkgName`, `common/` fallback).
3. `ZabricLocator` + tests with `TempDirectory` / `SteamLayoutBuilder`:
   order staged Workshop → Steam → `mods`; `-cachedir=` (quoted path, last
   wins); item `3820726385` first; highest version folder; `id` mismatch
   skipped; nothing found.
4. `ZombieBuddyConfigEditor` + tests: missing file/folder created; current by
   same path (case-insensitive on Windows) and by identical bytes → no write;
   replaced when jar differs or missing; `Zabric` moved first; other entries,
   `trusted_authors`, unknown fields survive (fixture copied from
   `~/.zombie_buddy/config.json` shape); `[]` / bad json / `preload_mods: 1`
   → `preload_config_invalid`, file unchanged; locked file → `io_failed`.
5. Error codes, factories, `ErrorCodeTests`; `ZabricPreload` enum in
   `CoreJsonContext` + round-trip test.
6. `ZombieBuddyService.EnsureZabricPreloadAsync` + tests: not allow-all /
   unsupported / json unreadable / no Zabric → `NotApplicable`; `config_dir`
   honoured (relative → game folder); `Written` then `Current` on re-run.

### Desktop

7. `IZombieBuddyService` fakes (`Fakes.cs`, `DesignData`).
8. `HomeViewModel.LaunchAsync` call + tests: called with the install's
   `ExtraGameArgs` after the gate; error → error dialog then launch; setup
   cancelled → not called; success → no dialog.
9. Error texts in all 10 resx files + `ErrorMessages` mapping.

## Validation

```
dotnet build PzLauncher.slnx -warnaserror
dotnet test PzLauncher.slnx
```

Manual (Windows, this machine): back up `~/.zombie_buddy/config.json`, delete
its `Zabric` entry, 启动 from Home → entry points at
`~/Zomboid/Workshop/Zabric/Contents/mods/Zabric/42`, Zabric first; game log
shows `Preloading pkg 'com.pzlauncher.zabric'`. Restore the backup afterwards.

## Risky points

- `config.json` is ZombieBuddy's file shared with servers: tree edit, atomic
  write, round-trip tests (AC3).
- Hashing the jar on every launch: small file, only when paths differ.
