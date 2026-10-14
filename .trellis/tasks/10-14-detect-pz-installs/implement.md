# Implementation plan

Each step ends with a passing build. Commit points are suggestions.

## 1. Solution skeleton
- [x] Create `PzLauncher.Core` (classlib, net10.0, Nullable, `IsAotCompatible`, `TreatWarningsAsErrors`).
- [x] Create `PzLauncher.Core.Tests` and `PzLauncher.Desktop.Tests` (xUnit v3, VSTest mode; see test-strategy.md). Note: uses `xunit.v3.mtp-off` because `xunit.v3` 4.x pulls in Microsoft.Testing.Platform, which the .NET 10 SDK rejects in VSTest mode.
- [x] Add all three to `PzLauncher.slnx`. App references Core. Tests reference their targets.
- [x] Remove the empty `Models\` folder include from the app csproj.
- [x] Gate: `dotnet build PzLauncher.slnx -warnaserror` and `dotnet test PzLauncher.slnx` (0 tests ok).

## 2. Core primitives
- [x] `Common/`: `ErrorKind`, `Error`, `Result`, `Result<T>` (+ tests for Ok/Fail/implicit conversions).
- [x] `Platform/`: `IPlatformEnvironment`, real `PlatformEnvironment` (registry read guarded by `OperatingSystem.IsWindows()`).
- [x] Test helpers in Core.Tests: `TempDirectory`, `FakePlatformEnvironment`, `SteamLayoutBuilder` (writes a fake Steam root/libraries/manifests).

## 3. Steam reading
- [x] Research: list root files of the real PZ game + server installs (read-only) → `research/install-markers.md`. Also copy (redacted of user names if any) a real `libraryfolders.vdf` and `appmanifest_108600.acf` into `Fixtures/Steam/`.
- [x] VDF parsing + tests (fixtures + malformed cases). First a hand-written `VdfParser`/`VdfNode`; replaced by the `SteamKeyValues` wrapper over the ValveKeyValue package (see `research/valvekeyvalue.md`).
- [x] `SteamLocator` + tests per OS via fake environment.
- [x] `SteamLibraryReader` (libraries from vdf, manifests) + tests.

## 4. Install detection
- [x] DTOs, `InstallErrors`, `PzInstallMarkers`, `PzSteamApps`.
- [x] `InstallDetectionService.ScanAsync` / `InspectAsync`.
- [x] `CoreJsonContext` with the DTOs + a round-trip test.
- [x] `AddPzLauncherCore()`.
- [x] Tests: every scenario listed in prd.md acceptance criteria + cancellation.
- [x] Gate: build + test. _Commit point._

## 5. Desktop UI
- [x] `IFolderPicker` + `AvaloniaFolderPicker`.
- [x] `ErrorMessages` (all codes in design.md) + test that every code has a non-fallback message.
- [x] `InstallsViewModel` + tests with fake service and fake picker.
- [x] `InstallsView.axaml` (x:DataType, Design.DataContext), DataTemplate in `App.axaml`.
- [x] `MainViewModel.CurrentPage`, `MainWindow` hosts it via `ContentControl`. Remove the Greeting sample.
- [x] DI composition root + global exception handler in `App.axaml.cs`.
- [x] Gate: build + test. Manual smoke is the next item (developer runs it).
- [x] Manual smoke: run the app and check the prd.md smoke steps.

## 6. Review
- [x] `trellis-check` against specs (no Avalonia in Core or ViewModels, no Message assertions, no blanket catch).
- [x] Update specs if implementation found something the spec got wrong (step 3.3).

## Validation commands
```bash
dotnet build PzLauncher.slnx -warnaserror
dotnet test PzLauncher.slnx
dotnet run --project PzLauncher.Desktop
```
