# Design: shared HTTP client with proxy settings

## Boundaries

| Layer | Owns |
|---|---|
| `PzLauncher.Core/Net/` | The shared `HttpClient`, the switchable proxy, proxy settings DTO, connection test. No persistence, no encryption. |
| `PzLauncher.Core/Update/` | Unchanged behavior; takes the shared client instead of `UpdateHttpClient`. |
| `PzLauncher.Desktop` | Persisting settings (with DPAPI on Windows), applying them at startup, Settings → Proxy page. |

Core owns the test and the proxy logic because a future web UI would need both (`.trellis/spec/core/service-api.md`).

## Core contract

```csharp
namespace PzLauncher.Core.Net;

public enum ProxyMode { System, None, Manual }

/// Host without scheme; Port 1–65535; Username/Password null when unused.
public sealed record ManualProxy(string Host, int Port, string? Username, string? Password);

/// Manual is non-null exactly when Mode == Manual.
public sealed record ProxySettings(ProxyMode Mode, ManualProxy? Manual)
{
    public static readonly ProxySettings Default = new(ProxyMode.System, null);
}

public sealed record ConnectionTestTarget(string Name, Uri Url);    // "GitHub", "Gitee"
public sealed record ConnectionTestResult(string Name, Result<TimeSpan> Outcome); // or Error? + Elapsed, whichever serializes per CoreJsonContext rules

public interface INetworkService
{
    ProxySettings Proxy { get; }
    /// Validates (Validation errors: network.proxy_host_invalid, network.proxy_port_invalid); on success applies immediately.
    Result ApplyProxy(ProxySettings settings);
    Task<IReadOnlyList<ConnectionTestResult>> TestConnectionAsync(CancellationToken ct = default);
}
```

- `ManualProxy.Parse`-style validation lives in Core (`ProxySettingsValidation`): host may come with `http://` prefix (stripped), must be a valid DNS name or IP (`Uri.CheckHostName` != Unknown), no path/credentials in the host; port 1–65535.
- `ApplyProxy` is synchronous: it swaps a reference.
- Test targets: GitHub and Gitee release API URLs from `LauncherReleases` (moved/shared constants); a `GET` with `HttpCompletionOption.ResponseHeadersRead`, any HTTP status counts as reachable (a 403 rate-limit still proves the route works), 15 s timeout each, both concurrently. Failure → `network.unreachable` (External) with `target` and `reason` details; timeout → `network.timeout` (External).

## Shared client

- `internal sealed class LauncherHttpClient` (renamed and moved from `Update/UpdateHttpClient.cs`): same handler settings, `UseProxy = true`, `Proxy = switchableProxy` set once at construction, `DefaultProxyCredentials` untouched.
- `internal sealed class SwitchableProxy : IWebProxy`:
  - `volatile IWebProxy? _current` — System: `HttpClient.DefaultProxy`; None: `null`; Manual: `new WebProxy(new Uri($"http://{host}:{port}")) { BypassProxyOnLocal = true, Credentials = username is null ? null : new NetworkCredential(username, password) }`.
  - `GetProxy(uri)` → `_current?.GetProxy(uri)` (null = direct). `IsBypassed(uri)` → `_current?.IsBypassed(uri) ?? true`. Never throws (a throw silently goes direct — research §3); invalid states are prevented by validation.
  - `Credentials` → a stable `ForwardingCredentials : ICredentials` whose `GetCredential(uri, authType)` asks `_current?.Credentials`. Setter ignored.
- `LauncherHttpClient`, `SwitchableProxy` and `NetworkService` are singletons in `AddPzLauncherCore`; `UpdateService` and `ReleaseFeed` take `LauncherHttpClient`. Tests construct `LauncherHttpClient` with a fake handler (as `UpdateHttpClient` did) and test `SwitchableProxy` directly.

Connections already pooled through the old proxy close when idle (≤ 1 min) or at the 5 min lifetime; in-flight requests finish on their route (R2).

## Desktop

### Preferences (`.trellis/spec/desktop/preferences.md`)

New field `proxy`: `{mode, host, port, username, password}`.

| Member | Values |
|---|---|
| `mode` | `system`, `none`, `manual` (`ProxyModePreferences.PreferenceName`); unknown → whole entry null (System) |
| `host`, `port` | kept even when mode isn't manual, so switching back restores them; invalid → dropped |
| `username` | string or absent |
| `password` | Windows: `dpapi:<base64>`; Linux: `plain:<text>`; absent when none. A value with the other prefix or undecryptable → loads as null, logged (D8) |

- `UserPreferences.Proxy` (`SavedProxy?` record holding mode + manual fields + decrypted password) and `SaveProxyAsync`.
- Encryption: `ISecretProtector` (Desktop service) — `WindowsDpapiSecretProtector` via `[LibraryImport]` of `crypt32!CryptProtectData`/`CryptUnprotectData` (`CRYPTPROTECT_UI_FORBIDDEN`, current-user scope, no entropy) and `PlainSecretProtector` for Linux. Hand-written P/Invoke instead of the `System.Security.Cryptography.ProtectedData` package keeps AOT certain and avoids a dependency; if the analyzer bans P/Invoke in Desktop, put it under `Core/Platform/` instead and expose through `IPlatformEnvironment`.
  - Tests: protector round-trip on Windows (skip elsewhere), corrupted input → null.

### Startup

In `App.axaml.cs`, after the provider is built and before the main window opens (so before `UpdateCoordinator` starts): `network.ApplyProxy(saved ?? Default)`; an invalid saved proxy (shouldn't happen) is logged and System is used.

### Settings → Proxy (`ProxySettingsViewModel`, `ProxySettingsView`)

- Radio buttons: System proxy (+ per-OS hint, D7) / No proxy / Manual HTTP proxy.
- Manual fields (enabled only in Manual): Address, Port, Username (optional), Password (masked, optional; Linux shows the plain-text note, D2).
- Mode change → `ApplyProxy` + save immediately. Field edits → validate on each keystroke (inline error from the validation `Error`); when valid, apply + save after 300 ms debounce (same pattern as `InstallOverridesService`, `TimeProvider` timer). Switching to Manual with invalid fields shows the error and leaves the previous mode in effect.
- Test connection button: runs `TestConnectionAsync`; shows a row per target: name, "OK (123 ms)" or localized error text. Disabled while running. Text re-renders on language change.
- Close-time flush includes a pending proxy save (`WindowPlacementPersistence` flush delegate), like install overrides.

## Errors (`NetworkErrors`)

| Member | Wire name | Kind |
|---|---|---|
| `NetworkProxyHostInvalid` | `network.proxy_host_invalid` | Validation |
| `NetworkProxyPortInvalid` | `network.proxy_port_invalid` | Validation |
| `NetworkUnreachable` | `network.unreachable` | External |
| `NetworkTimeout` | `network.timeout` | External |

## Trade-offs

- Switchable `IWebProxy` vs rebuilding the client: no restart and no handler churn; costs a tiny forwarder and loses PAC multi-proxy fail-over (research §3), acceptable.
- Linux "system proxy" = env vars only (a .NET limitation); documented in the UI rather than parsing GNOME/KDE settings.
- Plain-text password on Linux (user decision D2) rather than a keyring dependency.

## Rollback

Additive apart from the `UpdateHttpClient` rename. Reverting removes the page; an unknown `proxy` field is ignored by older builds.
