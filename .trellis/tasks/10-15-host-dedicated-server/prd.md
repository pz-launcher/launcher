# Host the dedicated server

Parent: `10-15-launch-installs` (facts and decisions D1–D3 live there).

Depends on `10-15-launch-game-client`: reuses its Core launch feature and Home
button wiring; start after that child is done.

## Goal

With a DedicatedServer install selected, 启动 starts the server as a process
hosted by the launcher, with live output, command input, running status and a
graceful stop.

## Requirements

_To be filled after the game-client child is planned._

## Acceptance Criteria

_To be filled._

## Open Questions

- Where the server console lives in the UI (Home content area, a new page, ...).
- Confirm the server follows the game's mechanism (java built from the server's `ProjectZomboid64.json` + per-install overrides), rather than `StartServer64.bat` (trailing `PAUSE`, hard-coded `-Xms16g`).
- Server name / `-servername`, custom args, admin password handling.
- What happens to a running server when the launcher closes.

## Note (2026-10-23)

`10-23-web-server-management` (D5, D11) builds server hosting in Core:
detached process, RCON commands, console tail of `server-console.txt`,
re-attach via PID file. When this task is planned, reuse that Core service
instead of the child-process + stdin model in parent D2.
