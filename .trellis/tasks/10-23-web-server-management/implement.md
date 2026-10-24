# Implement: web server management (parent)

The parent has no direct code. Each child is planned (own `design.md` +
`implement.md` refining `../10-23-web-server-management/design.md`), started,
checked and archived on its own, in this order:

1. `10-23-server-host-core` — first. Its design starts with a verification
   spike on a real Linux dedicated server for R1/R2 (stdin at EOF, RCON,
   minimal ini, console file, folder layout); findings go to its
   `research/`.
2. `10-23-server-config-core` — may run in parallel with 1 (touches only new
   `Server/Config` files and `PzServerFiles.cs`, which 1 creates; whoever lands
   second rebases).
3. `10-23-steamcmd-core` — after 1 (needs status + stop/start for the
   stopped-only rule and stop → update → start).
4. `10-23-server-web-host` — after 1–3 (maps all three services). Creates
   `.trellis/spec/server/`.
5. `10-23-server-web-ui` — after 4's API contract is merged. Creates
   `.trellis/spec/web/`.

`10-15-host-dedicated-server/prd.md` has a note to reuse the Core hosting
service (D11).

## Validation per child

- Core children: `dotnet build PzLauncher.slnx -warnaserror` (implement),
  `dotnet test PzLauncher.slnx` (check).
- Web host: the above + `dotnet publish PzLauncher.Server -c Release -r
  linux-x64 -p:PublishAot=true` with no IL2xxx/IL3xxx warnings.
- Web UI: `pnpm lint`, `pnpm typecheck`, `pnpm test`, `pnpm build`.

## Final integration (parent check, after child 5)

- Run the cross-child acceptance criteria in `prd.md` on a Linux VPS or VM
  (fresh user, systemd unit from the docs, Caddy in front), including
  `systemctl restart` with the game server running.
- Windows smoke check: unpack the package, start, log in, start/stop a server.
- Tag a prerelease and confirm `release.yml` attaches both
  `PzLauncher.Server-*` packages and the desktop updater still finds its
  package.
