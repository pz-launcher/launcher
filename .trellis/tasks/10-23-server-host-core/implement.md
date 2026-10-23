# Implement: host named servers in Core

Design: `design.md`; facts: `research/pz-server-runtime.md`.

## Checklist (in order)

1. **Platform seam** — `IProcessRunner.StartDetached`, `GetById`, `KillTree`
   + `DetachedProcess` / `ProcessInfo` records; `ProcessRunner` (Windows
   `CreateProcessW` via `LibraryImport`, Linux `sh` + `setsid`);
   `FakeProcessRunner` support. `ProcessRunnerTests`: `GetById` of the test
   process; `StartDetached` of a short-lived child on the current OS returns
   its PID and the child gets EOF on stdin.
2. **Constants + ini helper** — `Server/PzServerFiles.cs`;
   `Server/Config/ServerIniFile` (read key, set key keeping other lines byte-for-byte).
3. **DTOs + errors** — `ServerSite`, `NamedServer`, `StartServerRequest`,
   `ServerStatus`, `ServerState`, `ServerConsoleLine`; register in
   `CoreJsonContext` + round-trip tests. `ServerErrors` + 14 `ErrorCode`s;
   `ErrorCodeTests` pins; `Error_<Member>` in all 10
   `PzLauncher.Desktop/Lang/UiText*.resx` and the `ErrorMessages` arms.
4. **RCON client** — `IRconClient` / `RconClient` (auth, exec, sentinel,
   multi-packet, timeout). Tests against a loopback fake RCON server
   (`TcpListener` on `127.0.0.1:0`): auth ok / rejected, one packet,
   multi-packet response, server closes mid-response, timeout.
5. **Console tail** — reader with backlog, partial lines, truncation →
   `FileRestarted`, UTF-8 replacement. Tests with a temp file and
   `ManualTimeProvider`.
6. **`ServerHostService`** — list / create / start / status / stop / kill /
   command / stream per `design.md`; register in
   `ServiceCollectionExtensions` (+ `ServiceCollectionExtensionsTests`).
   Tests (temp dirs, `GameInstallBuilder`-style dedicated server folder,
   `FakeProcessRunner`, fake RCON, `ManualTimeProvider`):
   - name validation, duplicate (case-insensitive), minimal ini contents;
   - start: command line (json JVM args, heap override, `-Drconlo=1` once,
     `-statistic 0`, `-servername`, `-cachedir=` only when non-default,
     `-adminpassword` only when given), admin password required without db,
     empty RCON password generated and written, not-a-server install,
     already running;
   - running file written; re-attach after a new service instance; stale file
     (PID gone / start time differs / not java) → Stopped and deleted;
   - Starting → Running on first RCON auth, throttled to 2 s;
   - early exit → `exited_early` with console tail; admin prompt in tail →
     `admin_password_required`; exit after Ready → `exited_unexpectedly`;
   - stop: needs Ready, sends `quit`, waits, timeout → `stop_timeout`; kill
     checks identity;
   - command validation and RCON failure mapping.

## Validation

- Implement: `dotnet build PzLauncher.slnx -warnaserror` only.
- Check: `dotnet test PzLauncher.slnx`.
- Manual, Windows (this machine's dedicated server, scratch cache dir): a
  small throwaway console harness (not committed) calls the service: create →
  start with admin password → Running → `players` → stop; start again without
  password; kill the harness while running, new harness → status Running →
  stop. Same scenario as the spike, now through Core.
- Manual, Linux (needs a Linux machine with the dedicated server installed):
  the same harness run on Linux; also confirms the open items at the end of
  `research/pz-server-runtime.md` (folder layout, environment, `setsid`).
  Any constant that differs is fixed in `PzServerFiles.cs` /
  `LinuxLaunchEnvironment` before the child is archived.

## Risky points / rollback

- `ProcessRunner` changes touch the game launch's seam: only additive
  methods; existing `Start` / `StartPiped` untouched.
- New error codes force resx edits in 10 files: keep texts short; review the
  zh-Hans / zh-Hant wording against `.trellis/spec/desktop/localization.md`
  game terminology.
- Everything else is new files under `Server/`; reverting the child removes
  them and the DI line.
