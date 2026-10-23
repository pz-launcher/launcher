# Install and update the server with SteamCMD

Parent: `10-23-web-server-management` (D10; design → "Core: SteamCMD").
Depends on `10-23-server-host-core` (status, stop, start).

## Goal

Core can get SteamCMD, install or update the dedicated server (app 380870) on
a chosen branch with progress, and never touch a running server's files.

## Requirements

- R1 Status: SteamCMD available (path), installed build id and branch from the
  app manifest, or "not installed".
- R2 Download SteamCMD from Valve's official URL into `StateDir/steamcmd`
  (Linux tar.gz keeps exec bits; Windows zip), or use a configured path.
- R3 Install / update with branch (public / unstable) and optional validate,
  anonymous login; progress via `IProgress<SteamCmdProgress>`; cancellable.
- R4 Refused while the server runs; `RestartAfter` does stop → update → start
  in one call.
- R5 SteamCMD failures (exit code, `ERROR!` lines, missing 32-bit libs) become
  typed errors carrying SteamCMD's message.

## Acceptance criteria

- [ ] Unit tests with fake HTTP + fake process: download/extract, argument
      list per branch/validate, progress parsing from recorded output,
      running-server refusal, stop → update → start order, error mapping.
- [ ] Manual on Linux: fresh install, then update on `unstable`.
