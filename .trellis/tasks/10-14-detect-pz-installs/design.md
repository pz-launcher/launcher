# Design: Detect Project Zomboid installs

Follows `spec/core/service-api.md`, `spec/core/error-handling.md`,
`spec/desktop/directory-structure.md`, `spec/common/test-strategy.md`.

## Components

```
PzLauncher.Core
  Common/      Result, Result<T>, Error, ErrorKind
  Platform/    IPlatformEnvironment, PlatformEnvironment (real), PlatformKind
  Steam/       SteamKeyValues (ValveKeyValue wrapper), SteamLocator, SteamLibraryReader, PzSteamApps, SteamErrors
  Installs/    IInstallDetectionService, InstallDetectionService, PzInstall,
               PzInstallKind, PzInstallSource, InstallScanResult, InstallErrors, PzInstallMarkers,
               PzInstallList (path-keyed merge/contains, shared by hosts)
  Serialization/CoreJsonContext
  ServiceCollectionExtensions.AddPzLauncherCore()

PzLauncher.Desktop (app)
  App.axaml.cs           DI composition root, global exception handler
  App.axaml              DataTemplate InstallsViewModel → InstallsView
  ViewModels/MainViewModel        CurrentPage = InstallsViewModel
  ViewModels/Installs/InstallsViewModel
  Views/Installs/InstallsView.axaml
  Services/IFolderPicker + AvaloniaFolderPicker (StorageProvider)
  Localization/ErrorMessages
```

## Platform seam

```csharp
public interface IPlatformEnvironment
{
    PlatformKind Os { get; }                 // Windows | MacOS | Linux
    string HomeDirectory { get; }
    string? GetEnvironmentVariable(string name);
    string? ReadSteamPathFromRegistry();     // null on non-Windows
    StringComparison PathComparison { get; } // OrdinalIgnoreCase on Windows
}
```

`SteamLocator` turns this into ordered, de-duplicated candidate Steam roots
and keeps the ones that contain `steamapps/`:

| OS | Candidates (in order) |
|---|---|
| Windows | registry `HKCU\Software\Valve\Steam\SteamPath`; `%ProgramFiles(x86)%\Steam` |
| macOS | `~/Library/Application Support/Steam` |
| Linux | `~/.steam/steam`, `~/.local/share/Steam`, `~/.var/app/com.valvesoftware.Steam/.local/share/Steam` |

Since this logic is pure, tests run it on every OS with a fake environment.

## Scan algorithm

1. Find Steam roots. If there are none: `Ok(installs: [], warnings: [steam.not_installed])`.
2. For each root, read `steamapps/libraryfolders.vdf` → library paths (each
   `"<n>" { "path" "..." }`). Always include the root itself. If the file is
   unreadable or malformed: add a `steam.library_unreadable` warning
   (details: `path`) and continue with the root alone.
3. De-duplicate library paths (normalize with `Path.GetFullPath`, trim the
   trailing separator, compare with `PathComparison`).
4. For each library and each app id in {108600, 380870}: if
   `steamapps/appmanifest_<id>.acf` exists, parse `AppState`:
   `installdir`, `buildid`, branch = `UserConfig.BetaKey` ??
   `MountedConfig.BetaKey`, where `"public"`/empty → null. Install root =
   `<lib>/steamapps/common/<installdir>`. If the folder is missing, add an
   `install.files_missing` warning. If the manifest is unreadable or
   malformed (incl. missing/blank `installdir` or `buildid`), add a
   `steam.manifest_unreadable` warning.
5. Order the result: Game first, then DedicatedServer, then by path.
6. Honour `ct` between files (`ct.ThrowIfCancellationRequested()`).

Read files with `File.ReadAllTextAsync(path, ct)`, not sync reads wrapped in
`Task.Run`. The files are tiny and this keeps the UI thread free.

## VDF parsing (ValveKeyValue)

Parsing uses the NuGet package **ValveKeyValue** (SteamDB, MIT, AOT-compatible,
Core only) behind a small internal wrapper, `Steam/SteamKeyValues`:

- `SteamKeyValues.Parse(string text) → Result<KVObject>`. Uses
  `KVSerializer.Create(KVSerializationFormat.KeyValues1Text)` with
  `new KVSerializerOptions { HasEscapeSequences = true }` (a fresh options
  object per call). The library's default is escapes **off**, which would
  leave `\\` doubled in Steam paths. No `FileLoader` is set, so
  `#include`/`#base` fail instead of touching the file system. Conditionals
  (`[$WIN32]`) use the library's per-OS defaults (`WIN32` is always true).
- The value is a list collection holding the document's single root key, so
  callers look it up by name like any other child.
- Failure: for bad input the library throws only `KeyValueException`; the
  wrapper catches exactly that and returns `steam.vdf_malformed` (details:
  `reason`). No blanket catch.
- Lookups are extension methods. `GetChild(key)` / `GetString(key)` are
  case-insensitive (Steam isn't consistent: `BetaKey` vs `betakey`; the
  library's own indexer is case-sensitive and throws on a missing key) and
  return the **last** match for a repeated key. `GetChildren()` collapses
  repeated keys the same way. `AsText()` gives a scalar's text. Don't use the
  `KVObject` indexer, and never use the reflection-based
  `KVSerializer.Deserialize<T>`.
- **Known limitation: number-like values lose their formatting.** The library
  stores a value that parses as `int`/`float` as a number and drops the
  original text, so `AsText()` returns the invariant form: `"007"` → `"7"`,
  `"1.0"` → `"1"`, `"1e5"` → `"100000"`. There's no option to turn it off.
  Accepted: library `path`s are absolute (never number-like), `buildid` is a
  plain integer (round-trips), PZ's `installdir`s are fixed names, and PZ beta
  branch names are word-like (`unstable`, `b41multiplayer`). Only a branch
  literally named like `42.0` would display as `42`. Pinned by
  `GetString_NumberLikeValue_ReturnsLibraryNormalizedText` and
  `ReadManifestAsync_NumberLikeInstallDirAndBetaKey_ReturnsNormalizedNumbers`.
  If it ever matters, recover the raw token via
  `KVSerializer.DeserializeWithSourceMap`.
- The library's KV1 reader is iterative, so deep nesting can't overflow the
  stack and no depth pre-check is needed (see `research/valvekeyvalue.md`).

`SteamLibraryReader` turns a parse failure into the file-specific warning
(`steam.library_unreadable` / `steam.manifest_unreadable`) with `path` and
`reason` details.

## DTO shape

```csharp
public sealed record PzInstall(PzInstallKind Kind, string RootPath, SteamAppInfo? Steam)
{
    public PzInstallSource Source => Steam is null ? PzInstallSource.Manual : PzInstallSource.SteamLibrary;
}
public sealed record SteamAppInfo(string BuildId, string? Branch);   // Branch null = default branch
```

JSON: `{"kind":..,"rootPath":..,"steam":{"buildId":..,"branch":..}|null,"source":..}`.
`source` is output-only (ignored on deserialization). `Error.Details` is never
null; it serializes as `{}` when unused, and a missing `details` reads as empty.
`CoreJsonContext` rejects `null`/missing values for non-nullable members
(`RespectNullableAnnotations`, `RespectRequiredConstructorParameters`).

## Inspect (manual path)

`null` throws `ArgumentNullException` (a caller bug, not an expected failure).

| Check (in order) | Error |
|---|---|
| blank/relative/invalid-char path | `install.invalid_path` (Validation) |
| directory doesn't exist | `install.path_not_found` (NotFound) |
| enumerating it throws IO/Unauthorized | `install.path_unreadable` (External) |
| contains a server marker | Ok(DedicatedServer, Manual) |
| contains a game marker | Ok(Game, Manual) |
| otherwise | `install.not_a_pz_install` (Validation) |

Server markers are checked first, because a server folder may also contain
game-like files. Markers live in one table (`PzInstallMarkers`):

- server: `StartServer64.bat`, `start-server.sh`
- game: `ProjectZomboid64.exe`, `ProjectZomboid64`, `projectzomboid.sh`, `Project Zomboid.app`

**Verify before relying on these:** during implementation, list the files at
the root of the developer's real game and server installs (read-only) and fix
the table to match. Record what you found in `research/install-markers.md`.

`Steam` is null for manual installs. (Reading the manifest
for a manual path inside a Steam library is a possible later improvement.)

## Error codes introduced

All are members of `ErrorCode` (`PzLauncher.Core/Common/ErrorCode.cs`); the table shows wire names.

| Code | Kind | Where |
|---|---|---|
| `steam.not_installed` | NotFound | scan warning |
| `steam.library_unreadable` | External | scan warning |
| `steam.manifest_unreadable` | External | scan warning |
| `steam.vdf_malformed` | Validation | parser (internal; wrapped by the two above) |
| `install.files_missing` | NotFound | scan warning |
| `install.invalid_path` | Validation | inspect |
| `install.path_not_found` | NotFound | inspect |
| `install.path_unreadable` | External | inspect |
| `install.not_a_pz_install` | Validation | inspect |

`ErrorMessages` in the app has an entry for every code above.

## ViewModel state

```
InstallsViewModel
  Installs : ObservableCollection<PzInstall>
  IsScanning : bool
  Warnings : ObservableCollection<string>   // via ErrorMessages
  ErrorText : string?                       // last add-folder error
  IsEmpty : bool                            // !IsScanning && Installs.Count == 0, raised explicitly
  ScanCommand (AsyncRelayCommand, auto-run once when the page is shown)
  AddFolderCommand (AsyncRelayCommand)
```

`IFolderPicker.PickFolderAsync` returns a `FolderPickResult`: `Picked(Path)`
→ inspect; `Cancelled` → no change; `NotLocal(Location)` (e.g. a non-local
pick from a Linux portal) → `ErrorText = ErrorMessages.FolderNotLocal(..)`,
app-only text, not a Core `ErrorCode`. No window to host the picker throws
`InvalidOperationException` (a bug).

Rescan keeps manual entries (it re-merges them, de-duplicating by path) and
replaces the Steam entries. Adding a path that's already in the list does
nothing. The path comparison and merge live in Core (`PzInstallList`, using
`IPlatformEnvironment.PathComparison`) so a future web host de-duplicates the
same way; the ViewModel only calls them.

## Compatibility / rollback

Moving the `Models\` folder placeholder out of the app and adding projects
are additive. The only edits to existing files are App/MainWindow/MainViewModel.
Rollback = revert the task's commit.
