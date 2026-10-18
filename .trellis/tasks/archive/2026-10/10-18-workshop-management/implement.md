# Implement — Steam Workshop management

Build with `dotnet build` after each step (implement runs build only; tests run in check).

## 0. Spike gate (PRD D6) — stop and report before step 3 if it fails

- [x] `SteamApi` binding + `SteamHelperHost` with the `update` action and the stdout event protocol.
- [x] `Program.Main` dispatch on `--pzlauncher-steam-helper` before `SingleInstanceGuard`.
- [x] `dotnet publish PzLauncher.Desktop -r win-x64 -p:PublishAot=true`; run the helper against
      `E:\SteamLibrary\steamapps\common\ProjectZomboid\steam_api64.dll` with Steam running: init succeeds, one item
      force-updates, process exits, Steam stops showing PZ (not observable in the spike: PZ was running; re-check in
      step 3). Note overlay behaviour.
- [x] Record the outcome in `research/steam-workshop-integration.md`.

## 1. Core read side

- [x] `WorkshopSubscriptionsFile`, `WorkshopManifestFile` with fixtures under `PzLauncher.Core.Tests/Fixtures/Steam/`.
- [x] `WorkshopWebApi` (source-generated JSON, batching, timeout) with a fake `HttpMessageHandler` in tests.
- [x] `IWorkshopService` + DI registration in `ServiceCollectionExtensions`; `WorkshopErrors`.

## 2. Core write side

- [x] subscribe / unsubscribe actions in the helper.
- [x] `IWorkshopActionRunner` (process start, env, stdin request, event parsing, stall timeout, cancel, one-at-a-time).
- [x] Linux library lookup (`<gameFolder>`, `<gameFolder>/natives`); missing library → `WorkshopErrors` code.

## 3. 工坊订阅 section

- [x] `ModsSection` entries, sidebar items, `WorkshopSubscriptionsViewModel` + view (R2, R3, R4).
- [x] Progress dialog; unsubscribe confirmation naming collections; open in Steam (`steam://url/CommunityFilePage/<id>`).

## 4. 工坊合集 section

- [x] `WorkshopCollectionsViewModel` + view (R6); remembered collections preference.
- [x] 生成本地合集 with the multi-mod pick dialog (R7) via `IModCollectionService.SaveAsync`.

## 5. Pre-launch check

- [x] `WorkshopLaunchCheck` + three-choice dialog; hook into `HomeViewModel.LaunchAsync` (R5).

## 6. Finish

- [x] Spec: `core/directory-structure.md` lists `Steam/Workshop/` and `WorkshopFiles.cs` as a constants file.

- [x] All new strings in every `Lang/UiText*.resx` (R9).
- [x] Desktop tests for the new VMs and launch flow with fakes (`Fakes.*`); Core tests for files, Web API, runner
      event parsing.
- [x] Re-run the AOT publish and confirm no `steam_api*` file is in the output (AC8).

## Rollback points

After step 0 (route re-decision), after step 2 (Core complete, no UI), after step 5.
Risky files: `Program.cs` (startup path), `HomeViewModel.LaunchAsync` (launch flow).
