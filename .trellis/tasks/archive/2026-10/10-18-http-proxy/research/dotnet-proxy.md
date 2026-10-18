# Research: .NET system / manual proxy with SocketsHttpHandler (runtime-switchable)

- **Query**: How `SocketsHttpHandler` picks a proxy by default on Windows/Linux/macOS, whether the system proxy tracks live changes, whether the proxy can be switched at runtime, `WebProxy`/auth/SOCKS support, AOT concerns.
- **Scope**: mixed (repo + dotnet/runtime `release/10.0` source + MS Learn)
- **Date**: 2026-10-18

All runtime source paths below are under
`https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/` (abbreviated `RT/`).
`SHH/` = `RT/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/`.

## Files Found (this repo)

| File Path | Description |
|---|---|
| `PzLauncher.Core/Update/UpdateHttpClient.cs` | Only `HttpClient`; `new SocketsHttpHandler { AllowAutoRedirect, PooledConnectionLifetime = 5 min, ConnectTimeout = 15 s }` — no `Proxy`/`UseProxy` set, so defaults apply |
| `PzLauncher.Core/ServiceCollectionExtensions.cs:37` | `services.TryAddSingleton(_ => UpdateHttpClient.Create(currentVersion))` — one process-wide instance |
| `PzLauncher.Core/PzLauncher.Core.csproj` | `net10.0` |

## 1. Default behavior (`UseProxy = true`, `Proxy = null`)

**Handler -> `HttpClient.DefaultProxy`.** `SHH/HttpConnectionPoolManager.cs:131-139` (constructor, runs once when the handler is first used):

```csharp
if (settings._useProxy)
{
    _proxy = settings._proxy ?? HttpClient.DefaultProxy;
    if (_proxy != null)
        _proxyCredentials = _proxy.Credentials ?? settings._defaultProxyCredentials;
}
```

**`HttpClient.DefaultProxy`** (`RT/System.Net.Http/src/System/Net/Http/HttpClient.cs:39-47`) = `LazyInitializer.EnsureInitialized(ref s_defaultProxy, () => SystemProxyInfo.Proxy)`; `SystemProxyInfo.Proxy` is a `static Lazy<IWebProxy>` (`SHH/SystemProxyInfo.cs`). So one process-wide object, built on first access. It has a public setter.

Per platform (`SHH/SystemProxyInfo.{Windows,Unix,OSX}.cs`):

| OS | Order |
|---|---|
| Windows | `HttpEnvironmentProxy.TryCreate` (env vars) **else** `new HttpWindowsProxy()` — comment: "We create instance even if there is currently no proxy as that can change during application run." |
| Linux | env vars **else** `HttpNoProxy`. **No GNOME/KDE/gsettings reading** (no such code anywhere in System.Net.Http). |
| macOS | env vars **else** `MacProxy` (CFNetwork; `MacProxy.GetProxy` calls `CFNetworkCopySystemProxySettings` + `CFNetworkCopyProxiesForURL` **on every call**, PAC URL/script supported). |

**Env vars** (`SHH/HttpEnvironmentProxy.cs`, `.Unix.cs`, `.Windows.cs`):
- Names: `http_proxy`, `https_proxy`, `all_proxy`, `no_proxy`. On Unix lowercase checked first, then uppercase (`HTTP_PROXY` uppercase is skipped when `GATEWAY_INTERFACE` is set — CGI guard). On Windows only the uppercase names are queried (env is case-insensitive there).
- Protocol-specific wins over `all_proxy`. Values may be `host[:port]`, `http://`, `https://`, `socks4://`, `socks4a://`, `socks5://`, with optional `user:pass@` (becomes `HttpEnvironmentProxyCredentials`).
- **On Windows, if any of HTTP_PROXY/HTTPS_PROXY/ALL_PROXY is set, the Windows system proxy is ignored entirely**, and env vars are read once (Lazy).
- `NO_PROXY`: comma-separated, no `*` wildcards, leading dot for subdomains (per MS Learn).

**Windows system proxy** — `SHH/HttpWindowsProxy.cs` + `SHH/WinInetProxyHelper.cs`:
- `WinInetProxyHelper` ctor calls `WinHttpGetIEProxyConfigForCurrentUser` → the **per-user WinINet/"Internet Options"/Settings > Network > Proxy** config (HKCU `...\Internet Settings`), i.e. what Clash / v2rayN "system proxy" toggles. It does **not** read the machine WinHTTP config (`netsh winhttp set proxy`).
- Fields read: `AutoConfigUrl` (PAC URL), `AutoDetect` (WPAD), `Proxy` (manual, e.g. `127.0.0.1:7890` or `http=..;https=..`), `ProxyBypass` (incl. `<local>`).
- Auto settings (WPAD and/or PAC) → `WinHttpGetProxyForUrl` per request via a WinHTTP session (`WinInetProxyHelper.GetProxyForUrl`); a WPAD detection failure is cached for 2 min (`RecentAutoDetectionInterval = 120_000`). If WinHTTP returns a proxy + bypass list it falls back to manual parsing.
- Manual settings → `MultiProxy.ParseManualSettings` for http and https separately; bypass list parsed manually (`*` wildcards via `SimpleRegex`, `<local>` = loopback, own interface IPs, dot-less hosts).
- Implements internal `IMultiWebProxy` (PAC "PROXY a; PROXY b" fail-over list via `MultiProxy`, `FailedProxyCache`). `IsBypassed` always returns `false`; `GetProxy` returns `null` for direct.

MS Learn (`HttpClient.DefaultProxy` remarks) confirms: Windows = env vars else user proxy settings; macOS = env vars else system settings; Linux = env vars else "a non-configured instance that bypasses all addresses".

## 2. Is the system proxy cached or live?

- **Windows, .NET 9+ (incl. 10): live.** PR [dotnet/runtime#103364 "update proxy setting on registry changes"](https://github.com/dotnet/runtime/pull/103364) (merged 2024-07-08, milestone 9.0.0). `HttpWindowsProxy` opens `HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings`, calls `RegNotifyChangeKeyValue(..., watchSubtree: true, NAME|LAST_SET|ATTRIBUTES|THREAD_AGNOSTIC)`, and on each signal (thread-pool wait) re-registers and runs `UpdateConfiguration()` under `lock(proxy)`, which builds a new `WinInetProxyHelper` (re-reads IE config) and re-parses manual/bypass settings (`HttpWindowsProxy.cs:35-72`). Verified: `release/8.0` copy has 0 occurrences of `RegNotifyChangeKeyValue`; `release/9.0` and `release/10.0` have it. So toggling Clash/v2rayN system proxy while the app runs is picked up by the **same** `HttpClient.DefaultProxy` object and therefore by the existing handler, because `GetProxy`/`GetMultiProxy` is evaluated per request (see §3).
  - Caveat: the env-var path (if HTTP(S)_PROXY/ALL_PROXY set) is read once and never refreshed.
- **macOS:** live (CFNetwork queried per `GetProxy`).
- **Linux:** env vars read once per process (process env can't be changed externally anyway). No desktop-environment settings.
- **Getting a "fresh" system proxy:** there is no public API that constructs a new `HttpWindowsProxy`. `WebRequest.GetSystemWebProxy()` is literally `=> HttpClient.DefaultProxy` (`RT/System.Net.Requests/src/System/Net/WebRequest.cs:548`) — same singleton. Constructing a new `SocketsHttpHandler` with `Proxy = null` also just re-uses `HttpClient.DefaultProxy`. On .NET 10/Windows none of this is needed: the singleton self-updates. Both are plain managed code / framework P/Invokes, AOT-safe.

## 3. Switching proxy at runtime

- **`Proxy`, `UseProxy`, `DefaultProxyCredentials` are locked after first send.** Each setter calls `CheckDisposedOrStarted()` which throws `InvalidOperationException(net_http_operation_started)` once `_handler != null` (`SHH/SocketsHttpHandler.cs:31-38, 78-106`). Confirmed.
- **But the `IWebProxy` instance is consulted per request.** `HttpConnectionPoolManager.SendAsync` (`SHH/HttpConnectionPoolManager.cs:376-418`): `if (!_proxy.IsBypassed(uri)) proxyUri = _proxy.GetProxy(uri)` (or `GetMultiProxy` for internal `IMultiWebProxy`), then `GetConnectionKey(request, proxyUri, ...)`.
- **Pools are keyed by proxy URI.** `HttpConnectionKey(Kind, Host, Port, SslHostName, ProxyUri, Identity)` with equality including `ProxyUri` (`HttpConnectionPoolManager.cs:528-556`). Kinds: `Http`/`Https` (direct), `Proxy` (plain-http via proxy, one pool per proxy), `SslProxyTunnel`/`ProxyTunnel` (CONNECT), `SocksTunnel`/`SslSocksTunnel`. ⇒ A delegating `IWebProxy` whose returned URI changes routes new requests to a different pool; in-flight requests finish on their old connections; old pools' idle connections are scavenged (`PooledConnectionIdleTimeout` default 1 min, `HttpHandlerDefaults.cs:29`; the app also sets `PooledConnectionLifetime` 5 min) and empty pools removed by `RemoveStalePools`. Switching to "no proxy" (`GetProxy` → `null`) uses the direct `Https` pool. This is correct and race-free from the handler's point of view.
- **Gotchas for a delegating proxy:**
  1. **Credentials are captured once**: `_proxyCredentials = _proxy.Credentials ?? settings._defaultProxyCredentials` in the pool-manager ctor; pools use `_poolManager.ProxyCredentials` (`SHH/ConnectionPool/HttpConnectionPool.cs:291, 364-386`). So the wrapper's `Credentials` must return a stable, non-null delegating `ICredentials` whose `GetCredential(proxyUri, authType)` resolves against the *current* choice; swapping the wrapper's `Credentials` later has no effect.
  2. **Exceptions from `IsBypassed`/`GetProxy` are swallowed and treated as "no proxy"** (`HttpConnectionPoolManager.cs:405-410`) — a throwing wrapper silently goes direct.
  3. Returned URI scheme must be `http`, `https`, `socks4`, `socks4a`, `socks5` (`SHH/HttpUtilities.SocketsHttpHandler.cs:24-30`), else `NotSupportedException`.
  4. Forward `IsBypassed` as well as `GetProxy` (`WebProxy.GetProxy` returns the *destination* URI when bypassed; the handler always calls `IsBypassed` first, so forwarding both keeps them consistent).
  5. Wrapping `HttpClient.DefaultProxy` loses the internal `IMultiWebProxy` fail-over (PAC with several `PROXY` entries): the wrapper only gets the first proxy via `HttpWindowsProxy.GetProxy` (`ReadNext` once). Single-proxy setups (Clash/v2rayN) are unaffected.
- **Alternative — recreate handler/HttpClient on change:** works, but the `HttpClient` is a DI singleton handed to `ReleaseFeed`/`UpdateService`; swapping requires an indirection anyway, disposing the old client aborts in-flight downloads, and it gains nothing for the system mode (the new handler would reuse the same `HttpClient.DefaultProxy`).
- **"No proxy" mode**: wrapper returns `IsBypassed => true` / `GetProxy => null` (equivalent of `UseProxy = false`, which can't be toggled post-start).

## 4. `WebProxy` (manual proxy)

Source `RT/System.Net.WebProxy/src/System/Net/WebProxy.cs`.
- `http://host:port` proxy for `https://` targets → `HttpConnectionKind.SslProxyTunnel` = HTTP `CONNECT` tunnel then TLS to origin (`HttpConnectionPoolManager.cs:288-291`, `HttpConnectionPool.cs:772-781`). Standard, fine. Plain `http://` targets use absolute-URI forwarding (`Kind.Proxy`).
- Address string without scheme gets `http://` prepended (`CreateProxyUri`). `https://` proxy URIs (TLS to the proxy) are also supported (scheme check above).
- `IsBypassed = Address == null || (BypassProxyOnLocal && IsLocal(host)) || IsMatchInBypassList(host)`. `BypassProxyOnLocal` = loopback / local addresses / dot-less host names. `BypassList` entries are **regex strings** (`new Regex(..., IgnoreCase | CultureInvariant)`).
- `Credentials` (e.g. `new NetworkCredential(user, pass)`): used on 407 challenges via `AuthenticationHelper.SendWithProxyAuthAsync` (Basic/Digest) and `SendWithNtProxyAuthAsync` (NTLM/Negotiate) for `Proxy`/`ProxyConnect` pools, i.e. also for the CONNECT request. Basic over an `http://` proxy sends the password in clear to the proxy.
- **SOCKS**: SOCKS4/4a/5 supported since .NET 6 — PR [dotnet/runtime#48883](https://github.com/dotnet/runtime/pull/48883) (merged 2021-04-22, milestone 6.0.0). `new WebProxy("socks5://127.0.0.1:7891")` works; SOCKS5 username/password from `proxyCredentials?.GetCredential(proxyUri, proxyUri.Scheme)` (`SHH/SocksHelper.cs:38`). socks4/4a ignore passwords. Remote DNS: socks4a/socks5 send the host name.

## 5. AOT / trimming

- `HttpClient`, `SocketsHttpHandler`, `IWebProxy`, `WebProxy`, `NetworkCredential`, `HttpClient.DefaultProxy` carry no `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` (grep of the release/10.0 sources above found none). No reflection involved.
- `HttpWindowsProxy` uses framework-internal P/Invokes (winhttp.dll, advapi32 `RegNotifyChangeKeyValue`) — part of System.Net.Http's Native AOT support, nothing for the app to configure.
- `WebProxy.BypassList` builds `Regex` at runtime → runs on the regex interpreter under Native AOT (works, no warning; pulls in System.Text.RegularExpressions if used). Avoidable by not setting `BypassList` (or doing bypass matching in the custom `IWebProxy`).
- A custom `sealed class : IWebProxy` is trivially AOT-safe.

## Recommended approach (summary)

Keep the single `SocketsHttpHandler`/`HttpClient`; set `Proxy = <SwitchableProxy>` once at construction. `SwitchableProxy : IWebProxy` holds a volatile reference to the current target: `HttpClient.DefaultProxy` (System — live on Windows .NET 9+), a "none" sentinel (`IsBypassed => true`, `GetProxy => null`), or a `WebProxy(new Uri("http://host:port")) { BypassProxyOnLocal = true, Credentials = ... }` (Manual). Forward `IsBypassed`/`GetProxy` to the current target and expose a fixed, never-null delegating `ICredentials` that forwards to the current target's credentials. Settings changes just swap the reference; no handler/HttpClient rebuild, no restart.

## External References

- MS Learn — [HttpClient.DefaultProxy remarks](https://learn.microsoft.com/dotnet/api/system.net.http.httpclient.defaultproxy?view=net-10.0) — platform rules, env var names/case/format, socks schemes.
- [dotnet/runtime#103364](https://github.com/dotnet/runtime/pull/103364) — Windows proxy registry change notification (.NET 9).
- [dotnet/runtime#48883](https://github.com/dotnet/runtime/pull/48883) — SOCKS4/4a/5 in SocketsHttpHandler (.NET 6).
- Win32 — [WinHttpGetIEProxyConfigForCurrentUser](https://learn.microsoft.com/windows/win32/api/winhttp/nf-winhttp-winhttpgetieproxyconfigforcurrentuser) — the per-user IE/WinINet config that .NET reads.
- Source (release/10.0): `SHH/HttpWindowsProxy.cs`, `SHH/WinInetProxyHelper.cs`, `SHH/SystemProxyInfo*.cs`, `SHH/HttpEnvironmentProxy*.cs`, `SHH/MacProxy.cs`, `SHH/HttpConnectionPoolManager.cs`, `SHH/ConnectionPool/HttpConnectionPool.cs`, `SHH/SocksHelper.cs`, `SHH/SocketsHttpHandler.cs`, `SHH/HttpUtilities.SocketsHttpHandler.cs`, `RT/System.Net.Http/src/System/Net/Http/HttpClient.cs`, `RT/System.Net.Requests/src/System/Net/WebRequest.cs`, `RT/System.Net.WebProxy/src/System/Net/WebProxy.cs`, `RT/Common/src/System/Net/Http/HttpHandlerDefaults.cs`.

## Related Specs

- `.trellis/spec/desktop/preferences.md` — preferences persistence contract (per PRD).

## Caveats / Not Found

- Live-update behavior verified from source only, not by running the app while toggling Clash. The notification watches the whole `Internet Settings` subtree, which includes `Connections\DefaultConnectionSettings` written by `InternetSetOption`; tools that only broadcast without writing the registry would not be seen (none known).
- `HttpWindowsProxy.GetProxy` reads fields swapped by `UpdateConfiguration` without a lock; a request racing a change may briefly see old/mixed values (harmless).
- Whether `WinHttpGetIEProxyConfigForCurrentUser` returns `lpszProxy == NULL` when "Use a proxy server" is off (ProxyEnable=0) is per WinHTTP docs, not re-verified empirically here.
- Could not pin the exact .NET version that added `https://` proxy URI support (it is present in release/10.0).
