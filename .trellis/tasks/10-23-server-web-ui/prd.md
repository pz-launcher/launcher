# Web management UI

Parent: `10-23-web-server-management` (D7, D8, D9, D10, D13; design →
"PzLauncher.Web"). Depends on `10-23-server-web-host`'s API.

## Goal

A browser UI in Simplified Chinese and English that covers every server
management action of the first version.

## Requirements

- R1 `PzLauncher.Web`: Vite + React + TypeScript + Ant Design +
  react-i18next; pnpm; ESLint, typecheck, Vitest; build output into
  `PzLauncher.Server/wwwroot`.
- R2 Setup (token + new password) and login pages; warning when the page is
  neither HTTPS nor loopback.
- R3 Servers: list, create, select current.
- R4 Dashboard: status, start / stop / force kill (offered after a stop
  timeout), live console (SSE, auto-scroll, reconnect) with command input and
  inline responses.
- R5 Config: typed form (Server / Sandbox tabs) + raw tab (CodeMirror 6,
  bundled locally) with conflict and validation messages; "takes effect on next
  start" notice while running.
- R6 Install & Update: SteamCMD and install status, branch, install / update /
  stop→update→start, progress, SteamCMD output, cancel.
- R7 Settings: paths, per-server memory, change password, language (zh-Hans /
  en; Ant Design locale follows).
- R8 Errors: `code` → translated text, falling back to `detail`.
- R9 Creates `.trellis/spec/web/`.

## Acceptance criteria

- [ ] `pnpm lint`, `pnpm typecheck`, `pnpm test`, `pnpm build` pass.
- [ ] Against a running `PzLauncher.Server`, every requirement above is
      exercised manually in both languages; no untranslated keys show.
- [ ] Usable at phone width (dashboard and console).
