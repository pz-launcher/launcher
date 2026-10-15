# Add resx localization (en, zh-Hans, zh-Hant, 4× es, 2× pt, ru)

## Goal

All user-facing text in the desktop app comes from resx resources in ten language options: English, Simplified and Traditional Chinese, Spanish (Spain, Mexico, Chile, Argentina), Portuguese (Portugal, Brazil), Russian. The user can switch language from the UI and the switch takes effect immediately; the choice is remembered across restarts. On first launch the language follows the OS UI language. Reference: https://docs.avaloniaui.net/docs/app-development/localizing

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

- **R7 Spanish, Portuguese, Russian — aligned with Project Zomboid (revised 2026-10-15).** Spanish and Portuguese options and labels copy PZ's `media/lua/shared/Translate/*/language.json`: "Español (España)", "Español (México)", "Español (Chile)", "Español (Argentina)", "Português (Portugal)", "Português (Brasil)". Russian keeps its native label "Русский" (PZ shows English names for Russian/Chinese, most likely a font limitation; native names stay readable to non-English speakers). One resx per option with that region's wording (Argentine voseo; Spain "ordenador"/"Añadir"; European Portuguese "ficheiro", "o Steam"). Culture names `es-ES`, `es-MX`, `es-CL`, `es-AR`, `pt-PT`, `pt-BR`, `ru` are also the persisted values (the earlier `es` / `pt` were never released and now load as "no preference"). Default mapping from the OS culture: an exact supported region wins; other `es*` (incl. `es`, `es-419`) → Español (México); `pt-BR` and bare `pt` → Português (Brasil), other `pt-*` → Português (Portugal); `ru*` → Russian. Drop-down order: English, 简体中文, 繁體中文, the four Spanish, the two Portuguese, Русский. Inter covers Latin and Cyrillic, so these resx use the English font list.

## Acceptance Criteria

- [ ] AC1 (R1, R6, R7) Every key in the default resx exists with a non-empty value in every other language resx; a test fails when one is missing.
- [ ] AC2 (R6) For every `ErrorCode` × every supported language, `ErrorMessages.ToUserText` returns non-empty text that is not the developer fallback; path details are still included.
- [ ] AC3 (R2) Unit tests map `zh-CN`, `zh-SG`, `zh-Hans`, `zh` → Simplified; `zh-TW`, `zh-HK`, `zh-MO`, `zh-Hant` → Traditional; `es-ES` → España; `es-MX`, `es-419`, `es-CO`, `es` → México; `es-CL` → Chile; `es-AR` → Argentina; `pt-BR`, `pt` → Brasil; `pt-PT`, `pt-AO` → Portugal; `ru-RU` → Russian; `en-US`, `ja-JP`, invariant → English.
- [ ] AC4 (R3) Store tests: save→load round-trips each language; missing file, corrupt JSON, and an unknown value all load as "no preference".
- [ ] AC5 (R4) ViewModel test: after a language change, an `ErrorText` and `Warnings` produced before the change are re-rendered in the new language.
- [ ] AC6 (R1) Display names exist for every `PzInstallKind` and `PzInstallSource` value (test enumerates the enums).
- [ ] AC7 Build passes with `-warnaserror` (no new IL2xxx/IL3xxx); all existing tests still pass.
- [ ] AC8 Manual smoke (Windows): first launch on an English OS shows English; switching to every language updates the whole window immediately without rescanning and without the window flashing, CJK and Cyrillic glyphs render (no boxes), longer Spanish/Portuguese/Russian labels don't clip, window size/position/maximized state are kept; after restart the last choice is used; deleting the preference file returns to the OS default.

## Out of Scope

- Languages beyond the ten options above; other PZ languages; RTL.
- Translating Core's developer-facing `Error.Message`.
- A general settings system or settings page; the preference file holds only the language.
- Changing number/date formatting culture (`CurrentCulture`).
- Translating the native OS folder-picker chrome (only the title text we pass in).
