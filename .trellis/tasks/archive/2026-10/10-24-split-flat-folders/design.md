# Design: split oversized flat folders

Proposed split per folder, measured 2026-10-24 on `server-host-core`, where
the work runs (D3). Folders added later follow the same rules.

## Rules (go into the spec)

- A folder holds at most 12 source files directly; an `.axaml` + `.axaml.cs`
  pair counts once; test projects included.
- Past that, split by sub-concept: the feature's service, interface, errors,
  constants and main DTOs stay at the top; a self-contained part (a file
  format, a protocol client, a flow with its rows, a group of saved records)
  moves into a subfolder. Not by kind (`Models/`, `Interfaces/`, stays
  forbidden).
- Namespace = folder; test folders mirror source folders.
- Subfolder names don't shadow types the code uses unqualified (`Console`,
  `Window`, `Process`, `Launch` inside another feature): pick `ConsoleLog`,
  `Lifetime`, `Processes`, ….
- Partial-class files stay together (same namespace).

Placement: one new section in `.trellis/spec/common/coding-rules.md`
("Folder size"); `core/directory-structure.md` and
`desktop/directory-structure.md` link to it and update their trees / paths.

## Per-folder plan

Counts are files left at the top / in each new subfolder.

| Folder (now) | New subfolders | Left at top |
|---|---|---|
| `Desktop/Services/Shell` (21) | `Dialogs/` (8): `IDialogService`, `AvaloniaDialogService`, `ChoicePrompt`, `ConfirmPrompt`, `TextPrompt`, `IFolderPicker`, `AvaloniaFolderPicker`, `FolderPickResult` · `Lifetime/` (5): `IAppLifetime`, `SingleInstanceGuard`, `ILauncherWindow`, `IWindowPlacementService`, `WindowPlacement` | 8: clipboard, theme, URL launcher (interface + Avalonia impl each), `ContextProgress`, `ProgressPercent` |
| `Core/Mod/ZombieBuddy` (20) | `Preload/` (4): `ZombieBuddyPreload`, `ZombieBuddyPreloadConfig`, `ZombieBuddyPreloadEntry`, `ZombieBuddyJavaMod` · `LaunchSteps/` (4): `IZombieBuddyLaunchStep`, `ZombieBuddyLaunch`, `IZombieBuddyModsService`, `ZombieBuddyModsService` | 12: service, interface, errors, files, agent args, action, channel, item, mod, progress, status, step |
| `Core.Tests/TestSupport` (20) | `Fakes/` (8): `FakeHttpHandler`, `FakePlatformEnvironment`, `FakeProcessRunner`, `FakeRconClient`, `FakeRconServer`, `DeniedListing`, `ManualTimeProvider`, `SteppingTimeProvider` · `Builders/` (5): `GameInstallBuilder`, `JavaHomeBuilder`, `ModBuilder`, `SteamLayoutBuilder`, `ZabricBuilder` | 7: `Fixtures`, `Links`, `ListProgress`, `ResultAssert`, `TempDirectory`, `ServerHostTestKit`, `UpdateTestKit` |
| `Core/Mod/Collections` (18) | `Files/` (6): `ActiveModsFile`, `GameTextFile`, `ModCollectionFiles`, `ModCollectionOrderDocument`, `ModCollectionOrderFile`, `ModListSettingsFile` | 12: service, interface, errors, DTOs, `ModCollectionName`, `ModLoadOrder`, share text + `SharedModCollection` |
| `Core/Steam` (17) | `LaunchHook/` (7): `ISteamLaunchHookService`, `SteamLaunchHookService`, `SteamLaunchHookApplied`, `SteamLaunchHookState`, `SteamLaunchHookStatus`, `SteamLaunchOptions`, `LocalConfigEditor` | 10: locator, library reader, KeyValues, app manifest, login users, account, client files, app ids, errors, `WorkshopLibraries` |
| `Desktop/Services/Preferences` (16) | `Records/` (10): the `*Preferences`, `Saved*` records and `InstallKeyedMap` · `Secrets/` (3): `ISecretProtector`, `PlainSecretProtector`, `WindowsDpapiSecretProtector` | 3: `IPreferenceStore`, `JsonPreferenceStore`, `DesktopJsonContext` |
| `Core/Update` (16) | `Releases/` (4): `LauncherReleases`, `ReleaseFeed`, `ReleaseInfo`, `UpdateSource` | 12: service (3 partial files), interface, errors, paths, `UpdateJsonContext`, `SemVersion`, `UpdateCheck`, `StagedUpdate`, `DownloadProgress`, `LauncherArguments` |
| `Core/Launch` (16) | `Config/` (4): `PzLaunchConfig`, `PzLaunchConfigEditor`, `PzLaunchConfigFile`, `PzLaunchConfigJson` | 12 |
| `Desktop/ViewModels/Mods` (15) | `Installed/` (4): `InstalledModRow`, `InstalledModsFilter`, `InstalledModsSortColumn`, `InstalledModsViewModel` · `Collections/` (7): the `ModCollection*` VMs, rows, applier, prompts, target | 4: `ModsViewModel`, `ModsSection`, `RowSelection`, `SortedRows` |
| `Desktop.Tests/TestSupport` (15) | `Fakes/` (12): the `Fakes.*.cs` files | 3: `TempDirectory`, `TestErrors`, `UiTextCulture` |
| `Core/Steam/Workshop` (14) | `Ids/` (3): `WorkshopIdKind`, `WorkshopIds`, `WorkshopLinks` | 11 |
| `Desktop/ViewModels/Mods/Workshop` (13) | `Actions/` (3): `WorkshopActionFlow`, `WorkshopActionItemRow`, `WorkshopActionViewModel` | 10 |
| `Core/Platform` (13) | `Processes/` (8): `IProcessRunner`, `ProcessRunner`, `IRunningProcess`, `IPipedProcess`, `ProcessStartRequest`, `DetachedProcess`, `ProcessInfo`, `WindowsDetachedStart` | 5: platform environment (interface + impl), `PlatformKind`, `HostRuntime`, `RegistryHiveKind` |

Notes:

- Public types change namespace (e.g. `UpdateSource`, `ReleaseInfo`,
  `IFolderPicker`). Only in-repo callers exist; DTO JSON is unaffected
  (source-generated contexts serialise by property names, not type names).
- `ModCollectionFiles.cs` and `LauncherReleases.cs` are listed as
  domain-constant homes in `core/directory-structure.md`; their paths change.
- `Collections` under `ViewModels/Mods` doesn't clash: `System.Collections`
  is always written fully qualified in `using` directives.
- Test support namespaces: add `<Using Include="…TestSupport.Fakes" />` /
  `Builders` to the test `.csproj`s (they already use `<Using Include="Xunit" />`)
  instead of touching every test file.
- Avalonia: VM → View mapping is by explicit `DataTemplate`s in
  `App.axaml` / views with `x:DataType`; moved VMs need their `xmlns`
  prefixes updated in `.axaml` (compiled bindings fail the build otherwise).

## Mechanics

`git mv`, then fix namespaces, then let the build list missing usings; add
each `using` at its ordinal position in the existing block without reordering
other lines (lesson from `3003e80`). Tests move with their source. One commit
per project or folder group keeps review readable.

## Rollback

Pure move: revert the commits.
