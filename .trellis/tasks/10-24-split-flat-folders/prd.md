# Split oversized flat folders

## Goal

No source folder holds so many files directly that it is hard to scan. A spec
rule sets the limit, and every folder over it is split by sub-concept into
subfolders, as a pure move (no behaviour change).

## Background (2026-10-24)

- User request: some packages hold too many flat files.
- Precedent: `d5568d4` "Group Desktop Services into feature folders (#12)"
  split 41 flat files in `PzLauncher.Desktop/Services`: namespaces follow
  folders, tests mirror, pure move.
- Spec today names no size limit (`.trellis/spec/core/directory-structure.md`,
  `.trellis/spec/desktop/directory-structure.md`); test folders mirror source
  folders (`.trellis/spec/common/test-strategy.md`).
- Already done: `Core/Server` split into `Config/`, `ConsoleLog/`, `Rcon/` in
  PR #18 (`3003e80`, branch `server-host-core`).
- Work in flight touching the same folders: PR #18, and `feat/modpacks`
  (task `10-21-share-collection-preset`, 85 files incl.
  `ViewModels/Mods/Workshop`, `Services/Preferences`, new `Mods/Modpacks`
  folders).

## Decisions

- D1 (2026-10-24): limit = a folder holding **more than 12 source files
  directly** gets split. An `.axaml` + `.axaml.cs` pair counts once. Test
  projects count too, `TestSupport` included.
- D2: namespaces follow folders, test folders mirror source folders, pure
  move (as #12).
- D3 (revised 2026-10-24, user): run now on branch `server-host-core`, in
  PR #18. `feat/modpacks` resolves the conflicts when it rebases. The folder
  list in `design.md` was measured on this branch.

## Requirements

- R1 The limit rule (D1, D2) is in the spec, with naming guidance for
  subfolders, and the core / desktop directory-structure specs link to it.
- R2 Every folder over the limit at implementation time is split per
  `design.md`; neither a parent nor a new subfolder exceeds 12.
- R3 Pure move: only file locations, namespace declarations, `using`
  directives (and test-project global usings) and spec / doc path references
  change. No renamed types, no logic changes.
- R4 Spec and task docs that name a moved file point at its new path.

## Acceptance criteria

- [ ] A re-run of the counting script (in `implement.md`) lists no folder over
      12 in any project.
- [ ] `git diff -M` shows every moved file as a rename whose content change is
      only its namespace line / usings.
- [ ] `dotnet build PzLauncher.slnx -warnaserror` and `dotnet test
      PzLauncher.slnx` pass with the same test count as before the move.
- [ ] Desktop app starts and every page opens (DataTemplates still resolve
      VM → View after namespace changes).
- [ ] No spec / doc reference to an old path remains (grep in `implement.md`).

## Out of scope

- Renaming types, splitting large files, or regrouping folders under the limit.
- `Views/` folders (none is over the limit; pairs count once).
- A build-time check that enforces the limit.
