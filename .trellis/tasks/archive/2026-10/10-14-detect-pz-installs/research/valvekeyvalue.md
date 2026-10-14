# Research: ValveKeyValue as the VDF/ACF parser

Package: `ValveKeyValue` 0.71.0.528 (NuGet, MIT, by the ValveResourceFormat /
SteamDB people). Targets `net10.0` only, no dependencies, and its csproj sets
`<IsAotCompatible>true</IsAotCompatible>`. Source checked at the commit
the package was built from (`ffb04c9c563c1604eaca2e0d1f99afd74b89e684`).

We use it only through `PzLauncher.Core/Steam/SteamKeyValues.cs`:

```csharp
KVSerializer.Create(KVSerializationFormat.KeyValues1Text)
    .Deserialize(stream, new KVSerializerOptions { HasEscapeSequences = true });
```

Findings were checked in a throwaway console app (scratchpad, separate
process), against the library source, and are now pinned in
`PzLauncher.Core.Tests/Steam/SteamKeyValuesTests.cs`.

## 1. Escape sequences

- The option is `KVSerializerOptions.HasEscapeSequences`. It defaults to
  **false**. With it off, `"C:\\Program Files (x86)\\Steam"` comes back with
  doubled backslashes. We set it to `true`. The fixture test now gets
  `C:\Program Files (x86)\Steam` and `E:\SteamLibrary`.
- Supported escapes: `\n \t \v \b \r \f \a \\ \? \' \"`.
- An **unknown escape** (e.g. `\q`) throws `KeyValueException`, so we report
  `steam.vdf_malformed`. The old parser kept `\q` as written. The library's
  `EnableValveNullByteBugBehavior` would instead truncate the string at that
  point, silently. We don't turn it on. Steam always writes `\\`, so this
  shouldn't affect real files.

## 2. Case sensitivity

The `KVObject` string indexer and `TryGetValue` are **case-sensitive**
(`TryGetValue("userconfig")` on a `UserConfig` key returns false). The
indexer also **throws** `KeyNotFoundException` for a missing key. So the
wrapper has its own lookups, `GetChild` / `GetString` / `GetChildren`, which
compare with `OrdinalIgnoreCase`. Code must not use the indexer.

## 3. Duplicate keys, comments, BOM, conditionals, unquoted tokens

| Behaviour | ValveKeyValue | Wrapper result |
|---|---|---|
| Duplicate keys | KV1 collections are list-backed and keep **all** entries. The indexer returns the **first** one. | `GetChild` returns the **last** match (case-insensitive); `GetChildren` collapses repeats, last value at first position. Same as the old `VdfNode`. |
| `//` comments | Supported. A **single** `/` also starts a comment (a quirk of Valve's parser). | Same. Only matters for unquoted tokens that contain `/`, which Steam files don't have. |
| UTF-8 BOM | The `StreamReader` strips a leading BOM. A U+FEFF *in the middle* of the text is a syntax error. | `File.ReadAllTextAsync` already strips it. A leading `\uFEFF` in a string still parses (test). The old parser skipped U+FEFF anywhere. |
| Conditionals `[$X]` | Evaluated. Defaults: `WIN32` is always true, plus `WINDOWS`, or `LINUX`+`POSIX`, or `OSX`+`POSIX` for the current OS. A pair whose condition is false is **dropped**. | `[$WIN32]` kept on every OS, `[!$WIN32]` dropped (test). The old parser ignored conditionals and kept everything. Steam's own files don't use them. |
| Unquoted tokens | Supported. | Same. |
| Multiple top-level keys | Error ("documents with multiple root objects are not supported"). | `steam.vdf_malformed`. The old parser accepted them. Steam files always have exactly one root. |
| `#include` / `#base` | Without a `FileLoader` the library throws `KeyValueException`. It never opens files itself. | `steam.vdf_malformed`. Parsing stays read-only and never touches the file system. |

## 4. Deep nesting / stack overflow

The KV1 text reader is a loop over tokens with an explicit state stack
(`KV1TextReaderStateMachine`), and `KVObjectBuilder` uses a stack too. It
doesn't recurse. Tested in a separate process: `"k" {` nested 64, 1,000,
10,000, 100,000 and **1,000,000** levels deep all parsed, under JIT and
under Native AOT. Unclosed nesting at 1,000,000 levels gives a clean
`KeyValueException`. **No depth pre-check is needed.** The old
`MaxDepth = 64` test was replaced with
`Parse_VeryDeepNesting_DoesNotOverflowTheStack` (100,000 levels) and
`Parse_VeryDeepUnclosedNesting_ReturnsVdfMalformed`. If a future version
started recursing, the first test would crash the test host, which is loud
enough to catch it on upgrade.

## 5. Malformed input → which exceptions

From the source, every syntax error in the KV1 text path is thrown as
`KeyValueException` (`MakeSyntaxException`, the builder's "not in a fully
completed state", include errors). `EndOfStreamException` from the tokenizer
is caught inside the reader and wrapped in `KeyValueException`. Other throws
are argument null checks or KV3/array-only paths that KV1 text doesn't reach.

Fuzzing: 500,000 inputs (random mutations of both fixtures plus random
strings over `{}"\/[]$!&|()#`, newlines, NUL, BOM and a lone surrogate) gave
only `OK` (51,205) or `KeyValueException` (448,795). No other exception type
came out. So the wrapper catches **only** `KeyValueException`.

Every malformed case from the old `VdfParserTests` returns
`steam.vdf_malformed`: empty text, comment only, missing `}`, unterminated
string, extra `}`, `{` where a key should be, key with no value, lone key.
New cases were added for: two roots, unknown escape, unterminated
conditional, `#base`, `#include`, and `#include` that isn't at the start of
the file.

## 6. AOT / trimming

- `dotnet build PzLauncher.slnx -warnaserror`: 0 warnings (Core and the app
  both have `IsAotCompatible=true`, so the trim/AOT analyzers run).
- `dotnet publish PzLauncher.Desktop/PzLauncher.Desktop.csproj -c Release -r win-x64
  -p:PublishAot=true`: succeeds with **0 warnings** and no IL2026, IL3050,
  IL2104 or other IL warnings mentioning ValveKeyValue. In Git Bash the
  first attempt failed at the native link step only because `vswhere.exe`
  wasn't on `PATH`. It passed once `C:\Program Files (x86)\Microsoft Visual
  Studio\Installer` was added to `PATH` for that command. Nothing was
  installed.
- The scratch probe app was also published with Native AOT and run. Parsing,
  escapes, deep nesting and the fuzz loop all behave the same as under JIT.
- We never call `KVSerializer.Deserialize<T>` (reflection-based, annotated
  with `DynamicallyAccessedMembers`). Only the `KVObject` API is used.

## Other behaviour differences vs the old hand-written parser

- **Numeric-looking values are converted.** `KV1TextReader.ParseValue` turns a
  value into `int` when `int.TryParse` (leading whitespace and a sign
  allowed) succeeds, into `float` when it looks like a decimal or exponent
  (`1.5`, `1e5`), and into `ulong` for exactly `0x` + 16 hex digits. The
  original text isn't kept. `AsText()` formats with the invariant culture, so:
  - plain integers (`appid`, `buildid`, `"0"`, `"-1"`) and integers too big
    for `int` (kept as strings, e.g. `SizeOnDisk`) come back exactly as
    written;
  - `"007"` → `"7"`, `" 5"`/`"+5"` → `"5"`, `"1.0"` → `"1"`, `"1.50"` →
    `"1.5"`, `"1e5"` → `"100000"`, `"3.4028236E+38"` → `"Infinity"`.

  For PZ this only matters if a **beta branch name** (`BetaKey`) or
  `installdir` looks like a number with formatting that doesn't survive
  (`42.0`, `007`). Word-like branch names such as `unstable` aren't affected.
  The library has no option to turn this off. If it ever matters, the raw
  token text is available through `KVSerializer.DeserializeWithSourceMap`.
- Conditionals are evaluated instead of ignored, more than one top-level key
  is an error, U+FEFF is only allowed at the start, unknown escapes are
  errors. See the tables above.
