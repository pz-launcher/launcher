# Error Handling

> Expected failures are values (`Result<T>`). Exceptions are for bugs and
> cancellation only.

---

## Types

Reference: `PzLauncher.Core/Common/` → `Result.cs`, `Error.cs`, `ErrorKind.cs`, `ErrorCode.cs`.

Hand-written on purpose. Don't add a Result/OneOf/FluentResults package.

| `ErrorKind` | Use for | Future HTTP |
|---|---|---|
| `Validation` | bad input | 400 |
| `NotFound` | thing doesn't exist | 404 |
| `Conflict` | state doesn't allow it | 409 |
| `External` | Steam files, SteamCMD, OS, network, child process failed | 502 |
| `Unexpected` | caught at a boundary, unclassified | 500 |

## Error codes

All codes are members of `ErrorCode` (`PzLauncher.Core/Common/ErrorCode.cs`).

- Wire name `<feature>.<snake_case_reason>` is a **public contract**: never change one.
- Logs use the wire name (`ToWireName()`), never `ToString()`.
- Each feature builds errors in one factory class, e.g. `PzLauncher.Core/Installs/InstallErrors.cs`.
- `Message` is for developers and logs. Users see text from `Code` + `Details` via the desktop `ErrorMessages`.

Adding a code:

1. Add the member to `ErrorCode` with its wire name.
2. Add a factory method in `<Feature>Errors`.
3. Add user text: an `Error_<Member>` key in every `UiText` resx and its arm in `PzLauncher.Desktop/Localization/ErrorMessages.cs` (the build fails until you do; see `.trellis/spec/desktop/localization.md`).
4. Pin the wire name and the factory's `Kind` in `PzLauncher.Core.Tests/Common/ErrorCodeTests.cs`.

## Where exceptions are caught

| Exception | Rule |
|---|---|
| `OperationCanceledException` | **Never** turn into a Result. Let it propagate. |
| I/O, access, parse errors from external data (incl. a parser library's documented exception type) | Catch at the Core service boundary, nearest the source, convert to a specific `Error`, log once at Warning. |
| `ArgumentException`, `InvalidOperationException`, `NullReferenceException` from our code | Bugs. Don't catch. |

## External data is untrusted

Files we don't own (Steam `.vdf`/`.acf`, later server `.ini`, mod metadata) can
be corrupt. Corrupt input becomes an `Error` or warning, never an uncaught exception.

| Hazard | Rule | Reference |
|---|---|---|
| Invalid path chars, relative paths | Validate with `PathNormalizer.TryNormalizeAbsolute` before use | `PzLauncher.Core/Common/PathNormalizer.cs` |
| Deep nesting → uncatchable stack overflow | Parsers must be non-recursive or depth-limited; verify third-party parsers in a separate process | `PzLauncher.Core/Steam/SteamKeyValues.cs`; findings in the task's `research/valvekeyvalue.md` |
| One bad entry in a list | Skip it with a warning, keep the rest | `InstallDetectionService.ScanAsync` |

Each hazard has a test that feeds bad input and asserts the code.

## Partial failure

If an operation can partly succeed, return success with a `Warnings` list.
`Fail` only when there's no useful result. Reference: `InstallScanResult`.

## Common mistakes

- Creating a `Result` with `default`/`new()`. Use `Ok`/`Fail` or the implicit conversions.
- Throwing for an expected case from a Result-returning method.
