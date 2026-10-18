# Design: launcher auto-update

## Boundaries

| Layer | Owns |
|---|---|
| `PzLauncher.Core/Update/` | Release feeds (GitHub, Gitee), source race, version comparison, asset choice, download + verification + extraction, install/rollback, leftover cleanup. No UI, no preferences. |
| `PzLauncher.Desktop` | Preferences (`updateSource`, `autoCheckUpdates`), startup orchestration, banner, Settings "Updates" section, restart, close-time install. |

Core because the decision logic is host-independent (`.trellis/spec/core/service-api.md`: "if a web UI would also need it, it belongs in Core"); the future server can reuse it.

## Core contract

```csharp
public enum UpdateSource { Auto, GitHub, Gitee }

public sealed record ReleaseInfo(
    SemVersion Version, UpdateSource Source,   // Source: GitHub or Gitee, never Auto
    string PackageName, Uri PackageUrl, long? Size, string? Sha256, Uri ReleasePageUrl);

public sealed record UpdateCheck(SemVersion Current, ReleaseInfo? Available); // null = up to date / no asset
public sealed record StagedUpdate(SemVersion Version, string StagingDirectory, Uri ReleasePageUrl);
public sealed record DownloadProgress(long Received, long? Total);

public interface IUpdateService
{
    SemVersion CurrentVersion { get; }
    bool IsDevelopmentBuild { get; }               // Current is 0.0.0-*
    bool CanInstallInPlace { get; }                // install folder writable (probe file)
    Task<Result<UpdateCheck>> CheckAsync(UpdateSource source, CancellationToken ct = default);
    Task<Result<StagedUpdate>> DownloadAsync(ReleaseInfo release, IProgress<DownloadProgress>? progress, CancellationToken ct = default);
    Task<Result<StagedUpdate?>> GetStagedAsync(CancellationToken ct = default);   // already-downloaded, newer than Current
    Task<Result> InstallAsync(StagedUpdate staged, CancellationToken ct = default);
    Task CleanUpLeftoversAsync(CancellationToken ct = default);                   // never fails; logs
}
```

- `SemVersion`: small internal record parsing `MAJOR.MINOR.PATCH[-pre][+meta]`, precedence per SemVer 2.0 (meta ignored). Hand-written; no package.
- Current version: `AssemblyInformationalVersionAttribute` of the entry assembly, passed in by the host at registration (Core must not guess the entry assembly). `PzLauncher.Desktop.csproj` gets `<Version>0.0.0-dev</Version>` so local builds are dev builds; `release.yml` overrides it with `-p:Version`.
- Install folder: `AppContext.BaseDirectory`; executable name: `PzLauncher.exe` / `PzLauncher` from `IPlatformEnvironment.Os`. Both are `internal init` properties so tests point them at a temp folder.
- RID: `win-x64` / `linux-x64` from `IPlatformEnvironment.Os` + process architecture; anything else → `CheckAsync` returns no update.

## Feeds and the race

- `internal interface IReleaseFeed { UpdateSource Source; Task<Result<ReleaseInfo?>> GetLatestAsync(string rid, CancellationToken ct); }` with `GitHubReleaseFeed` and `GiteeReleaseFeed`. Each:
  - `GET .../releases/latest` with `User-Agent: PzLauncher/<version>` (GitHub requires it) and `Accept: application/json`.
  - Rejects a prerelease (flag or `-` in tag) → `null` (D5). Parses `tag_name` (`v` prefix stripped).
  - Picks the asset named exactly `PzLauncher-<ver>-<rid>.zip` (win) / `.tar.gz` (linux); none → `null`.
  - GitHub fills `Size` and `Sha256` (from `digest` when it starts with `sha256:`); Gitee leaves them `null`.
  - JSON via a source-generated `UpdateJsonContext` (AOT); a malformed body → `update.release_info_invalid`.
- `CheckAsync(Auto)`: start both feeds with a linked CTS; await with `Task.WhenAny` loops; the first `Result` that is a success with a platform package (including "not newer than current") wins, the other is cancelled; a success without a package (prerelease latest, or Gitee still uploading assets) waits for the other source and is used only if that one has none either. Both failed → return the GitHub error (`update.network_failed`). Per-request timeout 15 s via `CancelAfter`.
- `CheckAsync(GitHub|Gitee)`: that feed only.
- `Available` is set only when `release.Version > Current`.

HTTP: one `HttpClient` owned by Core, created with a `SocketsHttpHandler` (automatic redirects, `PooledConnectionLifetime` 5 min). Registered as an internal `UpdateHttpClient` holder so tests construct services with an `HttpMessageHandler` fake; no `Microsoft.Extensions.Http` package. The download uses `HttpCompletionOption.ResponseHeadersRead` and no overall timeout (cancellation + a 60 s stall timeout per read).

## Staging layout

All under `<install>/.update/` (same volume as the install, so moves are renames; writing there doubles as the writability check):

```
<install>/.update/
  download/<package>.part   → renamed to <package> when complete
  staged/<version>/...      extracted package
  staged/<version>.ready    marker written last; GetStagedAsync trusts only marked folders
  old/<timestamp>/...       previous files moved aside during install
```

- Verify: size (if known), SHA-256 (if known) → else `update.package_invalid`; extract (`ZipFile` / `GZipStream` + `TarFile`), reject entries escaping the folder; the executable must exist at the root (`update.package_invalid`). On Linux ensure the executable has `u+x`.
- One staged version at a time: a newer download deletes older `staged/*`.

## Install (R5)

1. Enumerate files in `staged/<v>/` (relative paths).
2. For each, if `<install>/<rel>` exists, move it to `old/<ts>/<rel>`; then move the staged file to `<install>/<rel>`. Record each step.
3. Any exception (I/O, access) → undo recorded steps in reverse, return `update.install_failed`, log once.
4. Success → delete `staged/<v>` and its marker.

Renaming a running executable and its loaded DLLs is allowed on Windows and Linux, so this runs inside the live process. Files not in the new package stay (the folder may hold unrelated user files). `CleanUpLeftoversAsync` at startup deletes `old/` and stale `download/` parts.

## Desktop orchestration

- `UpdateCoordinator` (Desktop service, singleton, ObservableObject via a VM): state machine `Idle → Checking → Downloading(progress) → Ready(staged) | Available(release, no in-place) | UpToDate | Failed(error)`; holds `Source`/`AutoCheck` preferences through `IPreferenceStore`.
- Startup (after the main window shows): `CleanUpLeftoversAsync`; `GetStagedAsync` → if present, `Ready` (no network, D11); else if `AutoCheck` and not dev build → check → if available: `CanInstallInPlace` ? download : `Available` (D8). Failures logged only.
- `Check now`: same flow, errors surface in Settings; disabled while a check/download runs.
- Banner (`MainView` top, bound to a `UpdateBannerViewModel` on `MainViewModel`): `Ready` → "Version X downloaded" + "Restart and update" (disabled while `GameRunState.IsGameRunning`, D9) + "Later"; `Available` → "Version X is available" + "Open download page" + "Later". Opening a URL goes through a new `IUrlLauncher` service (Avalonia `Launcher` behind an interface — ViewModels stay Avalonia-free).
- Restart: `InstallAsync`, then start `<install>/<exe>` via `IProcessRunner`, then `desktop.Shutdown()`. If install fails → banner shows the error, app keeps running.
- Close: the `WindowPlacementPersistence` flush delegate in `App.axaml.cs` also awaits `coordinator.InstallPendingOnCloseAsync(ct)` when state is `Ready` (whether or not "Later" was clicked). OS-shutdown closes skip it; the staged update is re-offered next start (D11).

## Preferences

`updateSource` (`auto`/`github`/`gitee`, `UpdateSourcePreferences.PreferenceName`) and `autoCheckUpdates` (bool) added to `PreferencesFile`, `UserPreferences` (`UpdateSource?`, `bool?`), with `SaveUpdateSourceAsync` / `SaveAutoCheckUpdatesAsync`; spec table in `.trellis/spec/desktop/preferences.md` updated.

## Errors (new `ErrorCode` members, `UpdateErrors` factory)

| Member | Wire name | Kind |
|---|---|---|
| `UpdateNetworkFailed` | `update.network_failed` | External |
| `UpdateReleaseInfoInvalid` | `update.release_info_invalid` | External |
| `UpdatePackageInvalid` | `update.package_invalid` | External |
| `UpdateInstallFolderNotWritable` | `update.install_folder_not_writable` | Conflict |
| `UpdateInstallFailed` | `update.install_failed` | External |

## Trade-offs

- Race vs geolocation (user choice D2): reflects actual reachability, costs two requests per check. Unauthenticated GitHub API allows 60 requests/hour/IP — one per start is fine.
- Gitee has no checksum; integrity there rests on HTTPS + archive validation. Publishing a `SHA256SUMS` asset is a possible follow-up (out of scope).
- In-process rename install avoids a helper executable/script, at the cost of leaving the old files under `.update/old` until the next start.

## Rollback

Feature is additive. Reverting the commits removes it; `.update/` folders left on users' machines are inert. Unknown preference fields are ignored by older versions.
