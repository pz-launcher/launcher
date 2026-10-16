# Implement: Launch the game client

Start only after `10-15-discover-java-runtimes` is done (uses
`IJavaDiscoveryService.InspectAsync`, `JavaRuntime`, `JavaRuntimesViewModel`).

## Checklist

0. ~~Research~~ done: `research/linux-launch.md`, folded into prd (D4, D18,
   AC1) and design (step 6, Linux environment).
1. Core errors: `ErrorCode` `launch` section (8 members), `Launch/LaunchErrors.cs`,
   rows in `PzLauncher.Core.Tests/Common/ErrorCodeTests.cs`.
2. Core platform: `IPlatformEnvironment.OsVersion` / `TotalMemoryBytes`
   (+ `PlatformEnvironment`, both test fakes); `Platform/IProcessRunner.cs`,
   `IRunningProcess`, `ProcessStartRequest` (with environment), `ProcessRunner.cs`;
   DI registration.
3. Core launch config: `Launch/PzLaunchFiles.cs`, `PzLaunchConfig` json model +
   reader, OS-version selection, `-Xmx`/`-Xms` parse/replace, `LaunchArguments.Split`.
   Unit tests with fixture copies of the real game and server json
   (`PzLauncher.Core.Tests/Fixtures/Launch/`, values anonymised where needed).
4. Core service: `IGameLaunchService`, `GameLaunchService`, DTOs; register in
   `AddPzLauncherCore` and `CoreJsonContext`; service tests with a fake
   `IProcessRunner` (`TestSupport/`), temp-dir install layouts (flat Windows,
   nested Linux `projectzomboid/`);
   JSON round-trips.
5. Desktop preferences: `installs` field, `UserPreferences.InstallOverrides`,
   `SaveInstallOverridesAsync`, store tests; `IInstallOverridesService`.
6. Desktop dialog: `IDialogService` + `AvaloniaDialogService`; move the dialog
   out of `App.axaml.cs`; DI wiring.
7. Desktop errors/text: `ErrorMessages` arms; all resx keys in 10 files.
8. Desktop navigation + Home: `ShellPage.VersionSettings`, `MainViewModel`
   mapping and events, Home tab highlight; `HomeViewModel` launch/version
   settings commands + `IsGameRunning`; `HomeView.axaml`.
9. Desktop 版本设置: `VersionSettingsViewModel` (Java dropdown from
   `JavaRuntimesViewModel`, D17 warning, max/min memory linkage), `VersionSettingsView.axaml`,
   `App.axaml` DataTemplate, `DesignData`.
10. Desktop tests: fakes in `TestSupport/Fakes.cs`; `HomeViewModelTests`,
    `VersionSettingsViewModelTests`, `MainViewModelTests` navigation.
12. GC / Graal / Viewpoint / crash (prd D19–D22, design step 7b, GC flags,
    `ValidateAsync`, `WaitForExitAsync` result): Core `GarbageCollector` +
    `GarbageCollectors`, `JavaRuntime.HasGraalCompiler` (release file),
    `IRunningProcess.ExitCode`, 3 error codes; Desktop `gc` preference field,
    GC dropdown, page validation text, Home crash dialog; D23 stderr tail +
    early-exit result, D24 extra-args GC + `launch.gc_conflict`, D22 log time
    and `-XX:ErrorFile`; tests per AC11–AC13, AC15.
11. Spec update (phase 3): `core/directory-structure.md` (`Launch/`),
    `core/service-api.md` test seams (`IProcessRunner`), `desktop/preferences.md`
    (`installs` row), `desktop/shell-and-theming.md` (sub-page navigation),
    `desktop/directory-structure.md` (dialog service) as needed.

## Validation

```bash
dotnet build PzLauncher.slnx -warnaserror
dotnet test PzLauncher.slnx
```

Then `dotnet format` on this task's `.cs` files and the manual smoke (`prd.md` AC9 on Windows; AC10 on Linux by the user).

## Risky points

- OS-version `windows` key rule is inferred from two files; keep it in one
  function with tests so it's easy to change.
- `App.axaml.cs` dialog move must keep the unexpected-error re-entry guard.
- `CanExecute` re-evaluation on `SelectedInstall` changes (arrive through
  `OnInstallsPropertyChanged`).
- Exit continuation runs off the UI thread; ViewModels set state after `await`
  on the UI context.
- Preferences: an invalid `installs` entry must not drop language/theme/window.
- Linux environment mirrors the user's copy of the B42 script; the user's AC10
  smoke confirms it works without the native launcher. Keep it in one
  function so a correction is a one-place change.
