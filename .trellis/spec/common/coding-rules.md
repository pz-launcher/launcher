# Coding Rules

> Language-level rules for all C# in the solution. Every project sets
> `TreatWarningsAsErrors`; diagnostic severities live in `.editorconfig`, not here.

---

## Comments

Comments explain **why**, never **what**. Default to none.

- Write one only for: external-format/OS quirks, gotchas a reader would "fix", contracts not visible in types, reasons for a `#pragma` or unusual ordering.
- One line where possible; longer explanations go to the task's design.md or research notes.
- `/// <summary>` only on public members whose name doesn't say it, one sentence.
- No prose listing errors, params, or returns. No comments in tests beyond a quirk the test relies on.

```csharp
// Wrong — restates the signature
/// <summary>Returns the child with this key, or null if it's missing.</summary>
public static KVObject? GetChild(this KVObject node, string key)
```

Reference for the right density: `PzLauncher.Core/Steam/SteamKeyValues.cs`.

## Nullability

Short-circuit `null` where it enters; everything downstream is non-null. Each
`?` carries one real meaning ("absent", "default branch"), never "not
validated yet" or "error".

| Situation | Rule | Reference |
|---|---|---|
| Public method, non-null param | `ThrowIfNull` at entry; helpers below take non-null types | `InstallDetectionService.InspectAsync` |
| OS / registry / env / file value | Nullable only until the first validation | `SteamLocator.GetCandidateRoots` |
| Value or failure reason | `Result<T>`, not a tuple of nullables | `SteamLibraryReader.ReadAsync` |
| Fields that are null together | One nullable sub-record; derive flags from it | `PzInstall` |
| Collections on DTOs | Non-null, empty by default | `Error.Details` |
| Value set at startup and used right away | Local or constructor parameter, not a nullable field | `PzLauncher.Desktop/App.axaml.cs` |
| Service with several outcomes | Explicit result type; `null` at most means "cancelled" | `PzLauncher.Desktop/Services/FolderPickResult.cs` |

- No `!` in production code. Tests use it only on genuinely nullable APIs or to feed a null guard.

```csharp
// Wrong — two nullables encode one either; callers need `!`
Task<(string? Text, string? Reason)> TryReadAsync(string path, CancellationToken ct);
```

## Async

- Async methods that can take time take `CancellationToken ct = default`.
- App code never calls `ConfigureAwait`.
- No `async void` except event handlers. Fire-and-forget only on `Task`-returning methods.
- Use async I/O APIs, not sync calls in `Task.Run`.

## AOT and trimming

Core and the app are `IsAotCompatible`; IL2xxx/IL3xxx warnings fail the build.

- No reflection: JSON via source generation (`CoreJsonContext`), no `Activator`, no assembly scanning.
- New packages: check for trim/AOT warnings and use only their reflection-free APIs.

## Types and naming

- Public classes and records `sealed` or `static` unless designed for inheritance; Core types `internal` unless a host needs them.
- Named constants for domain values (e.g. `PzSteamApps`). No static mutable state.
- Switches over an enum list every member with no `_ =>` arm: a discard silences CS8509 (an error per `.editorconfig`), so a new member would compile without a case.
