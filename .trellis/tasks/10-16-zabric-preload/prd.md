# Preload Zabric in allow-all mode

## Goal

Zabric is a ZombieBuddy Java mod that must be preloaded (loaded at Java-agent
startup). Under ZombieBuddy's `policy=allow-all` the approval dialog never
runs, so ZombieBuddy never records Zabric as a preload mod and Zabric misses
its preload. When 启动 is pressed on Home, the launcher registers the
installed Zabric in ZombieBuddy's own config first, so allow-all players get
it without the prompt flow.

## Background (repository evidence)

### ZombieBuddy (`E:\Dev\Project\pz\ZombieBuddy\java\src\main\java\me\zed_0xff\zombie_buddy`)

- Preload list: `<configDir>/config.json` → `preload_mods`,
  `{ "<modId>": { "infPath": "<abs mod.info>", "jarPath": "<abs jar>" } }`
  (`Config.java:14-26`). `configDir` = agent option `config_dir` (trimmed),
  else `<user.home>/.zombie_buddy` (`Agent.java:161`).
- Entries are added only by the approval flow: `applyBatchApprovalLines`
  (`Loader.java:349`) and the trusted-author path (`Loader.java:863`). Under
  allow-all, `isJarAllowedByPolicy` (`Loader.java:316`) approves without
  touching `preload_mods` → **the miss**.
- At agent start `Loader.preloadMods` (`Loader.java:435`) loads entries in map
  order and drops one whose `mod.info` has no `javaPkgName`, whose jar is
  missing, whose manifest lacks `ZB-Preload: true`, or that fails the ZBS
  check. Preloaded ids are injected ahead of the mod list (`Loader.java:708`).

### Zabric (`E:\Dev\Project\pz\dont39-pz-mods\zabric\src\main\java\com\pzlauncher\zabric`)

- Workshop item `3820726385`, layout `mods/Zabric/42/{mod.info,media/java/Zabric.jar}`;
  `mod.info`: `id=Zabric`, `javaJarFile=media/java/Zabric.jar`,
  `javaPkgName=com.pzlauncher.zabric`, `javaPreload=true`.
- Once running, Zabric keeps entries itself (`loader/PreloadConfig.java:34`);
  the launcher must use the same rules or the two would overwrite each other:
  - an entry is **current** when it names the same files, or both named files
    exist and the jar is byte-identical (sha256) (`:98-115`);
  - `Zabric` stays first in `preload_mods` (`:117-134`);
  - the json is edited as a tree (other fields survive); a non-object root or
    `preload_mods` is left untouched (`:50-63`); atomic write (`:173`);
  - preloadable = jar exists, `mod.info` has `javaPkgName` and
    `javaPreload=true`, manifest has `ZB-Preload: true` (`:80-96`); `mod.info`
    falls back to `common/` when the version folder's has no `javaJarFile`,
    the jar staying relative to the version folder (`:36-42`); `mod.info`
    values: first line whose lower-cased text starts with `key=` and has a
    non-empty value (`:137-152`).
- Game mod roots (`widener/ModFolders.java:47-58`; game order
  `workshop,steam,mods`, `dont39-pz-mods/.trellis/tasks/archive/2026-10/10-13-zb-kt-on-zabric/prd.md:40`).

### This machine

- `~/.zombie_buddy/config.json` already has a `Zabric` entry pointing at the
  dev copy under `~/Zomboid/Workshop/Zabric/Contents/mods/Zabric/42`; the game
  json has `-agentlib:zbNative=policy=allow-all`.

### Launcher

- ZombieBuddy status, agent-arg parsing (`policy`) and Workshop lookup:
  `PzLauncher.Core/ZombieBuddy/ZombieBuddyService.cs` (`GetStatusAsync:20`,
  `CandidateLibrariesAsync:270`), `ZombieBuddyAgentArgs.cs:30,100`.
- Home launch: `HomeViewModel.LaunchAsync` → `PrepareZombieBuddyAsync`
  (`PzLauncher.Desktop/ViewModels/Home/HomeViewModel.cs:84,121`); the only
  game args are the install's 额外游戏参数 (`LaunchArguments.Build`,
  `PzLauncher.Core/Launch/LaunchArguments.cs:16`); the json has none.
- `PzInstall` carries no game version (`PzLauncher.Core/Installs/PzInstall.cs`).

## Decisions (user, 2026-10-16)

- D1 Write whenever Zabric is installed and allow-all is on; no mod-list check.
- D2 Look in the game's order (staged Workshop, Steam, `mods`), first found
  wins, honouring `-cachedir`.
- D3 On failure show the reason, then launch as usual.

## Requirements

- R1 Trigger (D1): on Home 启动, after the ZombieBuddy setup gate, when the
  install is supported (Game, Windows/Linux), the json's agent arg carries
  `policy=allow-all`, and Zabric is found (R2). The mod list is not checked.
  Otherwise nothing happens. An unreadable game json is skipped (the launch
  reports it).
- R2 Lookup (D2): take the first `Zabric` mod folder in the game's order:
  1. `<cachedir>/Workshop/*/Contents/mods/Zabric`;
  2. `workshop/content/108600/*/mods/Zabric` in every candidate Steam library
     (same library order as the ZombieBuddy lookup), item `3820726385` first
     within a library;
  3. `<cachedir>/mods/Zabric`.
  `cachedir` = the last `-cachedir=` in the install's 额外游戏参数, else
  `<home>/Zomboid`. A folder counts only if its chosen `mod.info` says
  `id=Zabric`.
- R3 Entry (mirrors Zabric's `PreloadConfig`): version folder = the
  highest-numbered subfolder holding `mod.info` (fallback rules as Zabric's);
  paths absolute. If the existing `Zabric` entry is current, nothing is
  written unless `Zabric` is not first; otherwise the entry is replaced and
  moved first. Every other field and entry survives; atomic write; no write
  when unchanged.
- R4 Config location: agent option `config_dir` (trimmed; relative resolves
  against the game folder), else `<home>/.zombie_buddy`; missing file or
  folder is created.
- R5 Failure (D3): any failure (found copy not preloadable, config not a JSON
  object or `preload_mods` not an object, read/write IO error) shows a
  localized error dialog with the reason; after it is closed the game
  launches as usual. The config is not modified on failure.
- R6 Errors are typed `Result` errors with text in all 10 resx files.

## Acceptance Criteria

- [ ] AC1 (R1, R3) allow-all, Zabric in the Steam Workshop only, no
      `config.json` → after 启动 the file exists with
      `preload_mods.Zabric` = that copy's `mod.info` and jar; the game starts.
- [ ] AC2 (R2) Copies in staged Workshop, Steam and `mods` → the staged
      Workshop copy is written; with `-cachedir=D:\pz` in 额外游戏参数 the
      copies under `D:\pz` are used instead of `~/Zomboid`.
- [ ] AC3 (R3) An existing `Zabric` entry naming other files with a
      byte-identical jar is left as is (no write); one naming a missing or
      different jar is replaced; `Zabric` ends up first; other preload entries,
      `trusted_authors` and unknown fields survive.
- [ ] AC4 (R1) `policy` not `allow-all`, Zabric not found, server or macOS
      → config untouched, no dialog, game starts.
- [ ] AC5 (R4) `-agentlib:zbNative=config_dir=D:\zb,policy=allow-all` →
      `D:\zb\config.json` is written.
- [ ] AC6 (R5) Found Zabric lacking `ZB-Preload: true`, or `config.json`
      holding `[]` → error dialog with the reason, config unchanged, the game
      starts after the dialog closes.
- [ ] AC7 (R6) New error codes have resx text in all 10 languages;
      `dotnet build PzLauncher.slnx -warnaserror` and
      `dotnet test PzLauncher.slnx` pass.

## Out of Scope

- Writing the entry at any time other than Home 启动 (e.g. when the allow-all
  switch is turned on in 启动设置); Steam/exe launches are covered once the
  launcher has written it, after which Zabric keeps its own entry.
- Any Zabric status or UI in 启动设置.
- Preload for mods other than Zabric; removing a stale entry; the
  `-modfolders` game arg; picking the version folder by game version.
- Dedicated servers (`10-15-host-dedicated-server`); ZombieBuddy approvals
  (`mod_approvals.json`) and its other config fields.
