# Design: web server management

Decisions D1–D13 are in `prd.md`; this file is the technical shape all
children share. Each child refines its part in its own `design.md` before
`task.py start`.

## Projects and boundaries

```
PzLauncher.Web (React SPA, Vite) ──build──> PzLauncher.Server/wwwroot
PzLauncher.Server (ASP.NET Core Minimal API, AOT) ──> PzLauncher.Core
PzLauncher.Server.Tests ──> PzLauncher.Server
```

- **Core** (`server-host-core`, `server-config-core`, `steamcmd-core`): every
  rule about PZ servers, RCON, ini/Lua, SteamCMD and processes. No ASP.NET
  Core reference (`.trellis/spec/common/architecture.md`). Desktop can call the
  same services later (D11).
- **PzLauncher.Server** (`server-web-host`): settings file, auth, HTTP/SSE
  mapping, background jobs, static files, CLI commands. No PZ knowledge.
- **PzLauncher.Web** (`server-web-ui`): UI only; talks to `/api/v1`.
- Spec layers: `server-web-host` creates `.trellis/spec/server/`,
  `server-web-ui` creates `.trellis/spec/web/`, and `architecture.md` drops the
  "Web stack and auth not decided" note (D3, D7).

## Core: where the server lives (`ServerSite`)

Core services take a `ServerSite` record per call (same pattern as
`installRoot` on `IGameLaunchService`): `InstallRoot` (dedicated server
folder), `CacheDir` (Zomboid user dir, PZ default `~/Zomboid` unless
configured; passed as `-cachedir=` only when non-default), `StateDir`
(manager-owned: PID file, backups, SteamCMD). The host owns persistence of
these paths; Core persists nothing outside `StateDir`.

Domain constants get one file each (`.trellis/spec/core/directory-structure.md`):
`Server/PzServerFiles.cs` (ini / `_SandboxVars.lua` / `server-console.txt` /
`db/<name>.db` names, RCON keys, default ports), `SteamCmd/SteamCmdFiles.cs`
(download URLs, `app_update` syntax, progress line format).

## Core: hosting (`IServerHostService`, child `server-host-core`)

Operations (one per future endpoint): list servers (from
`<CacheDir>/Server/*.ini`, excluding `_SandboxVars` / `_spawnregions` etc.),
create server (validated name → minimal ini with RCON settings), get status,
start (`StartServerRequest`: name, optional admin password, memory override),
stop (RCON `quit`, wait for exit with timeout), kill, send command (RCON,
returns response text), stream console (`IAsyncEnumerable<ServerConsoleLine>`).

- **Start**: java command built from the install's `ProjectZomboid64.json` via
  the existing `PzLaunchConfig` reader + `GameLaunchOverrides` (memory), Linux
  env via `LinuxLaunchEnvironment`; game args `-servername <name>`
  (+ `-cachedir=`, + `-adminpassword <pw>` when `db/<name>.db` doesn't exist
  is required while `db/<name>.db` doesn't exist; a failed first start leaves
  the db without an admin, detected from the console). Refused while any
  server runs (D4). Before start, ensure the ini has `RCONPort` and a random
  `RCONPassword` (D5); always pass `-Drconlo=1` so RCON listens on loopback
  only.
- **Detached process**: new `IProcessRunner.StartDetached` — std handles on
  the null device (the server only polls stdin with `ready()`, harmless),
  console from `server-console.txt`. Linux: `sh` + `setsid`; systemd unit uses
  `KillMode=process` (docs). Windows: `CreateProcessW` with
  `DETACHED_PROCESS`, no job object.
- **PID file** `<StateDir>/server/running.json`: pid, process start time,
  server name, RCON port + password captured at start. Re-attach = PID alive +
  name `java` + start time matches; otherwise the file is stale and removed.
  Status polls with `TimeProvider` (spec rule: never wait on another
  program's handle).
- **RCON**: Source RCON over TCP to `127.0.0.1:<RCONPort>` (auth packet, exec,
  multi-packet responses). Internal client behind an interface so tests use a
  loopback fake server.
- **Console tail**: read `server-console.txt` from a given offset; detect
  truncation (every start, and mid-run at the size limit) and restart at 0;
  first emit the last N lines.
  Polling with `TimeProvider`, not a file watcher (portable, testable).
- Errors: new `ErrorCode`s (`server.already_running`, `server.not_running`,
  `server.name_invalid`, `server.rcon_failed`, `server.stop_timeout`, ...) per
  `.trellis/spec/core/error-handling.md`.

## Core: config (`IServerConfigService`, child `server-config-core`)

- Typed `ServerSettings` DTO for the common fields (D9); exact field list in
  the child design. Unknown keys untouched.
- Ini: line-preserving model (comments, order, blank lines, unknown keys kept);
  only edited values change.
- `SandboxVars.lua`: parse the subset PZ writes (`SandboxVars = { Key = value,
  Nested = { ... }, }` with numbers, booleans, strings, comments); anything
  else is a validation error, not a crash (untrusted input rule).
- Raw text: get returns text + content hash; save takes the expected hash
  (conflict error if the file changed, e.g. PZ rewrote it on start), validates,
  writes a timestamped backup under `<StateDir>/backups/<name>/` (keep last
  10), then `AtomicFile` write.
- Saving while running is allowed (D13); the response says a restart is needed.

## Core: SteamCMD (`ISteamCmdService`, child `steamcmd-core`)

- Status: SteamCMD present / path; installed build id and branch from
  `<InstallRoot>/steamapps/appmanifest_380870.acf` (`ValveKeyValue`).
- Ensure SteamCMD: download via `LauncherHttpClient` (Linux `tar.gz` via
  `System.Formats.Tar` + `GZipStream`, keeps exec bits; Windows `zip`) into
  `<StateDir>/steamcmd`, or use a configured path.
- Install / update: `+force_install_dir <InstallRoot> +login anonymous
  +app_update 380870 [-beta unstable] [validate] +quit`; progress parsed from
  `Update state (...) downloading, progress: x (a / b)` into
  `IProgress<SteamCmdProgress>`; exit code + output mapped to errors.
- Refused while the server runs (`server.running`); an `UpdateServerRequest`
  with `RestartAfter` performs stop → update → start in one call (one user
  action = one method, `.trellis/spec/core/service-api.md`).

## PzLauncher.Server (child `server-web-host`)

- `Microsoft.NET.Sdk.Web`, `PublishAot`, `WebApplication.CreateSlimBuilder`,
  source-generated JSON (`CoreJsonContext` + a server context).
- **Data dir**: `--data-dir`, default `~/.local/share/PzLauncher.Server`
  (Linux) / `%LOCALAPPDATA%\PzLauncher.Server` (Windows). Holds
  `settings.json` (install root default `<data>/pzserver`, cache dir, SteamCMD
  path, branch, per-server memory), `auth.json`, and Core's `StateDir`.
- **Listen**: default `http://127.0.0.1:8090` (D12), override with `--urls`.
  Forwarded headers honoured only from loopback / configured proxies.
- **Auth** (D3): `auth.json` holds a PBKDF2-SHA256 hash
  (`Rfc2898DeriveBytes.Pbkdf2`, no Identity). No password yet → a one-time
  setup token is printed to the log; the UI setup page needs it, so whoever
  reaches the port first can't claim the server. CLI `set-password` resets it.
  Cookie auth: HttpOnly, SameSite=Strict, Secure when the request is HTTPS,
  sliding 7 days. Mutating requests require a custom header (CSRF). Login
  rate-limited per IP.
- **API**: `/api/v1/...`, one endpoint per Core method; failures are RFC 7807
  with `code` = error wire name. Console and job progress over SSE
  (`TypedResults.ServerSentEvents`).
- **Jobs**: SteamCMD install/update runs as a host background job (one at a
  time) so closing the tab doesn't abort a multi-GB download; SSE streams its
  progress; an explicit cancel endpoint aborts it.
- **SPA**: static files from `wwwroot` + fallback to `index.html`.
- **Packaging**: `release.yml` builds the web app (Node + pnpm), publishes
  `PzLauncher.Server` AOT for both RIDs, packages
  `PzLauncher.Server-<ver>-<rid>` under one `PzLauncher.Server` folder. The
  desktop updater matches its own package name exactly
  (`PzLauncher.Core/Update/Releases/LauncherReleases.cs` `PackageName`), so extra assets
  in the same release are safe; Gitee sync uploads all `dist/*`.
- **Docs** (`docs/server/`): install, systemd unit (`KillMode=process`),
  Caddy and nginx samples (SSE: buffering off), firewall (open game UDP ports,
  keep RCON and 8090 closed), `set-password`, SteamCMD 32-bit libs.

## PzLauncher.Web (child `server-web-ui`)

- Vite + React + TypeScript + Ant Design + react-i18next (zh-Hans, en; Ant
  Design locale follows). pnpm, ESLint, `tsc --noEmit`, Vitest for logic.
- Pages: Setup / Login; Servers (list, create, select); Dashboard (status,
  start / stop / force kill, console with command input); Config (form tabs
  Server / Sandbox, raw tab with CodeMirror 6, bundled locally, no CDN);
  Install & Update (status, branch, install / update / stop→update→start,
  progress, log); Settings (paths, memory, change password, language).
- Error display maps `code` to translated text; unknown codes show the
  problem `detail`.

## Risks and verification items

- R1, R3: resolved by the `server-host-core` spike
  (`../10-23-server-host-core/research/pz-server-runtime.md`): stdin on the
  null device is harmless; `-Drconlo=1` binds RCON to 127.0.0.1.
- R2: RCON, ini defaults and console behaviour verified on B42 (Windows).
  Still open: B41, and the Linux server folder layout / environment /
  `setsid` — checked manually on Linux in `server-host-core`.
- R4: Plain HTTP exposes the password if the user changes `--urls` to a public
  address without a proxy; docs and the login page warn when the request is
  not HTTPS and not loopback.
- Rollback: everything is additive (new projects, new Core folders, new
  workflow steps). Removing `PzLauncher.Server` from the solution and
  `release.yml` reverts it; the desktop app is unaffected.
