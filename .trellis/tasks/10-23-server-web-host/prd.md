# PzLauncher.Server web host

Parent: `10-23-web-server-management` (D3, D6, D12; design →
"PzLauncher.Server"). Depends on the three Core children.

## Goal

A single AOT executable that, on a headless machine, serves the web UI and an
authenticated HTTP/SSE API over the Core server services.

## Requirements

- R1 New `PzLauncher.Server` project (Minimal API, AOT, source-generated
  JSON) and `PzLauncher.Server.Tests`, both in `PzLauncher.slnx`.
- R2 Data dir + `settings.json` (install root, cache dir, SteamCMD path,
  branch, per-server memory) with get/save endpoints.
- R3 Auth: setup with a one-time token printed to the log, login / logout,
  change password, CLI `set-password`; PBKDF2 hash; cookie session; CSRF
  header; login rate limit; every other API route requires a session.
- R4 Endpoints for every Core server / config / SteamCMD operation; RFC 7807
  errors with `code`; console and job progress over SSE; SteamCMD runs as a
  cancellable background job.
- R5 Serves the SPA from `wwwroot` with `index.html` fallback.
- R6 Default listen `http://127.0.0.1:8090`, `--urls` override; forwarded
  headers only from trusted proxies.
- R7 `release.yml` packages `PzLauncher.Server-<ver>-<rid>` for linux-x64 and
  win-x64 (after building the web app); `docs/server/` covers install, systemd
  (`KillMode=process`), Caddy / nginx, firewall, SteamCMD libs.
- R8 Creates `.trellis/spec/server/` and updates
  `.trellis/spec/common/architecture.md`.

## Acceptance criteria

- [ ] Integration tests (WebApplicationFactory): unauthenticated → 401; setup
      needs the token and works once; login rate limit; CSRF header required;
      a Core failure maps to 7807 with the right status and `code`; an SSE
      endpoint streams.
- [ ] AOT publish for linux-x64 without IL2xxx/IL3xxx warnings; the binary
      starts and serves `index.html`.
- [ ] Linux check carried over from `10-23-server-host-core`: a server
      started through the host survives a host restart, takes a command and
      stops on `quit` with the world saved; the open items in
      `archive/2026-10/10-23-server-host-core/research/pz-server-runtime.md` (folder layout,
      `linux64` / `libjsig.so` environment, `setsid`) are confirmed or fixed
      in `PzServerFiles.cs` / `ServerHostService.LinuxEnvironment`.
- [ ] With the systemd unit from the docs, `systemctl restart` leaves a running
      game server alive.
