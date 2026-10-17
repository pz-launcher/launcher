# Design: Preload Zabric in allow-all mode

## Boundaries

- Core owns lookup, preloadability check and the `config.json` edit (same
  feature folder `PzLauncher.Core/ZombieBuddy/`). Desktop only calls it from
  the Home launch path and shows the error.
- No change to `GameLaunchService`: the entry reaches ZombieBuddy through its
  own config file, not the command line.

## Core

### API (`IZombieBuddyService`)

```
/// Registers the installed Zabric in ZombieBuddy's preload list when the json's agent arg is allow-all.
Task<Result<ZabricPreload>> EnsureZabricPreloadAsync(string installRoot, string extraGameArgs, CancellationToken ct = default);
```

- `ZabricPreload { NotApplicable, Current, Written }` (enum DTO, registered in
  `CoreJsonContext` like the other ZombieBuddy enums). `NotApplicable`:
  unsupported install, game json unreadable (launch reports it), agent arg
  not allow-all, or no Zabric found.
- Runs under the service's `_gate` (no interleaving with initialize/remove).
- `Written` logged at Information with the config path and jar path.

### Constants (`ZabricFiles`, internal static, next to `ZombieBuddyFiles`)

- `ModId = "Zabric"`, `ItemId = "3820726385"`, `ConfigFile = "config.json"`,
  `DefaultConfigFolder = ".zombie_buddy"`, `PreloadModsKey = "preload_mods"`,
  `InfPathKey`/`JarPathKey`, `ManifestPreloadAttribute = "ZB-Preload"`,
  `CacheDirArg = "-cachedir="`, cache default `Zomboid`.

### Agent options

- `ZombieBuddyAgentArgs.Option(vmArgs, os, key)` → last value of `key`
  (case-insensitive key, value trimmed) on the first agent entry; reused by
  `IsAllowAll`. Config folder: `config_dir` value, rooted against the game
  folder when relative, else `<platform.HomeDirectory>/.zombie_buddy`.

### Lookup (`ZabricLocator`, internal)

- `cachedir`: last `-cachedir=` token of `LaunchArguments.Split(extraGameArgs)`,
  normalized with `PathNormalizer`; else `<HomeDirectory>/Zomboid`.
- Candidate mod folders in order (R2): `<cache>/Workshop/*/Contents/mods/Zabric`
  (items in ordinal name order), then for each `CandidateLibrariesAsync`
  library `workshop/content/108600/<3820726385 first, then the rest in ordinal
  order>/mods/Zabric`, then `<cache>/mods/Zabric`. Only existence checks per
  item folder; unreadable directories are skipped (logged at Debug).
- Version folder: subfolders whose name parses as a version (`42`, `42.13`),
  highest first, the first holding `mod.info`; `common/` is the fallback for
  `mod.info` when that one has no `javaJarFile` (Zabric's rule). A candidate
  counts only if its `mod.info` has `id=Zabric`; otherwise the next one.

### `mod.info` and manifest (`ZabricModInfo`, internal, pure + file read)

- Value lookup = Zabric's `modInfoValue`: first line whose lower-cased text
  starts with `key=` and has a non-empty trimmed value.
- Preloadable: jar file exists, `javaPkgName` present, `javaPreload=true`
  (case-insensitive), jar `META-INF/MANIFEST.MF` main section has
  `ZB-Preload: true` (attribute name case-insensitive, continuation lines
  joined). Read with `System.IO.Compression.ZipFile` (AOT-safe). Failure →
  `zombiebuddy.zabric_not_preloadable` with `path` and `reason`
  (`missing_jar` | `missing_pkg_name` | `not_java_preload` | `no_manifest_preload` | `unreadable_jar`).

### Config edit (`ZombieBuddyConfigEditor`, internal)

- Read as `JsonNode` (comments/trailing commas not expected; Gson writes
  plain JSON). Missing file → new object. Root not an object, or
  `preload_mods` present and not an object, or a parse error →
  `zombiebuddy.preload_config_invalid` (`path`); nothing written.
- Current check = Zabric's: both stored paths name the same files
  (`platform.PathComparison` on normalized paths), or both stored files exist
  and the jar sha256 matches. Stored value not an object/strings → not
  current.
- Rewrite: new `preload_mods` object with `Zabric` first, other entries in
  their order; other root fields untouched. Write only when the entry changed
  or `Zabric` was not first. Indented UTF-8 without BOM, temp file in the same
  folder + `File.Move(overwrite)`; folder created. IO / access errors on read
  or write → `zombiebuddy.preload_config_io_failed` (`path`, `reason`).

### Errors (`ErrorCode` zombiebuddy section, `ZombieBuddyErrors`)

| Wire name | Kind | When |
|---|---|---|
| `zombiebuddy.zabric_not_preloadable` | Validation | found Zabric copy fails a preload requirement |
| `zombiebuddy.preload_config_invalid` | Validation | `config.json` not JSON / root or `preload_mods` not an object |
| `zombiebuddy.preload_config_io_failed` | External | reading or writing `config.json` failed |

Each: factory with `Details`, `Error_<Member>` in all 10 resx, `ErrorMessages`
mapping, Kind pinned in `ErrorCodeTests` (`.trellis/spec/core/error-handling.md`).

## Desktop

- `HomeViewModel.LaunchAsync`: after `PrepareZombieBuddyAsync` returns true,
  `EnsureZabricPreloadAsync(root, overrides.ExtraGameArgs)` with the same
  overrides object the launch uses; on error `ShowErrorAsync(text)` and then
  continue to `_launcher.LaunchAsync`. No new UI, no new resx beyond the error
  texts.
- `FakeZombieBuddyService` and `DesignData.SampleZombieBuddy` implement the new
  method (fake: configurable result + recorded calls).

## Trade-offs

- Version folder by name, not game version: `PzInstall` has no version;
  Zabric ships only `42`. A future newer folder on an older game would pick
  the wrong one; accepted (out of scope).
- Steam item order inside a library is a guess (the game's own order is
  unknown here); Zabric's item first keeps the common case right.
- Error then launch: worst case equals today's behaviour (no preload).

## Rollback

- The feature is one Core method plus one call in Home; reverting removes it.
  The written entry is valid ZombieBuddy config and harmless if left behind.

## Revision (2026-10-17): Zabric in its own feature folder

User request: Zabric lives in `PzLauncher.Core.Zabric`, separate from ZombieBuddy.

- `PzLauncher.Core/Zabric/`: `IZabricService` / `ZabricService` (singleton,
  own gate) with `EnsurePreloadAsync(installRoot, extraGameArgs, ct)` →
  `Result<ZabricPreload>`; `ZabricFiles`, `ZabricLocator`, `ZabricModInfo`,
  `ZabricPreload`, `ZabricPreloadEntry`, `ZabricErrors`, and the config editor
  as `ZabricPreloadConfig` (named after Zabric's `PreloadConfig`).
  `IZombieBuddyService` loses `EnsureZabricPreloadAsync`.
- ZombieBuddy keeps only ZombieBuddy facts: `config_dir` option, default
  `.zombie_buddy` folder and `config.json` name move to `ZombieBuddyFiles`;
  `ZombieBuddyStatus` gains `ConfigFolder` (string?, null when unsupported or
  unresolvable). `ZabricService` reads `IsSupported`, `IsAllowAll`,
  `ConfigFolder` from `IZombieBuddyService.GetStatusAsync`; a status error →
  `NotApplicable`.
- Steam library order (`CandidateLibrariesAsync` + `OwnLibrary`) moves to an
  internal `Steam/WorkshopLibraries` singleton used by both services.
- Error codes become a `zabric` section: `zabric.not_preloadable`,
  `zabric.preload_config_invalid`, `zabric.preload_config_io_failed`
  (`ZabricNotPreloadable`, `ZabricPreloadConfigInvalid`,
  `ZabricPreloadConfigIoFailed`); resx keys and `ErrorMessages` follow.
- Desktop: `HomeViewModel` takes `IZabricService`; `FakeZabricService` and a
  `DesignData` sample; Zabric removed from the ZombieBuddy fake/sample.
- Tests move to `PzLauncher.Core.Tests/Zabric/`.

## Revision (2026-10-17, 2): shared mod logic under `PzLauncher.Core.Mod`

User request: extract what Zabric and ZombieBuddy share and put both under
`PzLauncher.Core.Mod`. Supersedes the folder/namespace and error-code parts of
the revision above; behaviour is unchanged.

- `PzLauncher.Core/Mod/` (`PzLauncher.Core.Mod`), any Project Zomboid mod:
  - `ModInfo`: reads a version folder's `mod.info` (first line per key, case-
    insensitive key, non-empty value), highest version folder, `common/`
    fallback when the version folder's has no `javaJarFile`.
  - `ModLocator`: finds a mod by id in the game's order (staged Workshop,
    Steam Workshop items with an optional preferred item first, `<cache>/mods`);
    `CacheFolder(extraGameArgs, gameFolder, home)`.
  - `ModFolders`: path constants/helpers for those roots and `-cachedir=`.
- `PzLauncher.Core/Mod/ZombieBuddy/` (`PzLauncher.Core.Mod.ZombieBuddy`): the
  existing ZombieBuddy feature moved as is, plus what any ZombieBuddy Java mod
  needs:
  - `ZombieBuddyJavaMod`: preloadable check on a `ModInfo` (jar from
    `javaJarFile`, `javaPkgName`, `javaPreload=true`, manifest `ZB-Preload`).
  - `ZombieBuddyPreloadConfig.EnsureAsync(configFolder, modId, entry, first)`:
    registers one `preload_mods` entry (current by path or jar sha256; other
    content kept; atomic; no write when unchanged; `first` moves it to the front).
  - `ZombieBuddyPreload` (NotApplicable/Current/Written) and
    `ZombieBuddyPreloadEntry` replace the Zabric-named types.
  - Errors back in the `zombiebuddy` section: `zombiebuddy.mod_not_preloadable`,
    `zombiebuddy.preload_config_invalid`, `zombiebuddy.preload_config_io_failed`
    (`ZombieBuddyModNotPreloadable`, `ZombieBuddyPreloadConfigInvalid`,
    `ZombieBuddyPreloadConfigIoFailed`); `zabric` section removed.
- `PzLauncher.Core/Mod/Zabric/` (`PzLauncher.Core.Mod.Zabric`): `IZabricService`
  / `ZabricService` and `ZabricFiles` (mod id, item id) only; it calls
  `ModLocator`, `ZombieBuddyJavaMod` and `ZombieBuddyPreloadConfig` with
  `first: true`.
- `Steam/WorkshopLibraries` stays in Steam and gains the per-item lookup
  (`<library>/steamapps/workshop/content/108600/<itemId>`) that ZombieBuddy's
  channel lookup uses; ZombieBuddy keeps its installer lookup rules.
- Tests mirror the folders (`PzLauncher.Core.Tests/Mod/...`).
