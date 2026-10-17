# Implement: Set up ZB Skip Online Checks at launch

## Checklist

### Core — ZombieBuddy helpers

1. `ZombieBuddyPreload.Removed` (+ doc tweak on `NotApplicable`).
2. `ZombieBuddyPreloadConfig.RemoveAsync` (shared read helper with
   `EnsureAsync`) + tests: missing file / no `preload_mods` / no entry →
   `NotApplicable`, nothing created or rewritten; entry removed, `Zabric` and
   other entries keep order, `trusted_authors` and unknown fields survive;
   `[]` / bad json / `preload_mods: 1` → `PreloadConfigInvalid`, file
   unchanged; locked file → `PreloadConfigIoFailed`.
3. `ZombieBuddyAgentArgs.WithPatchesJar` / `WithoutPatchesJar` + tests: no
   option → appended last; existing other entry → `;`-appended; own entry with
   another path (relative or `E:\...`) → replaced in place; duplicates of own
   package → one left; unchanged → same instance; remove leaves other entries
   and options in order, drops an empty option; repeated `patches_jar` options
   → last edited; no agent arg → unchanged.
4. `IZombieBuddyService.EnsurePatchesJarAsync` / `RemovePatchesJarAsync` in
   `ZombieBuddyService` (under `_gate`) + tests with a temp game folder: copy
   when missing or different, no copy when identical; json saved only when the
   arg changed; no agent arg → `Ok`, nothing written; remove deletes the jar
   and the entry; locked jar → `CopyFailed` / `DeleteFailed` and json
   untouched; unsupported install → its existing error.

### Core — the mod

5. `Mod/ZBSkipOnlineChecks/`: `ZBSkipOnlineChecksFiles`,
   `IZBSkipOnlineChecksService`, `ZBSkipOnlineChecksService`; DI. Extract a
   shared helper with `ZabricService` only if the duplicate is real.
6. `PzLauncher.Core.Tests/Mod/ZBSkipOnlineChecks/ZBSkipOnlineChecksServiceTests`
   (fixtures as in `ZabricServiceTests`, real `ZombieBuddyService` or a fake
   recording the two calls): enabled + allow-all + Zabric preloaded → preload
   written after `Zabric` and agent start ensured with the found jar; second run
   writes nothing; `zabricPreloaded: false` → agent start only; enabled but
   not allow-all / not installed → nothing; not enabled → both removed (also
   under `prompt`); `-cachedir` honoured for `default.txt` and lookup;
   malformed `default.txt` → nothing; non-preloadable copy → its error; one
   part failing still runs the other.
7. Spec: constants list in `.trellis/spec/core/directory-structure.md`.

### Desktop

8. `HomeViewModel` dependency + call (design "Desktop"); fakes for both
   services, `DesignData`, `MainViewModelTests` constructor.
9. `HomeViewModelTests`: called after Zabric with the install root, the
   overrides' `ExtraGameArgs` and `zabricPreloaded` true for `Written`/`Current`,
   false for `NotApplicable`/error; error → error dialog then launch; default
   collection without ZombieBuddy → not called; setup cancelled → not called.

## Validation

```
dotnet build PzLauncher.slnx -warnaserror
dotnet test PzLauncher.slnx
```

Manual (Windows, this machine; back up `~/.zombie_buddy/config.json`,
`ProjectZomboid64.json` and `ZBSkipOnlineChecks.jar` in the game folder first):

1. Mod enabled → 启动 from Home: preload entry follows `Zabric`; json and jar
   unchanged (already the manual setup); the game log's ZBSkipOnlineChecks line
   names agent start.
2. Apply a collection without it, 启动 → preload entry, `patches_jar` and the
   game-folder jar are gone; `policy=allow-all` stays.
3. Re-enable, 启动 → all three are back, no restart prompt from Zabric.

Restore the backups afterwards if anything differs.

## Risky points

- Two files the launcher does not own (`config.json`, `ProjectZomboid64.json`):
  tree/arg edits only, atomic writes, round-trip tests.
- The step runs on every ZombieBuddy launch: it must stay silent unless
  something is actually wrong, and write nothing when already in step.
- `patches_jar` parsing differs between ZombieBuddy versions: keep the entry a
  bare relative file name.
