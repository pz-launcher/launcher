# Localization

> All user-facing desktop text comes from resx. Languages: English (default and
> fallback), Simplified Chinese, Traditional Chinese (Taiwan wording).

---

## Resources

Reference: `PzLauncher.Desktop/Localization/` → `Strings.resx`, `Strings.zh-Hans.resx`, `Strings.zh-Hant.resx`; generator settings in `PzLauncher.Desktop/PzLauncher.Desktop.csproj`.

- One resource set, `Strings`. The class is generated into `obj/` (public, so `{x:Static}` reaches it); never commit a `Strings.Designer.cs`.
- Keys are `<Area>_<Name>` (`Installs_Title`, `Error_SteamNotInstalled`, `InstallKind_Game`). Error texts are `Error_<ErrorCode member>`.
- A new key goes into all three files in the same change. Parameterised texts use `{0}`; every language keeps the same placeholders.
- Product name "PzLauncher" and the language picker's native names (`AppLanguages.NativeName`) are not resources.
- `Main_FontFamily` is the per-language font list (Inter, then CJK fonts of that script per OS); every root that shows text (`MainView`, standalone windows such as the error dialog in `App.axaml.cs`) sets `FontFamily` from it. Without named CJK fallbacks, bold Chinese renders at regular weight.
- Format with `CultureInfo.CurrentCulture`; only the UI language changes, never number/date formatting.

## Using text

| Where | How | Reference |
|---|---|---|
| XAML literal | `{x:Static loc:Strings.Key}` (typos fail the build) | `PzLauncher.Desktop/Views/Installs/InstallsView.axaml` |
| XAML format | `StringFormat={x:Static loc:Strings.Key}` | same |
| Enum shown to users | `DisplayNames.Of` / its `FuncValueConverter`, exhaustive switch | `PzLauncher.Desktop/Localization/DisplayNames.cs` |
| `Error` | `ErrorMessages.ToUserText` | `PzLauncher.Desktop/Localization/ErrorMessages.cs` |
| ViewModel text | Read `Strings` when needed; keep the source (`Error`, a `Func<string>`) of shown text and re-render on `ILocalizationService.LanguageChanged` | `PzLauncher.Desktop/ViewModels/Installs/InstallsViewModel.cs` |

`{x:Static}` is read once per view instance: a language switch replaces the
main window's content (`App.RebuildMainView` → new `MainView`) around the same
ViewModels, so view state must live in ViewModels. Never recreate the window
itself for this: the OS window close/open visibly flashes. Localized UI goes in
`MainView` or below, not on `MainWindow`. Text that changes while a view lives
is a ViewModel property, never `{x:Static}`.

## Culture state

`Strings.Culture` is static: the one deliberate exception to "no static mutable state".

- `LocalizationService` is the only production writer. Don't set `CultureInfo.CurrentUICulture`: it flows with `ExecutionContext`, so awaits resume in the old language.
- Startup language: saved preference (`ILanguagePreferenceStore`), else `AppLanguages.FromCulture(CurrentUICulture)`. Composition in `PzLauncher.Desktop/App.axaml.cs`.
- The preference file stores the culture name (`AppLanguages.CultureName`): a persisted contract, never change one.

## Tests

- Every test class that constructs a `LocalizationService` or asserts localized text derives from `StringsCultureTest` and is in `[Collection(StringsCultureCollection.Name)]` (non-parallel; restores the culture). Reference: `PzLauncher.Desktop.Tests/TestSupport/StringsCulture.cs`.
- Resx key and placeholder parity per language: `PzLauncher.Desktop.Tests/Localization/StringsResourcesTests.cs`. Coverage loops enumerate `AppLanguage` and the shown enums, never a hand-kept list.
