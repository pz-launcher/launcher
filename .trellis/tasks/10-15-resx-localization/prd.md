# Add resx localization (en / zh-Hans / zh-Hant / es / ru / pt)

## Goal

All user-facing text in the desktop app comes from resx resources in six languages: English, Simplified Chinese, Traditional Chinese, Spanish, Russian, Portuguese. The user can switch language from the UI and the switch takes effect immediately; the choice is remembered across restarts. On first launch the language follows the OS UI language. Reference: https://docs.avaloniaui.net/docs/app-development/localizing

## Background (repository evidence)

- Avalonia 12.1.4, net10.0, `IsAotCompatible=true` (`PzLauncher.Desktop/PzLauncher.Desktop.csproj`); every view uses compiled bindings (`x:DataType`).
- No resx exists; all UI text is hard-coded English:
  - `PzLauncher.Desktop/Localization/ErrorMessages.cs`: user text per `ErrorCode` plus `FolderNotLocal`. The exhaustive switch makes a new `ErrorCode` without text a build error (CS8509).
  - `PzLauncher.Desktop/Views/Installs/InstallsView.axaml`: "Installs", "Rescan", "Add folder…", the empty-state text, `StringFormat='branch: {0}'` / `'build {0}'`; `PzInstallKind` (`Game`, `DedicatedServer`) and `PzInstallSource` (`SteamLibrary`, `Manual`) render as raw enum names.
  - `PzLauncher.Desktop/ViewModels/Installs/InstallsViewModel.cs`: folder picker title.
  - `PzLauncher.Desktop/App.axaml.cs`: unexpected-error dialog text and "OK".
  - `PzLauncher.Desktop/Views/MainWindow.axaml`: title "PzLauncher" (product name, not translated).
- `InstallsViewModel` stores already-rendered strings (`ErrorText`, `Warnings`).
- No settings/persistence layer and no navigation shell exist; `MainWindow` only hosts the current page.
- Avalonia docs: `{x:Static}` resolves once at load and does not follow culture changes; the generated resource class must be `public` (`MSBuild:Compile` + `PublicClass=true`).
- Specs: `.trellis/spec/desktop/index.md` lists localization as not yet written; `.trellis/spec/common/coding-rules.md` forbids static mutable state (see design.md for the one deliberate exception).

## Requirements

- **R1 Resources.** A default (English) resx plus `zh-Hans` and `zh-Hant` resx cover every user-facing string listed above, including error texts, the `branch` / `build` formats, and display names for `PzInstallKind` and `PzInstallSource`. "PzLauncher" stays untranslated. Traditional Chinese uses Taiwan-standard wording.
- **R2 Default language.** With no saved preference: OS UI culture in the `zh-Hant` family (`zh-Hant`, `zh-TW`, `zh-HK`, `zh-MO`) → Traditional Chinese; any other `zh*` → Simplified Chinese; any culture that matches no supported language (including invariant) → English. English is also the resource fallback: a key missing from a Chinese resx shows the English text.
- **R3 Persistence.** The chosen language is saved to a small Desktop-owned JSON file that holds only the language preference. On startup a saved value wins over R2. A missing, unreadable, or corrupt file, or an unknown value, falls back to R2 without crashing. A failed save is logged and does not undo the switch.
- **R4 Immediate switch.** Switching rebuilds the main window's content (`MainView`) inside the same OS window, so there is no window close/open flash and views keep using `{x:Static}` with compile-time key checks. Window size, position and state are untouched. ViewModel state (scanned installs, manually added folders) survives the rebuild and no rescan happens. Error and warning texts already shown are re-rendered in the new language.
- **R5 Switcher UI.** A narrow bar at the top of the main window (in `MainView`), outside the page content and visible on every page, with a language drop-down on the right. Options: "English", "简体中文", "繁體中文", each always shown in its own language.
- **R6 Error-text guarantee.** Every `ErrorCode` still has user text in every supported language, enforced by tests that enumerate `ErrorCode` and the supported languages.

- **R7 Spanish, Russian, Portuguese (added 2026-10-15).** One resx each, neutral culture names `es`, `ru`, `pt` (also the persisted values), so every regional variant of the language gets that one translation. Wording: Spanish is Latin-American/neutral, Portuguese is Brazilian. Drop-down options, each in its own language: "Español", "Русский", "Português (Brasil)". Default mapping: OS UI culture `es*` → Spanish, `ru*` → Russian, `pt*` → Portuguese (checked before the English fallback). Inter covers Latin and Cyrillic, so these resx use the English font list.

## Acceptance Criteria

- [ ] AC1 (R1, R6, R7) Every key in the default resx exists with a non-empty value in every other language resx; a test fails when one is missing.
- [ ] AC2 (R6) For every `ErrorCode` × every supported language, `ErrorMessages.ToUserText` returns non-empty text that is not the developer fallback; path details are still included.
- [ ] AC3 (R2) Unit tests map `zh-CN`, `zh-SG`, `zh-Hans`, `zh` → Simplified; `zh-TW`, `zh-HK`, `zh-MO`, `zh-Hant` → Traditional; `es-ES`, `es-MX`, `es-419` → Spanish; `ru-RU` → Russian; `pt-BR`, `pt-PT` → Portuguese; `en-US`, `ja-JP`, invariant → English.
- [ ] AC4 (R3) Store tests: save→load round-trips each language; missing file, corrupt JSON, and an unknown value all load as "no preference".
- [ ] AC5 (R4) ViewModel test: after a language change, an `ErrorText` and `Warnings` produced before the change are re-rendered in the new language.
- [ ] AC6 (R1) Display names exist for every `PzInstallKind` and `PzInstallSource` value (test enumerates the enums).
- [ ] AC7 Build passes with `-warnaserror` (no new IL2xxx/IL3xxx); all existing tests still pass.
- [ ] AC8 Manual smoke (Windows): first launch on an English OS shows English; switching to every language updates the whole window immediately without rescanning and without the window flashing, CJK and Cyrillic glyphs render (no boxes), longer Spanish/Portuguese/Russian labels don't clip, window size/position/maximized state are kept; after restart the last choice is used; deleting the preference file returns to the OS default.

## Out of Scope

- Languages beyond the six above; regional variants (pt-PT, es-ES) as separate options; RTL.
- Translating Core's developer-facing `Error.Message`.
- A general settings system or settings page; the preference file holds only the language.
- Changing number/date formatting culture (`CurrentCulture`).
- Translating the native OS folder-picker chrome (only the title text we pass in).
