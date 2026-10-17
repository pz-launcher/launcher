# Unify ZombieBuddy Java mod launch setup

## Goal

Home 启动 runs two ZombieBuddy Java mod setups after the ZombieBuddy gate:
Zabric's preload, then ZB Skip Online Checks' preload + agent start. Both Core
services repeat the same prerequisite and lookup code, and Home itself knows the
order between them and how Zabric's outcome feeds the second one. Move the
shared code into `Mod/ZombieBuddy/`, give Home one Core entry point, and keep
each mod's own rules in its own folder, so a third ZombieBuddy Java mod is one
more step rather than another Home change. Refactor only: what is written,
removed, logged and shown stays the same.

## Background (repository evidence)

- `PzLauncher.Core/Mod/Zabric/ZabricService.cs:19-67` and
  `PzLauncher.Core/Mod/ZBSkipOnlineChecks/ZBSkipOnlineChecksService.cs` both:
  - read `IZombieBuddyService.GetStatusAsync`, require `IsSupported`,
    normalize the install root, resolve the cache with `ModLocator.CacheFolder`;
  - find and check the mod: `WorkshopLibraries.CandidatesAsync` →
    `ModLocator.FindAsync(modId, itemId, …)` →
    `ZombieBuddyJavaMod.CheckPreloadableAsync`;
  - own a `SemaphoreSlim` gate and the same `Logged` / `LogWarning` shape.
- Differences that stay per mod: Zabric writes when installed + allow-all, first
  in `preload_mods`, never removes; ZB Skip Online Checks follows the enabled
  list (`default.txt`), goes after Zabric, removes when not enabled, edits
  `patches_jar`, and writes its preload only when Zabric's step ended
  `Current`/`Written`. Zabric's cache may be unresolved (it then searches Steam
  libraries only); ZB Skip Online Checks needs it for `default.txt`.
- Home (`PzLauncher.Desktop/ViewModels/Home/HomeViewModel.cs:189-207`) calls
  `IZabricService.EnsurePreloadAsync`, shows its error, derives
  `zabricPreloaded`, calls `IZBSkipOnlineChecksService.SyncAsync`, shows its
  error — up to two dialogs, in that order, then launches.
- Consumers of the two interfaces: Home, `DesignData` samples,
  `FakeZabricService` / `FakeZBSkipOnlineChecksService`
  (`PzLauncher.Desktop.Tests/TestSupport/Fakes.cs`), `HomeViewModelTests`,
  `MainViewModelTests`, DI (`ServiceCollectionExtensions.cs:31-32`). Core tests:
  `ZabricServiceTests` (10), `ZBSkipOnlineChecksServiceTests` (11).
- Spec: `Mod/ZombieBuddy/` holds what any ZombieBuddy Java mod needs; a mod's
  own folder keeps only its service and facts
  (`.trellis/spec/core/directory-structure.md`).

## Decisions (user, 2026-10-17)

- D1 Option B: shared helpers plus one Core entry point (not a declarative
  per-mod engine).
- D2 Done on `zb-skip-online-checks`, so it ships in PR #10
  (https://github.com/pz-launcher/launcher/pull/10).

## Requirements

- R1 One public Core entry point for Home: prepare every ZombieBuddy Java mod
  for this launch (install root, the install's extra game args) and return the
  errors to show, in step order.
- R2 Shared prerequisite resolution (status, supported, normalized root, game
  folder, cache, Steam libraries) and the find-and-check of a preloadable copy
  live once in `Mod/ZombieBuddy/`; both mods use them.
- R3 The order Zabric → ZB Skip Online Checks and "ZB Skip Online Checks'
  preload needs Zabric preloaded" live in Core, not in Home. `Mod/ZombieBuddy/`
  does not reference either mod.
- R4 Behaviour unchanged: same files written/removed under the same
  conditions, same log messages, same errors; Home still shows each error in its
  own dialog (Zabric's first), then launches; still only inside
  `if (!removeZombieBuddy)`.
- R5 Per-mod public interfaces no longer needed by Desktop are made internal or
  removed; Desktop fakes and design data shrink to the one entry point.

## Acceptance Criteria

- [ ] AC1 (R1, R5) `HomeViewModel` depends on one ZombieBuddy-mods service
      instead of `IZabricService` + `IZBSkipOnlineChecksService`; neither
      per-mod interface is referenced from `PzLauncher.Desktop`.
- [ ] AC2 (R2) Status/root/cache resolution and the find-and-check sequence
      appear once in `Mod/ZombieBuddy/`; neither mod service calls
      `GetStatusAsync`, `ModLocator.CacheFolder` or `ModLocator.FindAsync`
      directly.
- [ ] AC3 (R3) A Core test pins the step order and that ZB Skip Online Checks'
      preload is skipped when Zabric's step did not preload, while its agent
      start still runs.
- [ ] AC4 (R4) Every scenario of today's `ZabricServiceTests` and
      `ZBSkipOnlineChecksServiceTests` still passes with the same expectations
      (adapted to the new call shape); Home tests: both errors shown in order
      then launch, one error then launch, no call when the default collection
      leaves ZombieBuddy out or setup is cancelled.
- [ ] AC5 `dotnet build PzLauncher.slnx -warnaserror` and
      `dotnet test PzLauncher.slnx` pass.

## Out of Scope

- ZombieBuddy's own initialize/remove flow (`PrepareZombieBuddyAsync`, a dialog
  flow) and `ModCollectionService`'s cache resolution.
- A declarative per-mod descriptor engine; any behaviour change; new error
  codes or UI texts.
