# Implement: Discover Java runtimes

## Checklist

1. Research (persist to `research/registry-layouts.md`): registry key/value
   layouts per vendor in R1; check Adoptium on this machine (`reg query`,
   read-only).
2. Core errors: `ErrorCode` `java` section, `Java/JavaErrors.cs`,
   `ErrorCodeTests` rows.
3. Core platform: `RegistryHiveKind`, `GetRegistrySubKeyNames`,
   `ReadRegistryString` on `IPlatformEnvironment` + `PlatformEnvironment` +
   both test fakes (registry map).
4. Core Java: `JavaLocations`, `release` parser, PE-header check, candidate
   collection, `JavaDiscoveryService` (scan + inspect), DTOs in
   `CoreJsonContext`, DI registration.
5. Core tests: `PzLauncher.Core.Tests/Java/` — release parser (fixture from
   PZ's `jre64/release`), PE and ELF headers (tiny hand-built headers), scan
   per source for Windows and Linux fake platforms,
   de-dup, warnings, inspect codes; a `JavaHomeBuilder` in `TestSupport/`.
6. Desktop preferences: `javaRuntimes` field, `ManualJavaHomes`,
   `SaveManualJavaHomesAsync`, store tests.
7. Desktop: `JavaRuntimeSource` in `DisplayNames`; `ErrorMessages` arms; resx
   keys ×10; `JavaRuntimesViewModel`; `SettingsSection.Java` + sidebar +
   section view; DI; `DesignData`.
8. Desktop tests: `JavaRuntimesViewModelTests`, `SettingsViewModelTests`
   update, fakes.
9. Spec update (phase 3): `core/directory-structure.md` (`Java/`),
   `core/service-api.md` (registry seam), `desktop/preferences.md` row.

## Validation

```bash
dotnet build PzLauncher.slnx -warnaserror
dotnet test PzLauncher.slnx
```

`dotnet format` on this task's `.cs` files; manual smoke `prd.md` AC7
(Windows) and AC8 (Linux, by the user).

## Risky points

- Registry layouts differ per vendor; keep them in one table so a wrong guess
  is a one-line fix.
- Link resolution on `javapath` shims (file symlinks, not dir): resolve the
  `javaw.exe` file link, then take its `bin` parent's parent.
- Never touch the real registry or Program Files in tests (fake platform +
  temp dirs only).
