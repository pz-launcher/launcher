# Service API Shape

> Contract between Core and its hosts. Today: the desktop app, in-process.
> Later: `PzLauncher.Server` exposes the **same** services over HTTP. Design
> every service so that mapping is mechanical.

**If a web UI would also need it, it belongs in Core, not in a ViewModel.**

---

## Services

Reference: `PzLauncher.Core/Installs/IInstallDetectionService.cs`, `InstallDetectionService.cs`.

- `I<Feature><Noun>Service` interface; `internal sealed` implementation registered via DI.
- Public operations are async, return `Task<Result<T>>` / `Task<Result>`, end with `CancellationToken ct = default`.
- Parameters are primitives, or one `<Verb><Noun>Request` record when there are more than ~3.
- One method = one future HTTP endpoint. No call sequences required for one user action.
- Progress: `IProgress<TProgress>?` with a record payload. Streams: `IAsyncEnumerable<T>`. No C# `event`/`IObservable` from Core.

## DTOs

Reference: `PzLauncher.Core/Installs/PzInstall.cs`, `SteamAppInfo.cs`, `InstallScanResult.cs`.

- Immutable records; collections are `IReadOnlyList<T>`; paths are absolute.
- No UI types (Avalonia, `ObservableCollection`, `ObservableObject`).
- Every DTO crossing the boundary is registered in `PzLauncher.Core/Serialization/CoreJsonContext.cs` and round-trip tested. Its options are the JSON contract.

## Dependency injection

Reference: `PzLauncher.Core/ServiceCollectionExtensions.cs`.

- Hosts call `AddPzLauncherCore()` **and** `AddLogging(...)` (Core doesn't register logging).
- Singletons unless a service holds per-operation state.
- Test seams: `IPlatformEnvironment` (OS, registry, env, home dir) and `TimeProvider`. A fixed system location a test must redirect (e.g. `/usr/lib/jvm`) is an `internal init` property on the service, defaulting to the real list; every test sets it. Reference: `JavaDiscoveryService.LinuxSystemFolders`.
- Registry reads are generic (`IPlatformEnvironment.GetRegistrySubKeyNames` / `ReadRegistryString`, 64-bit view): empty/`null` off Windows or for a missing key, but they **throw** `IOException` / `UnauthorizedAccessException` for an existing key that can't be read, so the service can report it as a warning at its boundary. Fakes must model all three cases.
- Path comparison uses `IPlatformEnvironment.PathComparison`. Install list logic lives in `PzLauncher.Core/Installs/PzInstallList.cs`, not in hosts.
- Plain `System.IO`; no file system abstraction library.

## Future HTTP mapping (design for it, don't build it)

| Core | HTTP |
|------|------|
| service method | one endpoint, e.g. `ScanAsync` → `POST /api/v1/installs/scan` |
| `Result` success / failure | `200` + JSON / RFC 7807 with `code` = wire name, status from `ErrorKind` |
| `IAsyncEnumerable<T>` | Server-Sent Events |
| `CancellationToken` | request abort |

## Forbidden

- `void`/`bool` returns for operations that can fail for expected reasons. Use `Result`.
- `Action<string>` callbacks for logging/progress. Use `ILogger<T>` / `IProgress<T>`.
- Service locators.
