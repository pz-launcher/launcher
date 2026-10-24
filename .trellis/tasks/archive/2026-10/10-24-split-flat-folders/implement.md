# Implement: split oversized flat folders

## Before starting

1. Work on branch `server-host-core` (D3, revised); commits go into PR #18.
2. Record the baseline: `dotnet test PzLauncher.slnx` counts.
3. Re-measure (bash, repo root); update `design.md` for any new or changed
   folder before moving:

   ```bash
   for d in $(git ls-files | grep -E '\.(cs|axaml)$' | xargs -n1 dirname | sort -u); do
     n=$(git ls-files "$d/" | grep -v "^$d/.*/" | grep -E '\.(cs|axaml)$' | sed 's/\.axaml\.cs$/.axaml/' | sort -u | wc -l)
     [ "$n" -gt 12 ] && echo "$n $d"
   done | sort -rn
   ```

## Checklist

1. Spec: "Folder size" section in `.trellis/spec/common/coding-rules.md`
   (rules from `design.md`); links + tree updates in
   `core/directory-structure.md` and `desktop/directory-structure.md`.
2. Core, one folder at a time (`Platform`, `Launch`, `Update`, `Steam`,
   `Steam/Workshop`, `Mod/Collections`, `Mod/ZombieBuddy`), each with its
   mirrored test folder; build after each.
3. Desktop (`Services/Shell`, `Services/Preferences`, `ViewModels/Mods`,
   `ViewModels/Mods/Workshop`) incl. `.axaml` `xmlns` updates; build.
4. Test support folders + `<Using>` entries in both test `.csproj`s; build.
5. Update every spec / task-doc reference to a moved path:
   `grep -rn "<old path>" .trellis --include=*.md` per moved file (archived
   tasks excluded).

## Validation

- Implement: `dotnet build PzLauncher.slnx -warnaserror` after each step.
- Check: `dotnet test PzLauncher.slnx` (same counts as the baseline);
  re-run the counting script (no output); `git diff -M main --stat` shows
  renames; spot-check that renamed files differ only in namespace / usings.
- Manual: start the desktop app and open every page.

## Rollback

Revert the branch's commits; nothing else depends on the paths.
