# Set up ZB Skip Online Checks at launch (preload + agent start)

## Goal

ZB Skip Online Checks (`ZBSkipOnlineChecks`) makes ZombieBuddy skip its
network requests (Steam ban check, known authors list, Steam profile keys)
that hold up every mod list load. It starts two ways, both of which need setup
outside the mod list:

- mod only: runs from Zabric's preLaunch, so it must be in ZombieBuddy's
  `preload_mods`; today Zabric registers it on the first launch and the player
  restarts once;
- agent start: a `patches_jar` entry in ZombieBuddy's agent arg in
  `ProjectZomboid64.json` plus the jar in the game folder; also covers
  ZombieBuddy's own requests at game start; today a manual README step.

On Home 启动 the launcher keeps both in step with the enabled mod list, as it
already does for Zabric's preload (`.trellis/tasks/archive/2026-10/10-16-zabric-preload`):
enabled → set up before the game starts, so the skip works from the first
launch and at game start; not enabled → both removed, so the skip is off, also
for later Steam launches.

## Background (repository evidence)

### The mod (`E:\Dev\Project\pz\dont39-pz-mods\zb-skip-online-checks`)

- Workshop item `3821335660`, layout `mods/ZBSkipOnlineChecks/{42,common}`;
  `42/mod.info`: `id=ZBSkipOnlineChecks`,
  `javaJarFile=media/java/ZBSkipOnlineChecks.jar`,
  `javaPkgName=com.dont39.pz.zbskiponlinechecks`, `javaPreload=true`,
  `require=\ZombieBuddy,\Zabric`, `loadModAfter=\Zabric`. Jar manifest:
  `ZB-Preload: true`, `Zabric-PreLaunch`, `Zabric-Main`. Installed here at
  `E:\SteamLibrary\steamapps\workshop\content\108600\3821335660`.
- README: for ZombieBuddy's `allow-all` policy only (under `prompt` /
  `deny-new` signed mods fail ZBS verification); Steam-banned Workshop jars are
  no longer skipped. Agent start: copy the jar into the game folder and append
  `patches_jar=ZBSkipOnlineChecks.jar:com.dont39.pz.zbskiponlinechecks` to the
  agent options; the path must be relative to the game folder. When both ways
  are set up the mod reports agent start; when the game-folder copy differs
  from the mod's jar it logs an error.
- `PreMain.premain` installs the skip only when called from `patches_jar`, and
  then regardless of the mod list (`PreMain.java`); the mod-only install runs
  from Zabric's preLaunch (`entrypoint/PreLaunchEntry.java`), which needs
  Zabric preloaded and first.
- This machine already has the manual agent start: the game json's agent arg is
  `-agentlib:zbNative=policy=allow-all,patches_jar=ZBSkipOnlineChecks.jar:com.dont39.pz.zbskiponlinechecks`
  and `E:\SteamLibrary\steamapps\common\ProjectZomboid\ZBSkipOnlineChecks.jar`
  exists.

### Zabric / ZombieBuddy behaviour

- Zabric calls preLaunch for every preloaded jar "whatever the mods' places in
  the mod list" (`zabric/.../entrypoint/Lifecycle.java:27`) and registers only
  mods loaded this pass, asking for a restart when the entry was new
  (`entrypoint/Preload.java:46-56`).
- ZombieBuddy injects every `preload_mods` id ahead of the mod list
  (`ZombieBuddy/.../Loader.java:708`): an entry loads the mod even when it is
  not in the list. It drops an entry only when the id leaves the `default` list
  during a session (`Loader.java:697-703`), so a list changed outside the game
  (e.g. a launcher collection apply) leaves the entry, and the skip, in place.
- `patches_jar` (`Agent.java:96-121`): entries joined by `;`, each
  `<path>:<package>`; agent options are split on `,` (`Agent.java:30`). Up to
  2.3.x the entry is split on the first `:` (`git show 733c129^`), so a Windows
  drive path breaks; the `lastIndexOf` fix is only in `v3.0.0-beta2`. A
  relative path resolves against the working directory, the game folder.
  `Loader.loadJar` with no approved hash is not gated by the policy.

### Launcher

- Zabric precedent: `PzLauncher.Core/Mod/Zabric/ZabricService.cs:19`
  (status supported + allow-all + config folder → `ModLocator.FindAsync` →
  `ZombieBuddyJavaMod.CheckPreloadableAsync` →
  `ZombieBuddyPreloadConfig.EnsureAsync(first: true)`). With `first: false`
  a replaced entry keeps its place and a new one goes last
  (`PzLauncher.Core/Mod/ZombieBuddy/ZombieBuddyPreloadConfig.cs:14-69`).
- Agent arg edits: `ZombieBuddyAgentArgs` (`Option`, `WithPolicy`,
  `PzLauncher.Core/Mod/ZombieBuddy/ZombieBuddyAgentArgs.cs:35,48`) saved through
  `PzLaunchConfigEditor.SaveVmArgsAsync` under `ZombieBuddyService`'s gate
  (`ZombieBuddyService.cs:109-140`). The game is started with the json's
  vmArgs, working directory = game folder (`GameLaunchService.cs:158-184`), so
  an edit before the launch call takes effect at once.
- Home launch order: default collection apply → `PrepareZombieBuddyAsync` →
  Zabric preload, skipped when the applied collection leaves ZombieBuddy out
  → launch (`PzLauncher.Desktop/ViewModels/Home/HomeViewModel.cs:150-198`).
- Enabled list: `<cache>/mods/default.txt`, read by the internal
  `ActiveModsFile.ReadAsync` (`PzLauncher.Core/Mod/Collections/ActiveModsFile.cs:32`);
  the apply writes it before this step, so a read there is the list the game
  starts with.

## Decisions (user, 2026-10-17)

- D1 Set up only when installed, allow-all is on, and `ZBSkipOnlineChecks` is
  in the enabled mod list for this launch.
- D2 When it is not in the enabled list, remove its `preload_mods` entry and
  keep everything else.
- D3 Agent start too: write the `patches_jar` entry into
  `ProjectZomboid64.json` (persistent, so Steam launches get it) and remove it
  there when the mod is not enabled.

## Requirements

- R1 Scope: runs on Home 启动 right after the Zabric step, under the same
  conditions as that step (Game install, ZombieBuddy not left out by the
  applied default collection), and only when ZombieBuddy's status is supported.
  Otherwise nothing happens. An unreadable or malformed `default.txt` also
  means nothing happens (logged).
- R2 Preload write (D1): `ZBSkipOnlineChecks` in `default.txt`, agent arg
  `policy=allow-all`, the Zabric step ended `Current` or `Written`, a ZombieBuddy
  config folder, and the mod found in the game's order (`ModLocator`,
  `-cachedir` honoured, item `3821335660` preferred within a Steam library) and
  preloadable → `ZombieBuddyPreloadConfig.EnsureAsync(first: false)`: Zabric
  stays first, a current entry (same files or byte-identical jar) is not
  rewritten, other content survives.
- R3 Preload remove (D2): not in `default.txt` (any policy) → its
  `preload_mods` entry is deleted; every other entry, its order and every other
  field survive; atomic write; no file or no entry → no write.
- R4 Agent start write (D1, D3): same conditions as R2 except Zabric's step is
  not required, plus the json has ZombieBuddy's agent arg →
  - the found jar is copied to `<game>/<jar file name>` when missing or not
    byte-identical;
  - the agent arg's `patches_jar` option holds exactly one entry for the mod's
    package, `<jar file name>:<javaPkgName>` (relative to the game folder):
    added when absent (a new option at the end, or appended to existing entries
    with `;`), replaced in place when it names another path; other entries and
    other options keep their order; the json is not rewritten when already so.
- R5 Agent start remove (D2, D3): not in `default.txt` (any policy) → every
  `patches_jar` entry for the mod's package is removed (the option is dropped
  when no entry is left; other options and entries keep their order), and
  `<game>/ZBSkipOnlineChecks.jar` is deleted; nothing to remove → no write.
- R6 Enabled but not allow-all, mod not found or not preloadable (agent
  start), or Zabric not preloaded (preload) → that part writes and removes
  nothing.
- R7 Failure: a found copy that is not preloadable, `config.json` not a JSON
  object or `preload_mods` not an object, a json read/write error, or a jar
  copy/delete error → localized error dialog with the reason, then the game
  launches as usual; the failed file is not modified. The preload and agent
  start parts run independently; both failures are logged, the first is shown.
  Errors reuse existing codes and texts (`zombiebuddy.*`, launch-config
  errors).

## Acceptance Criteria

- [ ] AC1 (R2) allow-all, Zabric registered, `ZBSkipOnlineChecks` in
      `default.txt` and installed in the Steam Workshop only → after 启动
      `preload_mods` holds `Zabric` first and `ZBSkipOnlineChecks` with that
      copy's `mod.info` and jar; a second 启动 writes nothing.
- [ ] AC2 (R4) Same setup, json agent arg `policy=allow-all` → after 启动
      `<game>/ZBSkipOnlineChecks.jar` equals the mod's jar and the agent arg is
      `...=policy=allow-all,patches_jar=ZBSkipOnlineChecks.jar:com.dont39.pz.zbskiponlinechecks`;
      a second 启动 rewrites neither file. With an existing
      `patches_jar=other.jar:x.y`, the result is
      `patches_jar=other.jar:x.y;ZBSkipOnlineChecks.jar:com.dont39.pz.zbskiponlinechecks`.
      A stale game-folder copy is replaced.
- [ ] AC3 (R2, R4) A default collection that enables it → both are set up at
      the same 启动 that applies the collection, and the game process is started
      with the new agent arg.
- [ ] AC4 (R6) Enabled but policy not `allow-all`, or the mod not installed →
      `config.json`, the game json and the game folder unchanged, no dialog,
      game starts. Zabric not preloaded → only the agent start is set up.
- [ ] AC5 (R3, R5) Existing entries (this machine's manual setup) and the mod
      absent from `default.txt` (e.g. a default collection without it) → after
      启动 the `preload_mods` entry, the `patches_jar` entry and the
      game-folder jar are gone; `Zabric`, other preload and `patches_jar`
      entries and other agent options keep their order; also under `prompt`.
- [ ] AC6 (R1) Server install, macOS, or a default collection without
      ZombieBuddy → no change from this step.
- [ ] AC7 (R7) Found copy lacking `ZB-Preload: true`, `config.json` holding
      `[]`, or a locked game-folder jar → error dialog naming the reason, that
      file unchanged, the game starts after the dialog closes.
- [ ] AC8 `dotnet build PzLauncher.slnx -warnaserror` and
      `dotnet test PzLauncher.slnx` pass.

## Out of Scope

- Setting up or removing at any time other than Home 启动 (Steam/exe launches
  use what the last Home 启动 left); reacting to the 启动设置 allow-all switch.
- ZombieBuddy 移除 deleting the game-folder jar (its agent arg, and so the
  entry, goes; a leftover jar is harmless and goes at the next Home 启动 with
  the mod not enabled).
- Any UI in 启动设置 or on the mods pages; dedicated servers; other preload or
  `patches_jar` mods; checking the mod's `versionMin` / `ZBVersionMin`;
  absolute `patches_jar` paths for ZombieBuddy 3.x.
