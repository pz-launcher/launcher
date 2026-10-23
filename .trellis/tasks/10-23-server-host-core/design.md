# Design: host named servers in Core

Refines parent `../10-23-web-server-management/design.md` → "Core: hosting".
Facts cited as (F: section) are in `research/pz-server-runtime.md`.

## Public API (`PzLauncher.Core/Server/`)

```
ServerSite(InstallRoot, CacheDir, StateDir)            // absolute paths, from the host
NamedServer(Name, HasWorld, HasDatabase)
StartServerRequest(Site, Name, AdminPassword?, MaxMemoryMb?)
ServerStatus(State, Name?, ProcessId?, StartedAt?, LastFailure: Error?)
ServerState { Stopped, Starting, Running, Stopping }
ServerConsoleLine(Text, Kind)   Kind { Output, FileRestarted }

IServerHostService
  ListServersAsync(site)                 -> Result<IReadOnlyList<NamedServer>>
  CreateServerAsync(site, name)          -> Result<NamedServer>
  GetStatusAsync(site)                   -> Result<ServerStatus>
  StartAsync(StartServerRequest)         -> Result<ServerStatus>   (returns Starting)
  StopAsync(site)                        -> Result<ServerStatus>   (waits for exit)
  KillAsync(site)                        -> Result<ServerStatus>
  SendCommandAsync(site, command)        -> Result<string>         (RCON response)
  StreamConsoleAsync(site, backlogLines) -> IAsyncEnumerable<ServerConsoleLine>
```

All async with `CancellationToken ct = default`; DTOs registered in
`CoreJsonContext` and round-trip tested; `ServerHostService` is an
`internal sealed` singleton (`.trellis/spec/core/service-api.md`). One host
operation that changes state (create / start / stop / kill) runs at a time
(`SemaphoreSlim`). `ServerSite` is per call so the desktop can use the same
service later (parent D11).

## Source of truth: `<StateDir>/server/running.json`

Written atomically (`AtomicJsonFile`) right after a successful start:
`ProcessId`, `ProcessStartTime`, `Name`, `InstallRoot`, `CacheDir`,
`RconPort`, `RconPassword`, `Ready` (RCON answered once), `StopRequestedAt`.

- RCON port and password are captured **at start**: PZ reads them once, and
  the user may edit the ini while the server runs (parent D13), so later
  commands must use the values the running server has.
- The file holds a secret: the host creates `StateDir` owner-only (Linux
  `0700`); Core writes nothing else secret.
- No other in-memory state is authoritative, so a restarted host re-attaches
  by reading this file. A missing, unreadable or malformed file means
  Stopped (malformed is logged and deleted).

**Identity check** (re-attach and before kill): a process with `ProcessId`
exists, its name is `java`, and its start time equals `ProcessStartTime`
(± 1 s). This guards against PID reuse on every OS without reading other
processes' command lines (not portable on Windows).

## Named servers

- List = `<CacheDir>/Server/*.ini` file names; `HasDatabase` =
  `<CacheDir>/db/<name>.db` exists; `HasWorld` =
  `<CacheDir>/Saves/Multiplayer/<name>/` exists.
- Name rule: `[A-Za-z0-9_-]{1,64}`, unique case-insensitively (it becomes file
  names on every OS; PZ adds suffixes such as `_SandboxVars`).
- Create writes a minimal ini: `RCONPort=<PzServerFiles.DefaultRconPort>` and a
  random `RCONPassword` (24 URL-safe chars). PZ fills in every other key on
  first start (F: Ini).
- Ini access here is one small internal helper (`ServerIniFile`: read a key,
  set a key keeping all other lines). `10-23-server-config-core` builds its
  full model on top of it instead of adding a second ini reader.

## Start

1. Refuse when the running file passes the identity check
   (`server.already_running`, Conflict).
2. `IInstallDetectionService.InspectAsync(InstallRoot)` must be
   `DedicatedServer` (`server.not_a_server_install`). The ini must exist
   (`server.not_found`).
3. RCON: read `RCONPort` / `RCONPassword`; when the password is empty, generate
   one and write it back (RCON is off without one, F: RCON).
4. Admin password (F: First-run admin account): required when
   `db/<name>.db` doesn't exist (`server.admin_password_required`,
   Validation). When given while the db exists it **resets** the in-game
   admin password; the API docs say so. Never stored.
5. Command, reusing the game launch code: `PzLaunchConfig` from
   `PzLaunchFiles.ResolveGameFolder(InstallRoot)`; `config.JvmArgs(os,
   osVersion)` with `LaunchArguments.JvmArgs` applying `MaxMemoryMb`; plus
   `-Drconlo=1` unless present (RCON on loopback only, F: RCON); bundled
   `jre64`; classpath + main class via `LaunchArguments.Build`; game args
   `-statistic 0` (as `StartServer64.bat`), `-servername <name>`,
   `-cachedir=<CacheDir>` unless it equals PZ's default `<home>/Zomboid`,
   `-adminpassword <pw>` when given. Linux environment from
   `LinuxLaunchEnvironment` (to be confirmed on Linux).
6. `IProcessRunner.StartDetached` (below) → write the running file → return
   `Starting`.

## Status

`GetStatusAsync` reads the running file and checks identity:

- **Gone, never Ready** → Stopped with `LastFailure`:
  `server.admin_password_required` when the console tail contains
  `Enter new administrator password` (the failed-start case of F: First-run
  admin account, where the db exists but has no admin), otherwise
  `server.exited_early` with the last 30 console lines in `Details["output"]`.
- **Gone after Ready, no stop requested** → Stopped with
  `server.exited_unexpectedly` (same details).
- **Gone after a stop / kill** → Stopped, no failure.
- In every "gone" case the running file is deleted; `LastFailure` is kept in
  memory until the next start (a host restart loses it, acceptable).
- **Alive, not Ready** → try RCON auth at most every 2 s (`TimeProvider`);
  success sets `Ready` → Running; otherwise Starting.
- **Alive, stop requested** → Stopping. Otherwise Running.

## Stop and kill

- Stop needs `Ready` (Starting → `server.not_ready`, Conflict; the UI offers
  kill). Sends RCON `quit` (F: Stop: replies at once, exits ~8 s later),
  records `StopRequestedAt`, then polls the identity check every second up to
  `StopTimeout` (`internal init`, default 3 min: big worlds save longer).
  Timeout → `server.stop_timeout` with the state still Stopping.
- Kill re-checks identity, kills the process tree, deletes the running file.
  Allowed in any alive state.

## Commands (RCON client)

- Internal `IRconClient` / `RconClient`: TCP to `127.0.0.1:<RconPort>`, auth,
  `EXECCOMMAND`, then an empty `RESPONSE_VALUE` sentinel; concatenates
  response packets until the sentinel's id comes back (F: RCON). One
  connection per command (PZ allows 5). 30 s timeout per command.
- Validation: non-empty, no CR/LF, ≤ 4000 UTF-8 bytes (PZ rejects packets over
  4096) → `server.command_invalid`.
- Needs `Ready` (`server.not_ready` / `server.not_running`). Socket errors and
  a rejected password → `server.rcon_failed` with the reason.
- RCON commands don't appear in the console file (F: RCON), so the response
  is returned to the caller to show inline.

## Console stream

- Polls `<CacheDir>/server-console.txt` every 500 ms with `TimeProvider`
  (no file watcher: portable and testable), opened with
  `FileShare.ReadWrite | FileShare.Delete`.
- Starts by emitting the last `backlogLines` lines (read from the end, at most
  256 KB), then new complete lines; a trailing partial line waits for its
  newline.
- When the length drops below the read offset the file was truncated (new
  start, or the size limit mid-run, F: server-console.txt): emit one
  `FileRestarted` line and continue from 0. Lines written just before a
  mid-run truncation may be missed; acceptable.
- UTF-8, invalid bytes replaced. Runs while the server is stopped too (the
  file just doesn't change) until `ct` is cancelled.

## Process seam (`PzLauncher.Core/Platform/Processes/`)

New on `IProcessRunner` (fakes in `FakeProcessRunner`):

- `StartDetached(ProcessStartRequest) → DetachedProcess(Id, StartTime)`:
  stdin/stdout/stderr on the null device, own process group / session,
  survives the caller. Throws like `Start`.
  - Windows: `CreateProcessW` via `LibraryImport` with `DETACHED_PROCESS |
    CREATE_NEW_PROCESS_GROUP | CREATE_UNICODE_ENVIRONMENT` and
    `STARTF_USESTDHANDLES` on inheritable `NUL` handles — the exact setup the
    spike verified (F: Process). No job object.
  - Linux: `/bin/sh -c 'setsid "$0" "$@" </dev/null >/dev/null 2>&1 & echo $!'
    <java> <args…>`, reading the PID from sh's stdout (a background child of a
    non-interactive sh isn't a group leader, so `setsid` execs in place and
    `$!` is java's PID). The systemd unit uses `KillMode=process` (parent
    design, docs in `10-23-server-web-host`).
- `GetById(int id) → ProcessInfo?(Id, Name, StartTime)`; `null` when it
  doesn't exist or can't be read.
- `KillTree(int id)`: kills the process and its children; no-op once gone.

## Errors

New `ErrorCode`s (wire `server.*`): `name_invalid` (Validation),
`already_exists` (Conflict), `not_found` (NotFound), `not_a_server_install`
(Validation), `already_running` (Conflict), `not_running` (Conflict),
`not_ready` (Conflict), `admin_password_required` (Validation),
`start_failed` (External), `exited_early` (External), `exited_unexpectedly`
(External), `rcon_failed` (External), `command_invalid` (Validation),
`stop_timeout` (External). Problems with `ProjectZomboid64.json` or the JRE
reuse `launch.config_missing` / `launch.config_malformed` /
`launch.java_missing` (same files, same meaning). Each new code gets the
`ErrorCodeTests` pins, an `Error_<Member>` text in all 10 desktop resx files
and its `ErrorMessages` arm (`.trellis/spec/core/error-handling.md`).

## Constants

`PzLauncher.Core/Server/PzServerFiles.cs`: `Server` folder, `.ini`, `db`
folder + `.db`, `Saves/Multiplayer`, `server-console.txt`, ini keys
`RCONPort` / `RCONPassword`, default RCON port `27015`, `-Drconlo=1`, game
arg names, the admin-prompt marker text, default cache folder name `Zomboid`.

## Not in this child

Config editing (`10-23-server-config-core`), SteamCMD
(`10-23-steamcmd-core`), HTTP / auth / settings persistence
(`10-23-server-web-host`), any desktop UI. Desktop only gains error texts.
