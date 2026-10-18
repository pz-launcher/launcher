# Design: Unify ZombieBuddy Java mod launch setup

## Shape

```
Mod/ZombieBuddy/                     (knows no specific mod)
  IZombieBuddyModsService            public: PrepareLaunchAsync → errors to show
  ZombieBuddyModsService             internal: resolve once, run steps in order, one gate
  IZombieBuddyLaunchStep             internal: one mod's setup for a launch
  ZombieBuddyLaunch                  internal: resolved prerequisites + outcomes so far
Mod/Zabric/ZabricLaunchStep          internal step (was ZabricService)
Mod/ZBSkipOnlineChecks/ZBSkipOnlineChecksLaunchStep   internal step (was ZBSkipOnlineChecksService)
```

### Public API

```
public interface IZombieBuddyModsService
{
    /// Sets up every ZombieBuddy Java mod the launcher manages for this launch; the errors to show, in step order.
    Task<IReadOnlyList<Error>> PrepareLaunchAsync(string installRoot, string extraGameArgs, CancellationToken ct = default);
}
```

A list, not `Result`, because each step's failure is reported and the launch
goes on (today's two dialogs). `IZabricService` and
`IZBSkipOnlineChecksService` are deleted.

### `ZombieBuddyLaunch` (internal, built once per call)

- `ResolveAsync(IZombieBuddyService, WorkshopLibraries, IPlatformEnvironment,
  installRoot, extraGameArgs, ct)` → `null` when the status fails, is not
  `IsSupported`, or the root does not normalize (today both services then do
  nothing). Otherwise: `Status` (`IsAllowAll`, `ConfigFolder`), `Root`,
  `GameFolder`, `Cache` (nullable: Zabric still searches Steam libraries without
  it), lazily cached `LibrariesAsync()`.
- `FindPreloadableAsync(modId, itemId, logger, ct)` →
  `Result<ZombieBuddyPreloadEntry?>`: `Ok(null)` not found, error not
  preloadable, entry otherwise (wraps `ModLocator.FindAsync` +
  `ZombieBuddyJavaMod.CheckPreloadableAsync`).
- Outcomes: `MarkPreloaded(modId)` / `IsPreloaded(modId)` — a step records that
  its mod is in `preload_mods` this launch (`Current`/`Written`), a later step
  reads it. Replaces Home's `zabricPreloaded`.

### Steps

```
internal interface IZombieBuddyLaunchStep
{
    Task<Result> RunAsync(ZombieBuddyLaunch launch, CancellationToken ct);
}
```

- `ZabricLaunchStep`: today's `EnsurePreloadAsync` body minus status/cache/find
  code; needs `IsAllowAll` and `ConfigFolder`; on `Current`/`Written` →
  `launch.MarkPreloaded(ZabricFiles.ModId)`. Returns `Result` (the
  `ZombieBuddyPreload` value only fed Home's flag).
- `ZBSkipOnlineChecksLaunchStep`: today's `SyncAsync` minus the same code;
  `Cache` null → `Ok`; `zabricPreloaded` = `launch.IsPreloaded(ZabricFiles.ModId)`
  (the mod folder may know its own dependency; `Mod/ZombieBuddy/` does not).
- Steps keep their own `LoggerMessage`s (same texts) and lose their gates: the
  orchestrator serialises calls.

### Order

`ZombieBuddyModsService` takes `IEnumerable<IZombieBuddyLaunchStep>` and runs
them in registration order; `ServiceCollectionExtensions` registers Zabric then
ZB Skip Online Checks with a comment that later steps may read earlier
outcomes. Each step's error is logged by the step and collected; a thrown
`OperationCanceledException` propagates as today.

### Desktop

- `HomeViewModel` takes `IZombieBuddyModsService`; inside
  `if (!removeZombieBuddy)`: `foreach (var error in await
  _zombieBuddyMods.PrepareLaunchAsync(...)) await _dialogs.ShowErrorAsync(...)`.
- `FakeZombieBuddyModsService` (configurable errors, recorded calls) replaces
  the two fakes; `DesignData` gets one sample; `MainViewModelTests` updated.

## Tests

- `ZabricServiceTests` → `ZabricLaunchStepTests`,
  `ZBSkipOnlineChecksServiceTests` → `ZBSkipOnlineChecksLaunchStepTests`: same
  scenarios, calling the step with a `ZombieBuddyLaunch` built by
  `ResolveAsync` (helper in each fixture); the "not supported / not
  normalizable → nothing" cases move to `ZombieBuddyLaunch` tests.
  `zabricPreloaded: false` becomes "no `MarkPreloaded`".
- `ZombieBuddyModsServiceTests`: resolve-null → no step runs, empty list;
  steps run in order with one shared launch; both errors returned in order;
  one real-files test with both real steps (Zabric not installed →
  ZB Skip Online Checks' preload skipped, agent start written) for AC3.
- `HomeViewModelTests`: Zabric/ZBSOC-specific cases collapse to the fake's
  calls and errors (two errors → two dialogs in order, then launch).

## Trade-offs

- Registration order as the dependency mechanism is implicit; one test pins it
  and the registration comment names it. Explicit dependency declarations would
  be option C, not worth it for two mods.
- `ZombieBuddyPreload` stays public (it is in `CoreJsonContext`); only Core
  uses it after this.

## Rollback

Pure refactor in one commit; revert restores the two services and Home's calls.
