# Shared HTTP client with proxy settings

## Goal

Network features of the launcher share one HTTP layer that respects the user's proxy: by default the operating system's proxy, or no proxy, or a manually configured HTTP proxy set on a dedicated Settings page. Users behind a proxy, or using a local proxy tool to reach GitHub, can update the launcher, and future network features get the same behavior for free.

Request (verbatim): http做成通用的，并且支持系统代理。设置页面做个专门的代理配置可以配置http代理。

## Background (repository evidence)

- The only HTTP code is the auto-update feature on branch `feat/auto-update` (PR #11, open): `PzLauncher.Core/Update/UpdateHttpClient.cs` builds one `HttpClient` on a `SocketsHttpHandler` (redirects on, 5 min pooled lifetime, 15 s connect timeout, infinite client timeout, `User-Agent: PzLauncher/<version>`), internal to `Update/`, used by `ReleaseFeed` and `UpdateService`, registered in `PzLauncher.Core/ServiceCollectionExtensions.cs:37`. It sets no proxy, so it already uses .NET's default system proxy, with no way to change it.
- Release API URLs live in `PzLauncher.Core/Update/LauncherReleases.cs` (GitHub `api.github.com/repos/pz-launcher/launcher/releases/latest`, Gitee `gitee.com/api/v5/repos/dontcampy/pzl/releases/latest`).
- Settings sidebar (`PzLauncher.Desktop/Views/Settings/SettingsView.axaml`): Launcher group = Appearance, Updates; Game group = Java. Section views are separate UserControls (`UpdateSettingsView`, `JavaSettingsView`).
- Preferences: `.trellis/spec/desktop/preferences.md` (nullable fields, own save method, stable strings; values typed per keystroke debounce their write, e.g. `InstallOverridesService` 300 ms). File: `<ApplicationData>/PzLauncher/preferences.json`, plain JSON.
- .NET proxy behavior (`research/dotnet-proxy.md`, from dotnet/runtime `release/10.0` source, not tested live):
  - Windows: the default proxy reads the current user's Internet Options proxy (what Clash/v2rayN toggle), including bypass list, WPAD and PAC; since .NET 9 it follows changes while the app runs. If `HTTP_PROXY`/`HTTPS_PROXY`/`ALL_PROXY` is set, those win.
  - Linux: only `http_proxy`/`https_proxy`/`all_proxy`/`no_proxy` env vars; desktop (GNOME/KDE) settings are not read.
  - A handler's `Proxy` can't change after its first request, but its `IWebProxy` is asked on every request and connection pools are keyed by the returned proxy URI, so a switchable `IWebProxy` gives runtime switching. Its `Credentials` object is read once, so it must be a stable forwarder.
  - `WebProxy` supports an `http://` proxy for HTTPS targets (CONNECT), `BypassProxyOnLocal`, and Basic/Digest/NTLM credentials.

## Decisions

- D1 Modes: System proxy (default) / No proxy / Manual HTTP proxy (address + port). A change takes effect immediately, no restart. A "Test connection" button requests GitHub and Gitee and shows each result separately.
- D2 Manual proxy has optional username and password. Windows saves the password encrypted with DPAPI (current user); Linux saves it in plain text in preferences.json, and the proxy page says so.
- D3 Delivery: commits go on `feat/auto-update` and ship in PR #11.

Technical defaults (in design.md; listed because users can observe them):

- D4 Proxy is a new Settings section, "Proxy", in the Launcher group after Updates.
- D5 Manual address accepts a host name or IP, optionally prefixed with `http://`; port 1–65535. While the manual fields are invalid, an inline error shows and the last valid setting stays in effect and saved. Typing is applied and saved after a short debounce.
- D6 Local addresses (`localhost`, loopback, plain host names) bypass the manual proxy.
- D7 The System option explains its source per OS: Windows "Internet Options / system proxy settings" (environment variables override it); Linux "the `http_proxy`/`https_proxy` environment variables".
- D8 A saved DPAPI password that can't be decrypted (profile moved, file copied to another machine) loads as empty and is logged; the user re-enters it.
- D9 Test connection uses the settings currently in effect, a 15 s timeout per site, and shows per site "OK (n ms)" or the error text. It runs both sites at once.

## Requirements

- R1 One shared HTTP client in Core (`Net/`) used by every Core network feature; `Update/` moves onto it. Per-request timeouts stay with callers.
- R2 Proxy modes per D1 switch at runtime without recreating the client; requests already running finish on their old route.
- R3 Manual mode uses an `http://host:port` proxy with optional credentials (D2) and local bypass (D6).
- R4 Core exposes a host-facing proxy API: apply settings, read current settings, test connection (D9) returning a per-target result.
- R5 Desktop persists the proxy settings (D2, D5, D8), applies them before the first network request at startup (so the startup update check already uses them), and offers the Settings page (D4, D7).
- R6 New user-facing text in every `UiText*.resx`; new error codes follow `.trellis/spec/core/error-handling.md`.

## Acceptance Criteria

- AC1 (R1) `UpdateService`/`ReleaseFeed` use the shared client; no other `HttpClient` is constructed in Core or Desktop.
- AC2 (R2) Unit tests on the switchable proxy: System returns what the system proxy returns; None bypasses every URI; Manual returns `http://host:port`; switching changes the answer for the next request; credentials forwarder returns the current manual credentials (or none).
- AC3 (R3, D6) Manual proxy bypasses `localhost` and `127.0.0.1`.
- AC4 (R4, D9) Test connection with a fake handler: both OK, one failing, both failing, timeout → per-target results with the right error code.
- AC5 (R5, D2, D8) Preference tests: round-trip, missing, unknown mode, keeps other fields, unwritable file; Windows round-trip of a DPAPI-protected password (skipped off Windows); undecryptable password loads as empty; Linux stores plain text.
- AC6 (R5, D5) ViewModel tests: invalid port/host shows an error and doesn't apply or save; a valid edit applies and saves after the debounce; mode change applies immediately; saved settings are applied at startup before the update check.
- AC7 (R6) Resx parity and error-code tests pass; `dotnet build` (warnings as errors), `dotnet test PzLauncher.slnx` and a `win-x64` Native AOT publish pass with no new warnings.
- AC8 Manual smoke (Windows): with Clash/v2rayN on as system proxy, System mode passes Test connection and the update check works; No proxy mode bypasses it; Manual mode pointed at `127.0.0.1:<port>` passes; Manual pointed at a closed port fails Test connection for both sites; switching takes effect without restart.

## Out of Scope

- SOCKS proxies, HTTPS-scheme proxies, PAC URL entry, custom bypass lists.
- Reading GNOME/KDE proxy settings on Linux; a Linux keyring for the password.
- Proxying the game itself or Steam.
