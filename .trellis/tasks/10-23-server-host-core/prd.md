# Host named servers in Core

Parent: `10-23-web-server-management` (decisions D1–D13, shared design in its
`design.md` → "Core: hosting"). First child; no dependencies.

## Goal

Core can list and create named servers for one dedicated server install, start
one as a process that outlives its host, find it again after the host
restarts, send it commands, stream its console, and stop it with the world
saved.

## Requirements

- R1 List named servers of a `ServerSite` (from the cache dir's `Server/*.ini`);
  create one with a validated name (minimal ini incl. `RCONPort` and a random
  `RCONPassword`).
- R2 Start a named server detached from the host process: java command from the
  install's `ProjectZomboid64.json` + memory override, `-servername`,
  `-cachedir=` when non-default, `-Drconlo=1`, `-adminpassword` when given
  (required while the server has no database; resets the in-game admin
  password otherwise).
  Refuse while any server runs (D4). Ensure RCON keys exist before start.
- R3 Status: stopped / starting / running / stopping, with server name, PID,
  start time. Survives a host restart via the PID file; stale files are
  discarded.
- R4 Send a console command over RCON and return the response text.
- R5 Stream the console as lines from `server-console.txt` (last N lines first,
  then new lines; handles truncation on a new start).
- R6 Graceful stop via RCON `quit`, waiting for exit with a timeout; separate
  kill.
- R7 Core-only, `Result`-based, DTOs in `CoreJsonContext`, testable through
  `IProcessRunner` / `TimeProvider` / RCON fakes; no ASP.NET Core.

## Acceptance criteria

- [ ] Unit tests cover: list/create (name validation, ini contents), start
      command line incl. first-run admin password, already-running refusal,
      PID-file re-attach and stale-file cleanup, RCON auth + command +
      multi-packet response against a loopback fake, console tail incl.
      truncation, stop timeout → `server.stop_timeout`.
- [ ] Manual on Linux: start a server through a test harness, kill the
      harness, start it again → status running, command works, `quit` stops it
      and the world is saved.
- [x] Verification spike findings (parent R1/R2) recorded in
      `research/pz-server-runtime.md` (Windows, B42; Linux items open).
