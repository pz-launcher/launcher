# Implement: resx localization

## Checklist

1. **Resources** — add `Lang/UiText.resx` (English) with every key from prd.md R1; csproj `EmbeddedResource Update` with `MSBuild:Compile`, `PublicClass=true`, namespace `PzLauncher.Desktop.Lang`, class `UiText`. Add `UiText.zh-Hans.resx` and `UiText.zh-Hant.resx` (Taiwan wording). Build to confirm the generated class is public.
2. **Language model** — `AppLanguage.cs` (enum, culture name, native name, parse, `FromCulture`). Tests: AC3.
3. **LocalizationService** — interface + implementation (only writer of `UiText.Culture`). Add the non-parallel `UiTextCulture` test collection with a fixture/helper that sets and restores the culture.
4. **ErrorMessages + DisplayNames** — move texts to `UiText`, keep the exhaustive switch; add display names and `FuncValueConverter`s for `PzInstallKind` / `PzInstallSource`. Tests: AC2 (codes × languages), AC6, AC1 (resx key parity via `UiText.ResourceManager.GetResourceSet(culture, true, false)` per language vs. invariant).
5. **Preference store** — `ILanguagePreferenceStore`, `JsonLanguagePreferenceStore`, `DesktopJsonContext`. Tests: AC4 with `TempDirectory`-style helper in Desktop.Tests.
6. **ViewModels** — `MainViewModel` (options, selection, save); `InstallsViewModel` keeps error/warning sources and re-renders on `LanguageChanged`, picker title from `UiText`. Update `DesignData` and `Fakes.cs` (fake store, use real `LocalizationService`). Tests: AC5, selection calls `SetLanguage` + save.
7. **Views + App** — `MainView` (top bar + page) hosted by `MainWindow`; `InstallsView` literals → `x:Static`, converters; `App` startup resolution, DI registrations, `RebuildMainView` (swap `MainWindow.Content`), localized error dialog.
8. **Validate** — commands below, then manual smoke AC8.
9. **R7 (revised: PZ regional options)** — replace `Spanish`/`Portuguese` with `SpanishSpain`, `SpanishMexico`, `SpanishChile`, `SpanishArgentina`, `PortuguesePortugal`, `PortugueseBrazil` (keep `Russian`, last). `git mv` `UiText.es.resx` → `UiText.es-MX.resx` and `UiText.pt.resx` → `UiText.pt-BR.resx` (current wording already fits), add `es-ES`, `es-CL`, `es-AR`, `pt-PT` with regional wording; update `FromCulture` and AC3 tests; update localization.md "Adding a language" (regional cultures allowed when mirroring PZ); rerun build/test and the UIA smoke for all ten options.

## Validation

```bash
dotnet build PzLauncher.slnx -warnaserror
dotnet test PzLauncher.slnx
dotnet run --project PzLauncher.Desktop
```

## Risky points

- Step 7 content rebuild: verify no window flash, no rescan, and the old `MainView`'s ComboBox doesn't push a stray selection when detached.
- `StringFormat={x:Static …}` may not compile; fallback is a converter (design.md, Resources).
- CJK glyph rendering (design.md, Risks) — checked in smoke only.

## Before `task.py start`

- `implement.jsonl` / `check.jsonl` curated.
- After finishing: write `.trellis/spec/desktop/localization.md` (resx layout, key naming, `UiText.Culture` single-writer rule, test collection) and update `.trellis/spec/desktop/index.md` and `directory-structure.md` (Localization folder now holds more than ErrorMessages).
