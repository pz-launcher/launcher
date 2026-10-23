# Web server management (parent)

## Goal

Run a Project Zomboid dedicated server on a headless machine (typically a
Linux VPS) and manage it entirely from a browser: install / update it, create
and configure named servers, start and stop them, and watch and type into the
console. The user never needs an SSH session for day-to-day server work.

## Background (repository, 2026-10-23)

- `.trellis/spec/common/architecture.md` already reserves `PzLauncher.Server`
  ("ASP.NET Core host + web UI for headless Linux servers", depends only on
  Core; spec layer `server/` created when the project exists).
  `.trellis/spec/core/service-api.md` defines the Core → HTTP mapping (method
  → endpoint, `Result` → 200 / RFC 7807 with `code`, `IAsyncEnumerable` → SSE,
  cancellation → request abort).
- Solution today: `PzLauncher.Core`, `PzLauncher.Desktop` (Avalonia 12) and
  their tests; both `IsAotCompatible`. No web host, no auth code, no Node.
  `release.yml` publishes Native AOT packages for `win-x64` and `linux-x64`.
- Dedicated server hosting doesn't exist anywhere: desktop task
  `10-15-host-dedicated-server` is `planning` with an empty PRD. Core detects
  `PzInstallKind.DedicatedServer` installs
  (`PzLauncher.Core/Installs/InstallDetectionService.cs:108`; Linux marker
  `start-server.sh` unverified, `PzLauncher.Core/Installs/PzInstallMarkers.cs`)
  and `GameLaunchService` rejects them
  (`PzLauncher.Core/Launch/GameLaunchService.cs:300`).
- Reusable Core pieces: the `ProjectZomboid64.json` reader and
  `GameLaunchOverrides` (`PzLauncher.Core/Launch/`), `LinuxLaunchEnvironment`,
  `IProcessRunner` (`IPipedProcess.SendInputAsync` closes stdin after one
  write, so it can't drive a console), `LauncherHttpClient`, `AtomicFile`,
  `ValveKeyValue` (acf parsing), `CoreJsonContext`.
- No Core code for server ini, `SandboxVars.lua`, RCON or SteamCMD.
- Server folder (Windows, verified): `StartServer64.bat` with hard-coded
  `-Xms16g -Xmx16g` and trailing `PAUSE`; a `ProjectZomboid64.json` with
  `mainClass` `zombie/network/GameServer`; first start asks for an admin
  password on stdin.

## Decisions

- D1: Headless `PzLauncher.Server` on the server machine, managed from a
  browser. The desktop launcher is not involved.
- D2: First version covers start / graceful stop / status; live console with
  command input; editing server config; installing and updating via SteamCMD.
- D3: One admin account. Password set on first start or by a CLI command,
  stored hashed; browser keeps a session cookie. Plain HTTP; docs recommend
  Caddy/nginx in front for HTTPS. No multi-user/roles, no built-in TLS.
- D4: One server install, several named servers (`-servername`, each with its
  own ini / SandboxVars / save). Create, edit and switch between them; only
  one runs at a time. Modelled as a list from the start.
- D5: The game server outlives `PzLauncher.Server` restarts: independent
  process, console = tail of `server-console.txt`, commands over PZ's RCON
  (manager writes `RCONPort` / `RCONPassword`), re-attach via PID file after a
  restart. First-run admin password passed as `-adminpassword`. Console lags
  the log slightly.
- D6: Linux is the primary target (fully verified, systemd docs); the Windows
  package is built, released and smoke-checked only.
- D7: Frontend is React + Vite + Ant Design (TypeScript), a static SPA over the
  JSON API + SSE, served by `PzLauncher.Server`. Server side is ASP.NET Core
  Minimal API (AOT-compatible).
- D8: i18n from the start (react-i18next, Ant Design locale follows); ships
  zh-Hans and en.
- D9: Config editing = typed form for common settings + raw-text tab for the
  full ini and `SandboxVars.lua` (syntax highlighting, format check before
  save, backup before every save).
- D10: SteamCMD auto-downloaded from Valve's official URL into the manager's
  data folder, or an existing install's path. App 380870, anonymous login,
  branch selectable (public / unstable = B42). Update only while stopped; UI
  offers one-click stop → update → start.
- D11: Server hosting lives in Core with a web-independent API. This task makes
  no desktop UI changes; `10-15-host-dedicated-server` reuses the Core service
  when it is planned (note added to its PRD).
- D12 (approved 2026-10-23): the web endpoint listens on
  `http://127.0.0.1:8090` by default; exposing it is an explicit config change
  (`--urls`), in line with D3's reverse-proxy recommendation.
- D13 (approved 2026-10-23): saving config while the server runs
  is allowed; the UI says it takes effect on the next start. Stop waits for
  RCON `quit` to finish; on timeout the UI offers a separate force-kill.
- D14 (2026-10-24): only B42 is supported; B41 servers are out of scope (no
  B41 fixtures, no workarounds for its Java 17 behaviour). The server ini is
  strict UTF-8, like the mod-list files.

## Task map

| Child | Deliverable |
|---|---|
| `10-23-server-host-core` | Core: named servers, detached start, re-attach, RCON commands, console stream, graceful stop / kill, status |
| `10-23-server-config-core` | Core: ini + `SandboxVars.lua` read/write, typed common settings, raw text with validation, backups |
| `10-23-steamcmd-core` | Core: fetch SteamCMD, install/update 380870 by branch with progress, stopped-only, stop → update → start |
| `10-23-server-web-host` | `PzLauncher.Server`: auth, HTTP/SSE endpoints over the Core services, serves the SPA, packaging, Linux docs |
| `10-23-server-web-ui` | `PzLauncher.Web` SPA: login/setup, servers, dashboard + console, config editor, SteamCMD, settings, zh/en |

Order and dependencies are in `implement.md`; technical shape in `design.md`.

## Cross-child acceptance criteria

- [ ] On a fresh Linux VPS with only `PzLauncher.Server` unpacked and started
      under systemd, an admin can, from a browser and without SSH: set the
      admin password, install the dedicated server via SteamCMD, create a named
      server, edit its common settings, start it, see its console, run a
      command (e.g. `players`) and see the response, and stop it with the world
      saved.
- [ ] Restarting `PzLauncher.Server` (`systemctl restart`) while the game
      server runs leaves the game server running; afterwards the UI shows it as
      running and the console and commands work again.
- [ ] Every API call except login / setup / static files returns 401 without a
      valid session.
- [ ] Core owns all server logic (hosting, config, SteamCMD); `PzLauncher.Server`
      only maps HTTP to Core and holds auth and settings. Core references no
      ASP.NET Core package.
- [ ] Windows package builds, starts, serves the UI, and can start/stop a
      server (smoke check).
- [ ] `dotnet build PzLauncher.slnx -warnaserror`, `dotnet test
      PzLauncher.slnx`, and the web app's lint + typecheck + build pass after
      each child; the AOT publish of `PzLauncher.Server` has no IL2xxx/IL3xxx
      warnings.

## Out of scope

- Desktop UI for server hosting (D11), macOS.
- More than one running server at a time; multiple admin accounts / roles;
  built-in TLS; self-update of `PzLauncher.Server`; Docker image.
- Deleting named servers or their saves; world backup / restore; scheduled
  restarts or announcements; dedicated player-management UI (kick / ban / whitelist
  are console commands); Workshop browsing (mods are edited as ID lists).
- The desktop's other 8 languages in the web UI (D8).
