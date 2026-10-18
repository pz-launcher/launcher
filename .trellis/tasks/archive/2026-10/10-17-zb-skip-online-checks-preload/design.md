# Design: Set up ZB Skip Online Checks at launch

## Boundaries

- Core owns the enabled-list check, lookup, preloadability check, the
  `config.json` edit, the jar copy and the game-json agent arg edit. Desktop only
  calls it from the Home launch path and shows the error. No change to
  `GameLaunchService`: the agent arg reaches the game through the json it
  already reads.
- Feature folder `PzLauncher.Core/Mod/ZBSkipOnlineChecks/`
  (`PzLauncher.Core.Mod.ZBSkipOnlineChecks`, named after the mod id), per
  `.trellis/spec/core/directory-structure.md`: the mod's folder keeps only its
  service and facts. What any ZombieBuddy Java mod could use (preload removal,
  `patches_jar` edits, the jar copy) goes to `Mod/ZombieBuddy/`.
- The game json belongs to `ZombieBuddyService` (its gate serialises json
  edits with initialize/remove/allow-all), so the `patches_jar` part is a new
  `IZombieBuddyService` method rather than a second writer.

## Core

### Facts (`Mod/ZBSkipOnlineChecks/ZBSkipOnlineChecksFiles.cs`)

`ModId = "ZBSkipOnlineChecks"`, `ItemId = "3821335660"`,
`PackageName = "com.dont39.pz.zbskiponlinechecks"`,
`JarFile = "ZBSkipOnlineChecks.jar"` (the game-folder copy's name, as in the
README). Constants, not `mod.info` values, because removal must work with the
mod uninstalled. Add the file to the constants list in
`.trellis/spec/core/directory-structure.md`.

### API

```
public interface IZBSkipOnlineChecksService
{
    /// Sets up both start ways when the enabled list has the mod and allow-all is on, removes them when it lacks it.
    Task<Result> SyncAsync(string installRoot, string extraGameArgs, bool zabricPreloaded, CancellationToken ct = default);
}
```

- `zabricPreloaded`: Home passes `true` when the Zabric step returned
  `Current` or `Written`; only the preload part needs it (preLaunch comes from
  Zabric, and the Zabric step keeps Zabric first).
- Singleton next to `IZabricService` in `ServiceCollectionExtensions`; own gate.

### `IZombieBuddyService` additions (`ZombieBuddyService`, under `_gate`)

```
/// Copies the jar into the game folder when missing or different, and makes the agent arg's patches_jar hold one
/// "<jarFile>:<packageName>" entry for the package. Nothing is written when both already are.
Task<Result> EnsurePatchesJarAsync(string installRoot, string sourceJar, string jarFile, string packageName, CancellationToken ct = default);

/// Removes the package's patches_jar entries and deletes <game>/<jarFile>; nothing is written when neither exists.
Task<Result> RemovePatchesJarAsync(string installRoot, string jarFile, string packageName, CancellationToken ct = default);
```

- `ResolveAsync` + supported check as the other methods; json via
  `PzLaunchConfigEditor.LoadAsync`; no agent arg → `Ok` (nothing to do; the
  launch reports a missing setup through the existing gate).
- Copy: hash compare (sha256), `File.Copy(overwrite)` via the existing
  `Copy` helper → `CopyFailed`; delete → `DeleteFailed`. Copy before the json
  edit, so an entry never points at a missing jar written by us.
- `jarFile` must be a bare file name (no separators) — relative to the game
  folder because ZombieBuddy ≤ 2.3.x splits on the first `:`.

### `ZombieBuddyAgentArgs`

- `PatchesJarKey = "patches_jar"`, entry separator `;`, path/package separator
  `:`.
- `WithPatchesJar(vmArgs, os, entry, packageName)` and
  `WithoutPatchesJar(vmArgs, os, packageName)`: rewrite only the first agent
  arg's `patches_jar` option, same shape as `WithPolicy` (other options keep
  their order; unchanged input returns the same list instance so callers can
  skip the save).
  - An entry belongs to the package when the text after its last `:` (trimmed)
    equals `packageName` ordinally — covers both ZombieBuddy split rules.
  - With: first matching entry replaced by `entry`, other matching ones
    dropped; none → appended; no option → `patches_jar=<entry>` appended at the
    end. Repeated `patches_jar` options: ZombieBuddy uses the last; edit the
    last, leave the others.
  - Without: matching entries dropped; empty option dropped.

### `ZombieBuddyPreloadConfig.RemoveAsync`

```
/// Deletes this mod's preload_mods entry; writes nothing when the file or the entry is missing.
public static Task<Result<ZombieBuddyPreload>> RemoveAsync(string configFolder, string modId, CancellationToken ct)
```

- Shares the read + `Parse` + `preload_mods` type check with `EnsureAsync`
  (private helper). Missing file / `preload_mods` / entry → `NotApplicable`.
  Present → `preload.Remove(modId)` (order kept), atomic write, new enum value
  `ZombieBuddyPreload.Removed`. Errors as `EnsureAsync`.

### Flow (`ZBSkipOnlineChecksService.SyncAsync`)

1. `zombieBuddy.GetStatusAsync`; not success / not `IsSupported` / root not
   normalizable → `Ok`.
2. cache = `ModLocator.CacheFolder(...)`; null → `Ok`.
   `ActiveModsFile.ReadAsync`; error → log warning, `Ok`.
3. Not enabled:
   - preload: `ConfigFolder` present → `ZombieBuddyPreloadConfig.RemoveAsync`.
   - agent start: `zombieBuddy.RemovePatchesJarAsync(root, JarFile, PackageName)`.
4. Enabled and `IsAllowAll`: find (`ModLocator.FindAsync(ModId, ItemId, ...)`)
   and check (`ZombieBuddyJavaMod.CheckPreloadableAsync`); not found → `Ok`;
   not preloadable → that error.
   - preload: `zabricPreloaded` and `ConfigFolder` →
     `ZombieBuddyPreloadConfig.EnsureAsync(first: false)`.
   - agent start: `zombieBuddy.EnsurePatchesJarAsync(root, entry.JarPath, JarFile, PackageName)`.
5. Enabled, not allow-all → `Ok`.

The two parts in 3/4 both run; each failure is logged; the first is returned.
Written/removed states are logged at Information (config path, game json path,
jar path), errors at Warning, `LoggerMessage` as in `ZabricService`. If the
status/find/check part duplicates `ZabricService` by more than a few lines,
extract an internal helper in `Mod/ZombieBuddy/` and keep `ZabricService`'s
behaviour and tests unchanged.

## Desktop

- `HomeViewModel` takes `IZBSkipOnlineChecksService`. In `LaunchAsync`,
  inside the existing `if (!removeZombieBuddy)` block after the Zabric call:

  ```
  var zabricPreloaded = preload is { IsSuccess: true, Value: ZombieBuddyPreload.Current or ZombieBuddyPreload.Written };
  var skip = await _skipOnlineChecks.SyncAsync(install.RootPath, overrides.ExtraGameArgs, zabricPreloaded);
  if (!skip.IsSuccess) await _dialogs.ShowErrorAsync(ErrorMessages.ToUserText(skip.Error));
  ```

- Fakes: `FakeZBSkipOnlineChecksService` (configurable result, recorded calls
  with the flag); `FakeZombieBuddyService` / `DesignData.SampleZombieBuddy`
  implement the two new methods; a `DesignData` sample service;
  `MainViewModelTests` constructor call updated.

## Trade-offs

- Persistent json entry (D3): Steam launches get agent start; after the mod is
  disabled in-game (not through the launcher), a Steam launch still runs the
  skip until the next Home 启动 removes the entry. Steam updates/verification
  rewrite the json and drop the entry; the next Home 启动 adds it back.
- Removal under any policy: stale entries would otherwise keep the skip on
  (and break ZBS verification under `prompt`). A broken `config.json` then
  shows an error even for players not using the mod; ZombieBuddy reads the same
  file, so the problem is real.
- Deleting `<game>/ZBSkipOnlineChecks.jar` on removal also deletes a manual
  copy; with its entry gone nothing loads it.
- Relative path only: works with ZombieBuddy 2.3.x and 3.x; the copy can fall
  behind the mod between Home launches (the mod logs it; the next Home 启动
  re-copies).

## Rollback

One Core service, three ZombieBuddy helpers, one Home call; reverting removes
them. What was written is valid ZombieBuddy/game config equal to the README's
manual setup; removing it by hand restores the old state.
