# Implement: shared HTTP client with proxy settings

Branch: `feat/auto-update` (D3; ships in PR #11).

## Checklist

1. Core `Net/`
   - Move `Update/UpdateHttpClient.cs` → `Net/LauncherHttpClient.cs`; update `UpdateService`, `ReleaseFeed`, DI, tests (`UpdateTestKit`).
   - `ProxyMode`, `ManualProxy`, `ProxySettings`, validation, `SwitchableProxy` + `ForwardingCredentials` (AC2, AC3).
   - `NetworkErrors` + `ErrorCode` members + `ErrorCodeTests`; `Error_*` resx keys + `ErrorMessages` arms.
   - `INetworkService` / `NetworkService` with `ApplyProxy`, `TestConnectionAsync` (AC4, fake handler, cancellation test); DTOs in `CoreJsonContext` + round-trip tests.
2. Desktop persistence
   - `ISecretProtector` (Windows DPAPI via LibraryImport, plain on Linux) + tests.
   - `proxy` preference field, `SaveProxyAsync`, `JsonPreferenceStoreTests` matrix (AC5); spec table.
3. Desktop UI
   - Apply saved proxy at startup in `App.axaml.cs` before the update coordinator starts.
   - `SettingsSection.Proxy`, `ProxySettingsViewModel` (debounced apply/save, validation, test), `ProxySettingsView`, sidebar entry after Updates; `DisplayNames` for `ProxyMode`.
   - Flush pending proxy save on close.
   - VM tests (AC6).
4. Text in all 10 resx files; spec updates (`core/directory-structure.md` Net/, `desktop/preferences.md`).

## Validation

```bash
dotnet build PzLauncher.slnx -c Release
dotnet test PzLauncher.slnx -c Release
dotnet publish PzLauncher.Desktop/PzLauncher.Desktop.csproj -c Release -r win-x64 -p:PublishAot=true -p:Version=0.1.0 -o <scratch>/publish-proxy
```

AOT publish needs `C:\Program Files (x86)\Microsoft Visual Studio\Installer` on PATH (vswhere). Then AC8 manual smoke.

## Risky points

- `SwitchableProxy` must never throw and its `Credentials` must be stable (research §3).
- DPAPI P/Invoke under AOT: use `LibraryImport` with blittable `DATA_BLOB`; free the output with `LocalFree`.
- Banned APIs: OS checks via `IPlatformEnvironment`; timers via `TimeProvider`.
- Tests stay offline.
