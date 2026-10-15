# Test Strategy

> xUnit v3 for every test project. Packages: see the test `.csproj` files.

---

## Projects

| Project | Covers |
|---|---|
| `PzLauncher.Core.Tests` | Core services, parsers, Result/Error |
| `PzLauncher.Desktop.Tests` | Desktop ViewModels, localization, desktop services |

- Use `xunit.v3.mtp-off`, not `xunit.v3`: the .NET 10 SDK rejects xunit.v3 4.x (Microsoft.Testing.Platform) in VSTest mode.
- xUnit `Assert` only. No assertion or mocking libraries; hand-written fakes.
- Test projects aren't AOT.

## Levels

| Level | How | Reference |
|---|---|---|
| Parsers, pure logic | Unit tests; real-world samples as fixture files | `PzLauncher.Core.Tests/Fixtures/` |
| Core services | Fake on-disk world in a temp directory + fake platform | `PzLauncher.Core.Tests/TestSupport/` |
| ViewModels | Fake Core and UI services; assert state transitions | `PzLauncher.Desktop.Tests/TestSupport/Fakes.cs` |
| Headless UI | Deferred | — |
| Manual smoke | Listed in each task's `prd.md` | — |

Never touch the developer's real Steam install, home directory, or registry.

## Rules

- Every error code a service can return has a test asserting its `Code`. Never `Message`. `Kind` is asserted once per code, in the factory table in `PzLauncher.Core.Tests/Common/ErrorCodeTests.cs`.
- Checks over all codes enumerate `ErrorCode`, not a hand-kept list.
- Keep tests concise: shared arrange/assert helpers live in `TestSupport/` (`PzLauncher.Core.Tests/TestSupport/ResultAssert.cs`, `SteamLayoutBuilder.cs`; `PzLauncher.Desktop.Tests/TestSupport/Fakes.cs`); near-duplicate Facts become one Theory; each behaviour has one owner, the lowest layer that implements it, and higher layers don't re-pin it.
- Every partial-failure path asserts its warnings; long-running services have a cancellation test.
- Names: `Method_Scenario_Expected`. Test folders mirror source folders.
- Deterministic and offline: no network, no real Steam, no wall clock (`FakeTimeProvider`).
- OS-specific tests skip on other OSes; portable logic runs everywhere.
