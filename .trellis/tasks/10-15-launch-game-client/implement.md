# Implement: Launch the game client

Start only after `10-15-discover-java-runtimes` is done (uses
`IJavaDiscoveryService.InspectAsync`, `JavaRuntime`, `JavaRuntimesViewModel`).

## Checklist

0. Research (persist to `research/linux-launch.md`): `projectzomboid.sh` and
   the Linux `ProjectZomboid64.json` from a real Linux install (ask the user
   for copies) or reliable public copies; confirm `LD_LIBRARY_PATH` /
   `LD_PRELOAD` entries and whether the Linux json has an OS-version map.
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
   `IProcessRunner` (`TestSupport/`), temp-dir install layout; JSON round-trips.
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

## Before `task.py start`: re-review needed

`research/linux-launch.md` (written during the Java child) changes Linux
details in this plan: the Linux install root is one level deeper
(`<library>/ProjectZomboid/projectzomboid/`), the B42 client script also
preloads `libPZXInitThreads64.so` and clears `XMODIFIERS`, `libjsig.so` is in
`jre64/lib` (not `lib/amd64`), the stock Linux json has no OS-version map.
Fold these into prd/design and show the user the delta before starting.
