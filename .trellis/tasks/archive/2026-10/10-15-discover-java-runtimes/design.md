# Design: Discover Java runtimes

## Boundaries

```
JavaSettingsView ─> JavaRuntimesViewModel (singleton) ─> IJavaDiscoveryService (Core, Java/)
                                 │                           ├─> IPlatformEnvironment (Os, env, registry, PathComparison)
                                 │                           └─> System.IO (release file, PE header)
                                 ├─> IPreferenceStore (manual Java homes)
                                 └─> IFolderPicker
```

- Core: candidate sources, validation, `release` parsing, PE-header arch
  check, de-duplication, manual-folder inspection.
- Desktop: session cache of the scanned list, manual list persistence, the
  设置 → Java section. `10-15-launch-game-client` reads the same ViewModel/list
  for its dropdown.

## Core: `PzLauncher.Core/Java/` (new feature folder)

`IJavaDiscoveryService` (public, singleton):

| Member | Contract | Future HTTP |
|---|---|---|
| `Task<Result<JavaScanResult>> ScanAsync(CancellationToken ct = default)` | Never fails for "nothing found"; problems are warnings | `POST /api/v1/java/scan` |
| `Task<Result<JavaRuntime>> InspectAsync(string path, CancellationToken ct = default)` | Validate a picked folder (home or `bin`) | `POST /api/v1/java/inspect` |

DTOs (records, `CoreJsonContext`, round-trip tested):

- `JavaRuntime(string HomePath, string JavawPath, JavaVersion? Version, string? Vendor, JavaRuntimeSource Source)`
- `JavaVersion(int Major, string Full)` — `null` = unknown.
- `JavaRuntimeSource` enum: `JavaHome`, `Path`, `Registry`, `KnownFolder`,
  `Manual` (wire names via `JsonStringEnumMemberName`; display via
  `DisplayNames` collapses all but `Manual` to 自动).
- `JavaScanResult(IReadOnlyList<JavaRuntime> Runtimes, IReadOnlyList<Error> Warnings)`
  — same shape as `InstallScanResult`.

Scan steps:

1. Os is macOS → empty result, no warnings (D20).
2. Collect candidate homes in priority order: `JAVA_HOME`; each `PATH` dir
   whose Java executable (`javaw.exe` / `java`) exists → resolve the file
   link, take the `bin` parent's parent; Windows registry values (below);
   sub-folders of the OS's known folders (`JavaLocations`, per OS; home-relative
   ones from `IPlatformEnvironment.HomeDirectory`). Normalize with `PathNormalizer`; skip
   invalid paths silently (env/registry values are untrusted).
3. Resolve links so `javapath` shims and symlinked homes collapse: resolve
   the executable file link (`/usr/bin/java` → alternatives → real file),
   then resolve every component of the home path, because
   `ResolveLinkTarget` only follows the last component (`default-java`,
   SDKMAN `current`, `/usr/lib64` → `/usr/lib`). A JDK 8 home reached as
   `<jdk>/jre` maps to `<jdk>` when `<jdk>/bin/<exe>` exists. De-duplicate
   with `PathComparison`; first source wins.
4. Validate each: Java executable exists; arch 64-bit; read `release`.
5. Order: version major descending, then path.

Registry: `IPlatformEnvironment` gains two generic, OS-guarded reads (the
Steam-specific method stays):

- `IReadOnlyList<string> GetRegistrySubKeyNames(RegistryHiveKind hive, string path)`
- `string? ReadRegistryString(RegistryHiveKind hive, string path, string name)`

`RegistryHiveKind` (`LocalMachine`, `CurrentUser`) is a Core enum; the
implementation opens the 64-bit view. Empty / `null` off Windows and on
security/IO errors (logged), like `ReadSteamPathWindows`.
Vendor table: the proposed table in `research/registry-layouts.md` (one data
table in code: key, enumerate subkeys, optional suffix such as
`hotspot\MSI`, value name). Adoptium was checked on this machine: the home is
`...\JDK\<ver>\hotspot\MSI` → `Path` (`JavaHome` there is a DWORD flag, not
a path). JavaSoft keys list each install twice (`17`, `17.0.9`); de-dup
collapses them. Trim a trailing `\` from values.

`release` file: `KEY="value"` lines, mixed CRLF/LF (strip `\r`); read
`JAVA_VERSION` (major = first number; legacy `1.8.0_x` → 8; `17`, `21.0.9`,
`11.0.9.1` all valid), `IMPLEMENTOR`, `OS_ARCH` (64-bit: `x86_64`, `amd64`,
`aarch64`; 32-bit: `x86`, `i586`, `i386`). Missing file →
unknown version/vendor, arch from PE header. Malformed line → skipped;
unreadable file → warning `java.release_unreadable`.

Executable header, only when `release` has no `OS_ARCH`:

- PE (Windows `javaw.exe`): `e_lfanew` at 0x3C, check `PE\0\0`, `Machine`
  (`0x8664` x64, `0xAA64` arm64 → 64-bit; `0x14C` → 32-bit).
- ELF (Linux `java`): magic `\x7FELF`, `EI_CLASS` byte 4 (`2` → 64-bit).

Any read/format failure → treated as not a Java (skipped, no warning).

Errors (`JavaErrors`, new `java` section):

| Member | Wire name | Kind | Use |
|---|---|---|---|
| `JavaInvalidPath` | `java.invalid_path` | Validation | inspect |
| `JavaPathNotFound` | `java.path_not_found` | NotFound | inspect |
| `JavaNotAJavaHome` | `java.not_a_java_home` | Validation | inspect (no Java executable in `bin`) |
| `JavaNot64Bit` | `java.not_64_bit` | Validation | inspect |
| `JavaSourceUnreadable` | `java.source_unreadable` | External | scan warning (folder/registry) |
| `JavaReleaseUnreadable` | `java.release_unreadable` | External | scan warning |

Known folder names and file names live in `Java/JavaLocations.cs`.

## Desktop

### Preferences

New field `javaRuntimes`: array of manual Java home paths (strings).
`UserPreferences.ManualJavaHomes` (empty when missing; non-string or
non-absolute entries dropped, logged once). `SaveManualJavaHomesAsync(IReadOnlyList<string>)`
rewrites only that field. `preferences.md` table gets the row.

### `JavaRuntimesViewModel` (`ViewModels/Settings/` or `ViewModels/Java/`, singleton)

- State: `IsScanning`, `Runtimes` (observable rows), `Warnings` (rendered
  from `Error`s, re-rendered on `LanguageChanged`), `IsEmpty`, `ErrorText` for
  add failures.
- `EnsureScannedAsync()` — run-once rule (like `InstallsViewModel.ActivateAsync`);
  `RescanCommand`; `AddCommand` (picker → `InspectAsync` → add as `Manual`,
  save); row `RemoveCommand` for manual rows (save).
- Merge: scanned runtimes + manual homes. A manual home also found by the
  scan is shown once, as 手动; removing it leaves the discovered entry.
  Manual homes are re-inspected on each scan; a failing one becomes a
  找不到 row (`IsAvailable = false`).
- Exposes the available runtimes for the game-client child's dropdown.

### Settings UI

- `SettingsSection` gains `Java`; sidebar gets group 游戏 with item Java
  (icon from FluentIcons). Selecting it calls `EnsureScannedAsync`.
- Section content: header row (title, 重新扫描, 添加…), busy bar, warnings,
  empty state, list rows (version + vendor as title, path muted, source chip,
  remove button on manual rows, 找不到 style for unavailable).
- Shell colors via `Shell.*` resources only.

### Resx keys (all 10)

`SettingsGroup_Game`, `Settings_Java`, `Java_Title`, `Java_Rescan`, `Java_Add`,
`Java_Remove`, `Java_Empty`, `Java_VersionUnknown`, `Java_VendorUnknown`,
`Java_Missing`, `JavaSource_Auto`, `JavaSource_Manual`, `Java_VersionFormat`
(`{0}`), plus `Error_<Member>` for the six codes.

## Trade-offs

- `release` file + PE header instead of running `java -version`: no process
  per candidate, fast, works for JREs that fail to start; cost: runtimes
  without `release` show unknown version.
- Registry reads are generic on `IPlatformEnvironment` (two methods) rather
  than one method per vendor: keeps the seam small and fakeable.
- macOS sources (`/Library/Java/JavaVirtualMachines/*/Contents/Home`,
  Homebrew) come later with the macOS launch port (D20).

## Rollback

Revert the commit. Older builds ignore the `javaRuntimes` field.
