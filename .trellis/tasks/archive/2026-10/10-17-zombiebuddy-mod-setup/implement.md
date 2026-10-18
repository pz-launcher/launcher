# Implement: Unify ZombieBuddy Java mod launch setup

Branch: `zb-skip-online-checks` (PR #10). Behaviour must not change; every
existing scenario keeps its expectation.

## Checklist

### Core

1. `Mod/ZombieBuddy/ZombieBuddyLaunch` (internal): `ResolveAsync`, `Cache`,
   lazy `LibrariesAsync`, `FindPreloadableAsync`, `MarkPreloaded` /
   `IsPreloaded` + `ZombieBuddyLaunchTests` (unsupported status, status error,
   bad root → null; `-cachedir` and default cache; unresolvable cache → `Cache`
   null; libraries read once; find: not found / not preloadable / entry).
2. `IZombieBuddyLaunchStep`; `ZabricService` → `ZabricLaunchStep`,
   `ZBSkipOnlineChecksService` → `ZBSkipOnlineChecksLaunchStep` (git mv to keep
   history); drop their gates and the duplicated status/cache/find code; delete
   `IZabricService`, `IZBSkipOnlineChecksService`.
3. `IZombieBuddyModsService` + `ZombieBuddyModsService` (one gate, steps in
   registration order, errors collected in order); DI: register the service and
   both steps, Zabric first, with the ordering comment.
4. Tests: rename/adapt `ZabricServiceTests` → `ZabricLaunchStepTests` and
   `ZBSkipOnlineChecksServiceTests` → `ZBSkipOnlineChecksLaunchStepTests`
   (same scenarios; `zabricPreloaded: false` = no `MarkPreloaded`; Zabric step
   marks on `Current`/`Written` only); `ZombieBuddyModsServiceTests` per design
   (order, shared launch, both errors in order, resolve-null runs nothing, the
   real-files AC3 case).
5. Spec `.trellis/spec/core/directory-structure.md`: the `Mod/ZombieBuddy/`
   line mentions the launch steps and the entry point; the Zabric and
   ZBSkipOnlineChecks lines say "launch step".

### Desktop

6. `HomeViewModel` takes `IZombieBuddyModsService`; loop over the errors
   inside `if (!removeZombieBuddy)`.
7. `FakeZombieBuddyModsService` replaces `FakeZabricService` and
   `FakeZBSkipOnlineChecksService`; `DesignData` one sample; `MainViewModelTests`.
8. `HomeViewModelTests`: replace the Zabric/ZBSOC cases with: called once with
   root + `ExtraGameArgs` after the ZombieBuddy gate; two errors → two dialogs in
   order, then launch; no call when the collection leaves ZombieBuddy out or
   setup is cancelled; the existing "dialog shown before launch" ordering test
   kept.

## Validation

```
dotnet build PzLauncher.slnx -warnaserror
dotnet test PzLauncher.slnx
```

`git grep -n "IZabricService\|IZBSkipOnlineChecksService\|zabricPreloaded"` →
no hits outside the archived task folders.

## Risky points

- Losing a scenario while moving tests: compare the old and new test name
  lists in the check step.
- Step order is registration order: pinned by a test.
