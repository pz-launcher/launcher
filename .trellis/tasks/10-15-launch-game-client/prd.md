# Launch the game client

Parent: `10-15-launch-installs` (shared facts and decisions D1–D3 live there).
Depends on `10-15-discover-java-runtimes` (Java list for the per-install pick).

## Goal

With a Game install selected, Home's 启动游戏 button starts Project Zomboid
from that install with a java command the launcher builds itself (from the
install's `ProjectZomboid64.json` plus per-install overrides — Java runtime,
memory, extra args — set in a new 版本设置 page), and shows when that game is running so it can't be started
twice.

## Background

- `PzLauncher.Desktop/ViewModels/Home/HomeViewModel.cs`: `LaunchCommand` is a
  `RelayCommand` with `CanExecute` always `false`.
- Game folder (Windows, Steam build 25485521), read-only inspection:
  - `ProjectZomboid64.json`: `mainClass` (`zombie/gameStates/MainScreenState`),
    `classpath` (`.`, `projectzomboid.jar`), `vmArgs` (user-customised:
    `-Xmx23072m`, `-agentlib:zbNative=policy=allow-all`, …), and a `windows`
    map of OS version → extra `vmArgs` (`10.0.17134` → Shenandoah GC,
    `6.1` → G1 GC). The server's file has the same shape (`7`, `10` keys).
  - Bundled JRE `jre64\bin\java.exe` / `javaw.exe` (Azul Zulu 25.0.1).
- Linux (`projectzomboid.sh` copied from the user's Ubuntu install; json from
  public sources; `research/linux-launch.md`): the Steam app folder holds
  `projectzomboid.sh` and a `projectzomboid/` subfolder; the script `cd`s into
  that subfolder (`ProjectZomboid64.json`, `jre64/`, `natives/`). Install
  detection reports the app folder as the root (the `.sh` marker). The stock
  json has no OS-version map. The script sets
  `LD_LIBRARY_PATH=natives:<game>:jre64/lib:$LD_LIBRARY_PATH`,
  `LD_PRELOAD=$LD_PRELOAD:libjsig.so:libPZXInitThreads64.so` and an empty
  `XMODIFIERS`, then runs the native `ProjectZomboid64`. macOS keeps JVM args
  in the app bundle's `Info.plist` instead of the json, so it needs a real
  bundle to design.
  - `ProjectZomboid64.exe` (not used) hosts the JVM in-process from that json;
    `ProjectZomboid64.bat` runs `java.exe` and passes `%1 %2` as game args.
  - `steam_appid.txt` = `108600`: the game initialises the Steam API itself
    inside the JVM, so Steam shows it as in-game when started this way
    (confirmed by the user). No Steam overlay; Steam launch options don't apply.
- The only dialog today is the ad-hoc unexpected-error window in
  `PzLauncher.Desktop/App.axaml.cs` (`ShowUnexpectedErrorDialogAsync`).
- Home sidebar has 启动游戏 and 版本选择; 版本设置 was out of scope in
  `10-15-home-shell-titlebar-nav`. Navigation: `ShellPage` (Home, Installs,
  Settings) in `MainViewModel`; pages request navigation through events
  (`.trellis/spec/desktop/shell-and-theming.md`).
- Saved choices live in `preferences.json` via `IPreferenceStore`
  (`.trellis/spec/desktop/preferences.md`: new field nullable, own save method,
  stable mapping; saves are fire-and-forget).

## Decisions

- D4 Mechanism: start the chosen Java (D14) — `bin\javaw.exe` on Windows (no
  console window), `bin/java` on Linux — with a launcher-built command, the
  game folder (D18) as working directory; on Linux also the environment from
  `projectzomboid.sh` (`LD_LIBRARY_PATH`, `LD_PRELOAD` with `libjsig.so` and
  `libPZXInitThreads64.so`, empty `XMODIFIERS`).
  Not `ProjectZomboid64.exe`, not Steam.
- D18 Game folder: the folder holding `ProjectZomboid64.json` and `jre64` —
  the install root, or else its `projectzomboid` subfolder (Linux Steam
  layout). Overrides stay keyed by the install root (D10).
- D9 Arguments: from the install's `ProjectZomboid64.json` (`mainClass`,
  `classpath`, `vmArgs`, plus the `windows` entry for the current OS version),
  then launcher overrides: max memory (replaces `-Xmx`), min memory
  (replaces `-Xms`), extra JVM args (after the json's), extra game args
  (after the main class).
- D10 Overrides are per install, keyed by install root path (platform path
  comparison), persisted across restarts.
- D11 UI: Home sidebar gets 版本设置 next to 版本选择; it opens a 版本设置
  sub-page for the selected install (back button; Home tab stays highlighted;
  not a top nav tab) with a 启动参数 section. Later per-install settings go
  on this page.
- D12 Memory: two radios, 跟随 json（当前 N MB）(default) and 自定义; 自定义
  shows a slider from 2 GB to the machine's physical memory, step 0.5 GB, with
  the value and the machine's total memory displayed.
- D13 Saving: every change applies and saves immediately (like 设置 → 外观);
  no Save button.
- D14 / D16 Java is a per-install override: 版本设置 has a Java dropdown —
  first item 游戏自带（<vendor> <version>）= that install's own `jre64`
  (default), then the available runtimes from 设置 → Java
  (`10-15-discover-java-runtimes`).
- D17 Java compatibility: the dropdown lists every 64-bit Java; when the
  chosen Java's major version is lower than the bundled JRE's, 版本设置 shows
  a warning, launch is still allowed. Unknown version → no warning.
- D15 Min memory (`-Xms`) is an override too, same UI as max memory: radios
  跟随 json（未设置 / 当前 N MB）and 自定义 + slider from 512 MB to the
  effective max memory. Lowering max below min pulls min down with it, so
  Xms ≤ Xmx always holds.
- D5 After launch: the launcher tracks the game process; the window is not
  minimised or closed. A configurable "after launch" behaviour is a later
  settings task.
- D6 Errors: a failed launch shows a modal error dialog with localized text
  from the error code. Game exit codes are not interpreted.
- D7 Running scope: only the game started in this launcher session counts.
  Closing the launcher leaves the game running; after a launcher restart it
  is not detected.
- D8 Platforms (revised): Windows and Linux. On macOS, launch fails with a
  "not supported on this platform yet" error (dialog); macOS is a later task
  once a real app bundle can be inspected. The user smoke-tests on Linux.

## Requirements

- R1 With a Game install selected and no game running, 启动游戏 is enabled;
  clicking it starts the chosen Java from that install with the command from D9
  (Steam and manual installs alike).
- R2 While the launched game runs, the button reads 游戏运行中 and is disabled
  for every Game install; when the game exits it returns to 启动游戏. Page and
  language switches keep this state.
- R3 With a DedicatedServer install selected, or none, 启动游戏 and 版本设置
  are disabled (the server child enables servers).
- R4 版本设置 page (D11–D17) for the selected Game install shows its path and:
  - Java: dropdown per D14/D16, D17 warning under it. A saved Java that is no
    longer available shows as 找不到 (still selected, so the user sees it) and
    launch fails with `launch.java_missing`.
  - 最大内存: D12 radios + slider; 跟随 json shows the json's `-Xmx` value in
    MB (or 未设置 when the json has none).
  - 最小内存: D15 radios + slider (512 MB … effective max); lowering max
    pulls a custom min down.
  - 额外 JVM 参数 and 额外游戏参数: one-line text boxes; space-separated,
    double quotes group a value containing spaces. The json's `vmArgs` are
    shown read-only for reference.
  - If the json can't be read, the page shows the error text and the memory
    control still allows 自定义.
  - Changes save immediately and apply to the next launch of that install.
- R5 Launch failures show a modal error dialog: unsupported platform; game
  already running; install no longer valid (existing `install.*` codes);
  folder is not a game install; `ProjectZomboid64.json` missing or malformed;
  chosen Java executable missing (bundled or selected); invalid memory values
  (≤ 0, or min > max); OS refused to start the process.
- R6 Launch logic (json reading, OS-version vmArgs, overrides, argument
  splitting, single-running-game rule) lives in Core; ViewModels call Core and
  show results. Per-install overrides are stored by the desktop app and passed
  to Core on launch.
- R7 All new texts exist in all 10 `UiText*.resx` files.

## Acceptance Criteria

- [ ] AC1 (R1, R6, D18) Core tests (Windows and Linux fake platforms): the built
      command is `<game>/jre64/bin/javaw.exe` (Linux: `…/bin/java`), or the
      chosen Java home's, working dir `<game>` (= root, or `<root>/projectzomboid`
      for the Linux Steam layout); Linux adds `LD_LIBRARY_PATH`, `LD_PRELOAD`
      (inherited values kept, as in `projectzomboid.sh`) and empty
      `XMODIFIERS`, and joins the classpath with `:`;
      args = json `vmArgs` + matching `windows` vmArgs (Windows only) +
      extra JVM args + `-cp .;projectzomboid.jar` + main class + extra game
      args; works for a manual install.
- [ ] AC2 (D9, D15) Core tests: max/min overrides replace an existing
      `-Xmx`/`-Xms` (any unit) or are added when absent; no override keeps the
      json value; min > effective max → `launch.invalid_memory`; the
      `windows` entry chosen is the highest version ≤ the OS version (none when
      all are higher); quoted extra args stay one argument.
- [ ] AC3 (R5, D8) Core tests assert each code: platform unsupported
      (macOS), already running, not a game (server folder), config missing,
      config malformed, java missing, invalid memory, start failed, and an
      `install.*` code passed through.
- [ ] AC4 (R2, D7) Core tests: after the tracked process exits a new launch
      succeeds; waiting for exit completes on exit and immediately when nothing
      runs; waiting honours cancellation.
- [ ] AC5 (R1–R3) ViewModel tests: launch enabled only for a Game install with
      no game running; success → running → exit → enabled; failure → dialog
      with the error's user text; stored overrides (incl. Java home) of the
      selected install are passed to Core.
- [ ] AC6 (R4, D10, D13–D17) ViewModel/store tests: 版本设置 shows the json
      values; Java dropdown = 游戏自带 + available runtimes; lower-major warning
      shown/hidden; missing saved Java shown as 找不到; lowering max pulls min
      down; changing Java, memory mode/value or args saves for that install only; another
      install keeps its own values; values survive a store reload; preferences
      round-trip, missing, invalid, keeps other fields.
- [ ] AC7 (R7) Resx parity passes; new codes pinned in `ErrorCodeTests`; new
      DTOs round-trip through `CoreJsonContext`.
- [ ] AC8 `dotnet build PzLauncher.slnx -warnaserror` and `dotnet test
      PzLauncher.slnx` pass.
- [ ] AC9 Manual smoke (Windows, PZ via Steam): 启动游戏 starts the game with
      no console window and the json settings (incl. the zbNative agent);
      button shows 游戏运行中 until quit; in 版本设置 choose Adoptium 25, max
      8 GB, min 4 GB, extra game arg `-debug` → next launch uses them (visible
      in the process command line); choose Adoptium 21 → warning shown;
      restart the launcher → settings kept; selecting the server
      disables both buttons.
- [ ] AC10 Manual smoke (Linux, PZ via Steam, done by the user): the game
      starts from 启动游戏 with the json settings; a chosen system Java and
      custom memory apply; 游戏运行中 until quit.

## Out of Scope

- Dedicated server launch and server settings (`10-15-host-dedicated-server`).
- Discovering and managing Java runtimes (`10-15-discover-java-runtimes`).
- Launching on macOS, through Steam, or via `ProjectZomboid64.exe` /
  `projectzomboid.sh`.
- Editing `ProjectZomboid64.json`; a global (all-installs) override set;
  presets such as `-nosteam` / debug toggles beyond free-text game args.
- Detecting a game started outside this session; stopping the game from the
  launcher; "after launch" minimise/close settings.
- Interpreting game exit codes or crash logs; showing the final command line.
