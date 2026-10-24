# Edit server config in Core

Parent: `10-23-web-server-management` (D9, D13; design → "Core: config").
May run in parallel with `10-23-server-host-core`.

## Goal

Core reads and writes a named server's ini and `SandboxVars.lua` both as typed
common settings and as raw text, without losing anything it doesn't
understand.

## Requirements

- R1 Typed `ServerSettings` for the common fields: public name / description,
  password, max players, default + UDP ports, PVP, Public, Mods, WorkshopItems,
  Map; sandbox: zombie population, speed, loot rarity, XP multiplier (final
  list in this child's design, checked against B42 files; B41 is out of scope, parent D14).
- R2 Saving typed settings changes only those values; comments, order and
  unknown keys stay byte-identical.
- R3 Raw text get/save for both files with a content hash; save with a stale
  hash fails with a conflict error.
- R4 Validation before save (ini syntax; Lua subset PZ writes); invalid text is
  a typed error with a line number, never an exception.
- R5 Backup before every save under `StateDir/backups/<name>/`, last 10 kept;
  atomic writes.
- R6 Saving while the server runs is allowed; the result says a restart is
  needed.

## Acceptance criteria

- [ ] Round-trip tests on real B42 ini / SandboxVars fixtures: load →
      save unchanged → byte-identical.
- [ ] Tests for typed edits, conflict, validation errors with line numbers,
      backup rotation.

## Notes from `10-23-server-host-core` (2026-10-23)

- PZ regenerates the whole ini on **every** server start: unknown keys and
  custom comments are dropped, only PZ's own layout survives
  (`../10-23-server-host-core/research/pz-server-runtime.md` → Ini). R2's
  "byte-identical" applies to the manager's own saves; the UI should say that
  PZ rewrites the file on start.
- `<name>_SandboxVars.lua` exists only after the first successful start.
- Build on `server-host-core`'s `Server/Config/ServerIniFile` helper instead of a
  second ini reader.
- The ini is strict UTF-8 (B42's Java writes UTF-8; parent D14): a file that
  isn't valid UTF-8 fails with `server.file_access_failed`.
