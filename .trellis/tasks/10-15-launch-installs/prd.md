# Launch game and dedicated server installs (parent)

## Goal

Home's 启动游戏 button (currently a disabled no-op) starts whatever install is
selected on Home / 安装: a `Game` install starts the game, a `DedicatedServer`
install starts a server hosted by the launcher.

## Task map

| Child | Deliverable |
|---|---|
| `10-15-discover-java-runtimes` | Find installed Java runtimes; 设置 → Java list with rescan and manual add |
| `10-15-launch-game-client` | Start the selected Game install with a launcher-built java command; 版本设置 page; know whether it is running |
| `10-15-host-dedicated-server` | Start the selected server as a launcher-hosted process: live output, command input, status, graceful stop |

Order: `discover-java-runtimes` → `launch-game-client` → `host-dedicated-server`.
The game client picks a Java from the discovered list; the server builds on
the game client's Core launch feature, 版本设置 page and Home wiring
(dependencies written in each child's PRD).

## Confirmed facts (repository + local install, read-only)

- `PzLauncher.Desktop/ViewModels/Home/HomeViewModel.cs`: `LaunchCommand` is a
  `RelayCommand` whose `CanExecute` is always `false`; `SelectedInstall` comes
  from `InstallsViewModel.SelectedInstall` (Game or DedicatedServer, Steam or
  manual; session-only).
- `PzLauncher.Desktop/Views/Home/HomeView.axaml`: sidebar with 启动游戏
  (accent) and 版本选择 buttons; the content area left of it is empty.
- Core has no process/launch capability. Launch logic belongs in Core
  (`.trellis/spec/core/service-api.md`), returns `Result<T>` with typed
  `ErrorCode`s (`.trellis/spec/core/error-handling.md`); every new code needs
  `Error_<Member>` text in all 10 resx files.
- Game folder (Windows, Steam build 25485521):
  - `ProjectZomboid64.exe` reads `ProjectZomboid64.json` (`mainClass`,
    `classpath`, `vmArgs` incl. `-Xmx`, per-Windows-version GC args) and starts
    the bundled `jre64`. The user's copy is customised (`-Xmx23072m`,
    `-agentlib:zbNative=...` for ZombieBuddy), so the file is user-owned config.
  - `ProjectZomboid64.bat` / `ProjectZomboid64ShowConsole.bat` run
    `jre64\bin\java.exe` with hard-coded args and pass `%1 %2` as game args.
  - `steam_appid.txt` = `108600`: the exe initialises Steam without being
    started by Steam (Steam client must be running for `-Dzomboid.steam=1`).
- Server folder: `StartServer64.bat` runs `jre64\bin\java.exe ...
  zombie.network.GameServer -statistic 0 %1 %2` with hard-coded
  `-Xms16g -Xmx16g` and ends with `PAUSE`; it also has a (customised)
  `ProjectZomboid64.json` with `mainClass` `zombie/network/GameServer`. First
  start asks for an admin password on stdin.
- macOS/Linux install markers are unverified
  (`PzLauncher.Core/Installs/PzInstallMarkers.cs`).

## Decisions

- D1: Both kinds launch from the same Home button, by `SelectedInstall.Kind`.
- D2: The server is hosted by the launcher (not an OS console window): live
  output, command input (also used for the first-run admin password), running
  status, graceful stop (`quit`, so the world is saved).
- D3: Split into the children above (Java discovery added as its own child, D16).
- Game mechanism (decided in `10-15-launch-game-client`, D4/D9): the launcher
  builds the java command itself from the install's `ProjectZomboid64.json`
  plus per-install overrides, and starts the bundled `jre64` directly. The
  server's json has the same shape, so the server child reuses that config
  reader, the process seam and the 版本设置 page. Windows and Linux; macOS
  later (D8, revised).

## Cross-child acceptance criteria

- [ ] With a Game install selected, 启动 starts the game; with a
      DedicatedServer install selected, 启动 starts the hosted server.
- [ ] Core owns all launch/process logic; no ViewModel starts a process.
- [ ] `dotnet build PzLauncher.slnx -warnaserror` and `dotnet test
      PzLauncher.slnx` pass after each child.

## Out of scope (whole feature)

- Installing / updating via SteamCMD.
- Editing server config (`servertest.ini`, sandbox vars) or game options.
- `PzLauncher.Server` / web UI (Core API is designed so it can map to HTTP later).
