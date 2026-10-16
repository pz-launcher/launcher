# Initialize ZombieBuddy for subscribed installs

## Goal

A player subscribed to the ZombieBuddy Workshop mod gets it working for a Game
install without running the separate CLI installer: the launcher copies its
files into the game folder and registers its Java agent in
`ProjectZomboid64.json`, from a 初始化 button on 启动设置 or automatically from a
progress dialog when 启动 is pressed on Home. When the mod is no longer
subscribed, the launcher removes what it left behind the same way. 启动设置
also has a switch for ZombieBuddy's allow-all mod policy.

## Background (repository evidence)

### Reference: ZombieBuddy CLI installer (`E:\Dev\Project\pz\ZombieBuddy\installer\installer.go`, v4.3)

- Mod ids: stable `3619862853`, beta `3812624292` (`installer.go:28-29`).
- Subscribed = folder `steamapps/workshop/content/108600/<id>` exists, looked
  up in the game's own Steam library first, then the Steam root, then every
  library in `libraryfolders.vdf` (`findZBPath`, `installer.go:625`).
- Install copies `zbNative.dll` and `ZombieBuddy.jar` from
  `<item>/mods/ZombieBuddy/libs/` into the game folder (`copyCoreFiles`,
  `installer.go:682`) and prepends `-agentlib:zbNative` to the json's
  top-level `vmArgs` when missing (`addZombieBuddyVMArg`, `installer.go:804`).
  Its `.bat` and Steam `localconfig.vdf` patches are irrelevant here: the
  launcher reads neither.
- An agent arg counts with or without options: `-agentlib:zbNative` or
  `-agentlib:zbNative=<opts>` (`isZombieBuddyJavaOption`, `installer.go:977`).
- Uninstall removes those args from the json and deletes `zbNative.dll`,
  `ZombieBuddy.jar`, `ZombieBuddy.jar.new` (`installer.go:740`).
- `ZombieBuddy/doc/Installation.md`: Linux/macOS need only `ZombieBuddy.jar`
  in the game folder plus `-javaagent:ZombieBuddy.jar`; Windows needs
  `zbNative.dll`, which loads the jar and applies a pending
  `ZombieBuddy.jar.new` self-update.
- `ZombieBuddy/c/windows/zbNative.c:25` calls `SetDllDirectoryA(".\\jre64\\bin")`
  then `LoadLibraryA("instrument.dll")` by bare name. Windows searches the
  exe's folder first, and this launcher starts `<JavaHome>\bin\javaw.exe`, so
  the selected Java's own `instrument.dll` (with its `java.dll`/`jli.dll`) is
  used, bundled or not. Only a Java without `java.instrument` (a trimmed
  jlink runtime) falls back to the bundled `jre64` copy (version mismatch);
  on Linux `-javaagent` fails outright without `lib/libinstrument.so`.
- Real layout on this machine:
  `E:\SteamLibrary\steamapps\workshop\content\108600\3619862853\mods\ZombieBuddy\libs\{zbNative.dll,ZombieBuddy.jar}`.

### Launcher

- The launcher starts java itself from `ProjectZomboid64.json` plus
  per-install overrides, working directory = game folder
  (`PzLauncher.Core/Launch/GameLaunchService.cs:123`, `ResolveGameFolder`
  `:295`; Linux nests the game in `projectzomboid/`). A json `vmArgs` entry
  therefore reaches both this launcher's launch and Steam/exe launches.
- `10-15-launch-game-client` kept the launcher from editing the json
  (archived `prd.md:290`); this task deliberately adds a narrow, user-requested
  edit (agent arg only).
- Core has no Workshop knowledge; Steam roots/libraries come from
  `SteamLocator` / `SteamLibraryReader`; `PzSteamApps.Game = 108600`.
- Home launch: `HomeViewModel.LaunchAsync`
  (`PzLauncher.Desktop/ViewModels/Home/HomeViewModel.cs:72`).
  `IDialogService` only has `ShowErrorAsync`.
- Per-install saved choices: `preferences.json` through `IPreferenceStore`
  (`.trellis/spec/desktop/preferences.md`).

## Terms

- **Subscribed**: at least one of the two Workshop items exists on disk.
- **Initialized** (subscribed install): every required file is in the game
  folder **and** the json's top-level `vmArgs` has the agent arg.
  Windows: `zbNative.dll` + `ZombieBuddy.jar`, `-agentlib:zbNative[=…]`.
  Linux: `ZombieBuddy.jar`, `-javaagent:ZombieBuddy.jar[=…]`.
- **Leftovers** (unsubscribed install): any of `zbNative.dll`,
  `ZombieBuddy.jar`, `ZombieBuddy.jar.new` in the game folder, or an agent arg
  in the json's top-level `vmArgs`.

## Requirements

- R1 Scope: Game installs (Steam or manual) on Windows and Linux. Dedicated
  servers and macOS show nothing and behave as today.
- R2 Detection: look for both Workshop items in the install's own Steam
  library, then every known Steam library (as the installer does). Missing or
  unreadable Steam data means "not subscribed", not an error.
- R3 Channel: if only one item is subscribed, use it. If both are, 启动设置
  shows a channel choice (稳定版 / 测试版), saved per install, default 稳定版.
  Changing it does not change the initialized state; the page says that
  初始化 must be run again to switch.
- R4 Initialize: copy the required files from `<item>/mods/ZombieBuddy/libs/`
  into the game folder (overwriting), then add the agent arg to the json's
  top-level `vmArgs` if no agent arg is present (an existing one, with its
  options, is kept). Every other json field and value is preserved. Running it
  on an initialized install refreshes the files (重新初始化).
- R5 Remove: delete the leftover files and remove every agent arg from the
  json's top-level `vmArgs`, preserving everything else.
- R6 Launch gate: pressing 启动 on Home with a selected Game install:
  - subscribed and not initialized → setup dialog in initialize mode;
  - not subscribed with leftovers → setup dialog in remove mode;
  - otherwise → launch exactly as today.
  The launcher adds no agent arg at launch time: the gate guarantees the json
  already has it (or no longer has it) before the game starts.
- R7 Setup dialog (modal over the main window):
  - starts the operation as soon as it opens;
  - lists the steps with per-step state (pending / running / done / failed)
    and an overall progress bar;
  - on success enables 启动; pressing it closes the dialog and launches with
    the current overrides;
  - 取消 is always available: closes without launching (an operation in
    progress is cancelled between steps);
  - on failure shows the failed step and the localized reason, and offers
    重试 (re-runs the whole operation; every step is idempotent).
- R8 启动设置 ZombieBuddy section, shown when subscribed or leftovers exist:
  - status: 未初始化 / 已初始化 / 未订阅（有残留）;
  - source: channel + Workshop path (when subscribed); channel choice per R3;
  - 初始化 (重新初始化 when initialized), when subscribed;
  - 移除, when not subscribed and leftovers exist;
  - both buttons open the setup dialog in that mode, with 关闭 instead of
    启动, and refresh the section when it closes;
  - when initialized and the selected Java (bundled or custom) has no
    instrument library (Windows `bin\instrument.dll`, Linux
    `lib/libinstrument.so`), a non-blocking warning that ZombieBuddy needs
    the `java.instrument` module and may fail to load with this Java;
  - the allow-all switch (R11), when initialized.
- R9 While a game started from this launcher is running, 初始化 / 移除 and the
  allow-all switch are disabled (the game holds the files open; the policy is
  read only at game start).
- R10 Errors: every failure is a typed `Result` error with text in all 10
  resx files; nothing throws past Core.
- R11 Allow-all switch (启动设置, shown when initialized): on = the json's
  agent arg carries `policy=allow-all`; off = no `policy` option (ZombieBuddy's
  default `prompt`). The switch reflects the json on load (any other `policy`
  value shows as off). Toggling rewrites only the `policy` option of that agent
  entry and keeps its other options (`verbosity=…` etc.) in order
  (`ZombieBuddy/doc/CommandLine.md`: options are `key=value` joined by `,`).
  It applies to every launch that reads the json, Steam included. While on,
  a warning says Java mods load without approval prompts and signature checks
  (`ZombieBuddy/doc/Installation.md`, policy table). 初始化 never changes the
  policy: an existing agent entry is kept as is, a new one has no options.

## Acceptance Criteria

- [ ] AC1 (R2, R6) Subscribed + game folder without the files → 启动 opens the
      setup dialog in initialize mode; the game does not start yet.
- [ ] AC2 (R4, R7) The dialog runs on open, shows each step finishing, then
      enables 启动; afterwards the files are in the game folder and the json's
      top-level `vmArgs` starts with the agent arg; 启动 launches the game.
- [ ] AC3 (R4) A json that already has `-agentlib:zbNative=policy=allow-all`
      keeps that exact entry and gains no second one; all other json content
      (fields, per-Windows `vmArgs`, unknown fields) survives a round trip.
- [ ] AC4 (R6) Subscribed + initialized → 启动 launches directly, no dialog.
- [ ] AC5 (R5, R6) Not subscribed + leftovers → 启动 opens the dialog in
      remove mode; afterwards the three files are gone and no agent arg is in
      the json; 启动 launches.
- [ ] AC6 (R6) Not subscribed, no leftovers → 启动 launches as today; 启动设置
      shows no ZombieBuddy section.
- [ ] AC7 (R7) 取消 at any point closes the dialog and no game starts; a
      failure (e.g. locked `ZombieBuddy.jar`) shows the failed step and reason,
      and 重试 succeeds once the cause is gone.
- [ ] AC8 (R3) Both items subscribed → 启动设置 shows the channel choice,
      default 稳定版; the choice survives a restart; 初始化 copies from the
      chosen item.
- [ ] AC9 (R8) 启动设置 shows the right status and buttons for the three
      states and refreshes after the dialog closes.
- [ ] AC10 (R1) Linux: initialize copies only `ZombieBuddy.jar` into the
      folder that holds the json and adds `-javaagent:ZombieBuddy.jar`
      (unit-tested; not verified on a real Linux install). macOS and dedicated
      servers: no section, no dialog.
- [ ] AC11 (R8) Initialized + selected Java without the instrument library →
      warning shown; a full JDK/JRE (custom or bundled) → no warning.
- [ ] AC12 (R10) New error codes have resx text in all 10 languages; resx
      parity tests pass. `dotnet build PzLauncher.slnx -warnaserror` and
      `dotnet test PzLauncher.slnx` pass.
- [ ] AC13 (R11) Initialized install: switching allow-all on turns
      `-agentlib:zbNative=verbosity=1` into
      `-agentlib:zbNative=verbosity=1,policy=allow-all` (Linux: the
      `-javaagent:ZombieBuddy.jar` entry); off removes only `policy=…`
      (`-agentlib:zbNative` when no options remain). A json with
      `policy=allow-all` loads as on; `policy=deny-new` loads as off and
      becomes `policy=allow-all` when switched on. Warning visible while on.

## Out of Scope

- Patching `ProjectZomboid64.bat` or Steam launch options; closing Steam.
- Comparing file versions/hashes with the Workshop copy (ZombieBuddy updates
  its jar itself via `ZombieBuddy.jar.new`).
- A per-install "use ZombieBuddy" switch, or launching without it while
  subscribed; UI for other agent options (`verbosity`, `deny-new`,
  `allow_unsigned_mods`, …).
- Removing an agent arg the user put in 启动设置's extra JVM args, or in the
  per-Windows-version `vmArgs`.
- Dedicated servers, macOS, subscribing/unsubscribing through Steam.
- Verifying the Linux path on a real Linux machine.
