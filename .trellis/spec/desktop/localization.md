# Localization

> All user-facing desktop text comes from resx. Languages: the members of
> `AppLanguage`; English is the default and the fallback for missing keys.

---

## Resources

Reference: `PzLauncher.Desktop/Lang/` → `UiText.resx` (English) and one `UiText.<culture>.resx` per other language, named by `AppLanguages.CultureName`; generator settings in `PzLauncher.Desktop/PzLauncher.Desktop.csproj`.

- `Lang/` holds only resx files; localization code lives in `Localization/`. One resource set, `UiText` (namespace `PzLauncher.Desktop.Lang`). The class is generated into `obj/` (public, so `{x:Static}` reaches it); never commit a `UiText.Designer.cs`.
- Keys are `<Area>_<Name>` (`Installs_Title`, `Error_SteamNotInstalled`, `InstallKind_Game`). Error texts are `Error_<ErrorCode member>`.
- A new key goes into every `UiText*.resx` in the same change. Parameterised texts use `{0}`; every language keeps the same placeholders.
- Product name "PzLauncher" and the language picker's native names (`AppLanguages.NativeName`) are not resources.
- `Main_FontFamily` is the per-language font list (Inter, then CJK fonts of that script per OS; languages Inter covers fully keep the English value); every root that shows text (`MainView`, standalone windows such as the error dialog in `App.axaml.cs`) sets `FontFamily` from it. Without named CJK fallbacks, bold Chinese renders at regular weight.
- Format with `CultureInfo.CurrentCulture`; only the UI language changes, never number/date formatting.

## Terminology

- A game concept takes the game's own term for that language, from `media/lua/shared/Translate/<LANG>/UI.json`. A language whose game translation is missing or empty uses its nearest regional sibling (`es-AR`, `es-CL` follow `ES_MX`).
- The game's mod lists (`ModCollection`) are the game's "mods preset" (`UI_mods_SavePrompt`): en "mod preset", zh-Hans 模组预设, zh-Hant 模組預設. Always write the full term, never a bare "preset"/预设/預設. Grammatical forms (plural, case, article and gender agreement) follow the term.
- "Collection" (zh-Hans 合集, zh-Hant 合輯) means only Steam Workshop collections; the launcher's own lists of Workshop items are always "local collection" (本地合集, 本機合輯), never a bare "collection".
- "Modpack" (zh-Hans/zh-Hant 整合包) is the launcher's pairing of a mod preset with a collection; the game has no term for it.

## Using text

| Where | How | Reference |
|---|---|---|
| XAML literal | `{x:Static lang:UiText.Key}` (typos fail the build) | `PzLauncher.Desktop/Views/Installs/InstallsView.axaml` |
| XAML format | `StringFormat={x:Static lang:UiText.Key}` | same |
| Enum shown to users | `DisplayNames.Of` / its `FuncValueConverter`, exhaustive switch | `PzLauncher.Desktop/Localization/DisplayNames.cs` |
| `Error` | `ErrorMessages.ToUserText` | `PzLauncher.Desktop/Localization/ErrorMessages.cs` |
| ViewModel text | Read `UiText` when needed; hold shown text in a `LocalizedText` (one message) or `LocalizedErrorList` (warnings), which keep its source, and call their `Render()` on `ILocalizationService.LanguageChanged` | `PzLauncher.Desktop/Localization/LocalizedText.cs`, `PzLauncher.Desktop/ViewModels/Installs/InstallsViewModel.cs` |

`{x:Static}` is read once per view instance: a language switch replaces the
main window's content (`App.RebuildMainView` → new `MainView`) around the same
ViewModels, so view state must live in ViewModels. Never recreate the window
itself for this: the OS window close/open visibly flashes. Localized UI goes in
`MainView` or below, not on `MainWindow`. Text that changes while a view lives
is a ViewModel property, never `{x:Static}`.

## Adding a language

- Add an `AppLanguage` member at its drop-down position (drop-down order = enum order; files persist culture names, so reordering is safe); the build then requires its `CultureName` and `NativeName`.
- Options mirror Project Zomboid's languages (`media/lua/shared/Translate/*/language.json`), whose names are the reference for `NativeName`; Chinese and Russian keep native names.
- Culture: neutral (`ru`) by default; a script split (`zh-Hans`/`zh-Hant`) or a region when PZ offers regional variants (`es-MX`, `pt-BR`). Add its mapping to `AppLanguages.FromCulture`.
- Add `UiText.<culture>.resx` with every key, in the key order of `UiText.resx`.

## Culture state

`UiText.Culture` is static: the one deliberate exception to "no static mutable state".

- `LocalizationService` is the only production writer. Don't set `CultureInfo.CurrentUICulture`: it flows with `ExecutionContext`, so awaits resume in the old language.
- Startup language: saved preference (`IPreferenceStore`, see [preferences.md](./preferences.md), which owns the file contract), else `AppLanguages.FromCulture(CurrentUICulture)`. Composition in `PzLauncher.Desktop/App.axaml.cs`.

## Tests

- Every test class that constructs a `LocalizationService` or asserts localized text derives from `UiTextCultureTest` and is in `[Collection(UiTextCultureCollection.Name)]` (non-parallel; restores the culture). Reference: `PzLauncher.Desktop.Tests/TestSupport/UiTextCulture.cs`.
- Resx key and placeholder parity per language: `PzLauncher.Desktop.Tests/Localization/UiTextResourcesTests.cs`. Coverage loops enumerate `AppLanguage` and the shown enums, never a hand-kept list.
