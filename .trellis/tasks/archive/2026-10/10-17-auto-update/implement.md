# Implement: launcher auto-update

## Checklist

1. Core basics
   - `Update/SemVersion.cs` (+ tests: parse, precedence, prerelease, `+meta`).
   - `ErrorCode` members + `UpdateErrors` + `ErrorCodeTests` rows; `Error_*` keys in every `UiText*.resx` + `ErrorMessages` arms.
2. Feeds
   - `UpdateJsonContext`, `GitHubReleaseFeed`, `GiteeReleaseFeed`; fixtures from the real v0.2.0 responses (`PzLauncher.Core.Tests/Fixtures/Update/`).
   - Tests: asset choice per RID, Gitee source archives ignored, prerelease rejected, missing asset → null, malformed JSON → `release_info_invalid`, HTTP failure → `network_failed`.
3. `UpdateService.CheckAsync` race + fake `HttpMessageHandler` in TestSupport (AC2, AC3, AC1).
4. Download/verify/extract/staging, `GetStagedAsync` (AC5; zip and tar.gz built in-test; zip-slip entry rejected; cancellation test).
5. Install/rollback/cleanup over a temp install folder (AC6; injected failure via a locked/readonly target or an `internal` move seam).
6. DI: register in `AddPzLauncherCore`; host passes current version.
7. Desktop preferences (`updateSource`, `autoCheckUpdates`) + `JsonPreferenceStoreTests` (AC9) + spec table update.
8. `IUrlLauncher` service, `UpdateCoordinator`, banner VM + `MainView` banner, Settings `Updates` section (VM tests for AC7, AC8 with fakes).
9. `App.axaml.cs`: startup kickoff, close-time install in the flush delegate, restart path. `PzLauncher.Desktop.csproj`: `<Version>0.0.0-dev</Version>`.
10. Resx text for all languages (R9); spec updates (`core/` update section, `desktop/preferences.md`).

## Validation

```bash
dotnet build PzLauncher.slnx -c Release
dotnet test PzLauncher.slnx -c Release
dotnet publish PzLauncher.Desktop/PzLauncher.Desktop.csproj -c Release -r win-x64 -p:PublishAot=true -p:Version=0.1.0 -o <scratch>/old
```

Then AC11 manual smoke with the published `0.1.0` build against the real v0.2.0 release (GitHub and Gitee).

## Risky points

- AOT: JSON must be source-generated; `TarFile`/`ZipFile` are AOT-safe; publish once with `PublishAot=true` to catch trimming warnings.
- Windows: moving loaded native DLLs (`libSkiaSharp.dll`, `av_libglesv2.dll`, `libHarfBuzzSharp.dll`) — verify in the smoke test.
- Close path timeout (2 s in `WindowPlacementPersistence`): install must fit; it is renames only.
- Tests must stay offline (fake handlers only).

## Before `task.py start`

- `implement.jsonl` / `check.jsonl` curated (done in planning).
- User approval of the final planning summary.
