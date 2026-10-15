# Design: resx localization

## Boundaries

All work is in `PzLauncher.Desktop` (+ its tests). Core is untouched: UI language is a desktop concern, and Core's `Error.Message` stays developer text.

```
PzLauncher.Desktop/
├── Lang/
│   └── UiText.resx + UiText.{zh-Hans,zh-Hant,es-ES,es-MX,es-CL,es-AR,pt-PT,pt-BR,ru}.resx   # all UI text, nothing else
├── Localization/
│   ├── AppLanguage.cs            # enum + culture names, native names, OS-culture resolver
│   ├── ILocalizationService.cs   # Current, SetLanguage, LanguageChanged
│   ├── LocalizationService.cs    # the only writer of UiText.Culture
│   ├── ErrorMessages.cs          # unchanged shape; texts from UiText
│   └── DisplayNames.cs           # PzInstallKind / PzInstallSource → text, plus FuncValueConverters for XAML
├── Services/
│   ├── ILanguagePreferenceStore.cs
│   └── JsonLanguagePreferenceStore.cs   # + DesktopJsonContext (source-gen)
├── ViewModels/MainViewModel.cs   # language options + selection
├── Views/MainWindow.axaml        # bare host: Title, Icon, size; Content = MainView
├── Views/MainView.axaml          # top bar with ComboBox + current page
└── App.axaml.cs                  # startup resolution, content rebuild
```

## Resources

- Generated class `PzLauncher.Desktop.Lang.UiText`, `public`, via `MSBuild:Compile` (`StronglyTypedFileName` under `$(IntermediateOutputPath)`, `PublicClass=true`), so it works from any IDE/CLI and stays out of git. Only `UiText.resx` gets the generator entry. (Not `Resources`: that name collides with the `Resources` property on `Application` and controls.)
- Culture-specific files are named `zh-Hans` / `zh-Hant` so .NET parent-culture fallback does the right thing if a specific culture is ever passed (`zh-TW` → `zh-Hant`, `zh-CN` → `zh-Hans`).
- Keys: `<Area>_<Name>` (`Installs_Title`, `Installs_Rescan`, `Error_SteamNotInstalled`, `InstallKind_Game`, `Dialog_UnexpectedError`, …). Parameterised texts use `{0}` and are formatted with `string.Format(CultureInfo.CurrentCulture, …)`.
- Views keep `{x:Static lang:UiText.Key}`, so a typo is a compile error. `StringFormat` texts (`branch: {0}`, `build {0}`) use `StringFormat={x:Static …}`; if Avalonia's compiler rejects a markup extension there, fall back to a `FuncValueConverter` in `DisplayNames.cs`.

## Language model

- `enum AppLanguage { English, SimplifiedChinese, TraditionalChinese, SpanishSpain, SpanishMexico, SpanishChile, SpanishArgentina, PortuguesePortugal, PortugueseBrazil, Russian }` (drop-down order = enum order) with exhaustive switches (CS8509 as with `ErrorCode`) for: culture name (`en`, `zh-Hans`, `zh-Hant`, `es-ES`, `es-MX`, `es-CL`, `es-AR`, `pt-PT`, `pt-BR`, `ru`), native display name (hard-coded, not from resx — R5), and parsing a culture name back (unknown → no value).
- `AppLanguages.FromCulture(CultureInfo)`: walk the parent chain; `zh-Hant` or a name in {`zh-TW`, `zh-HK`, `zh-MO`} → Traditional; an exact supported Spanish/Portuguese region anywhere on the chain wins; then two-letter `zh` → Simplified, `es` → México, `pt` → Brasil for `pt`/`pt-BR` else Portugal, `ru` → Russian; else English. Spanish/Portuguese use regional cultures to mirror PZ's options; there is no neutral `es`/`pt` file, which is safe because `UiText.Culture` is only ever set to a supported culture name and every file is complete (parity test). Pure function, covered by AC3. The walk avoids depending on ICU vs NLS parent quirks.

## Culture state (deliberate exception to "no static mutable state")

The resx-generated `UiText.Culture` is static; `{x:Static}` gives no other hook. Rules that contain it:

- `LocalizationService` is the only writer. It is constructed with the startup language and applies it in its constructor, before any window exists.
- We set `UiText.Culture` explicitly instead of relying on `CultureInfo.CurrentUICulture`: the latter flows with `ExecutionContext`, so an `await` that started before a switch would resume with the old culture and render stale text.
- `CurrentCulture` (number/date formatting) is not changed.
- Tests that set the language run in one xUnit collection with `DisableParallelization = true` and restore the previous culture on dispose.

## Preference persistence

- `ILanguagePreferenceStore`: `AppLanguage? Load()` (sync: needed in `OnFrameworkInitializationCompleted` before the first window; the file is tiny) and `Task SaveAsync(AppLanguage, CancellationToken ct = default)`.
- `JsonLanguagePreferenceStore(string filePath, ILogger)`; the composition root passes `Path.Combine(Environment.GetFolderPath(SpecialFolder.ApplicationData), "PzLauncher", "preferences.json")`, tests pass a temp path.
- Format: `{"language":"zh-Hans"}` — culture name, not the enum name, so renaming the enum never breaks saved files. Record `LanguagePreference(string? Language)` serialized via a source-generated `DesktopJsonContext` (AOT).
- `Load`: missing file, `IOException`, `UnauthorizedAccessException`, `JsonException`, or unknown value → `null` (logged at warning except "missing"). `SaveAsync`: create directory, write to `preferences.json.tmp`, `File.Move(…, overwrite: true)`; IO failures are logged and swallowed (R3). Only those specific exception types are caught.

## Switching flow

1. `MainView` ComboBox `SelectedItem` ↔ `MainViewModel.SelectedLanguage` (`LanguageOption(AppLanguage Language, string NativeName)` from `Languages`).
2. `OnSelectedLanguageChanged` → `localization.SetLanguage(lang)` (no-op when unchanged) → `_ = preferences.SaveAsync(lang)` (never throws for IO, so fire-and-forget on a `Task` is safe).
3. `SetLanguage` sets `UiText.Culture`, `Current`, raises `LanguageChanged` synchronously.
4. Subscribers:
   - `InstallsViewModel` re-renders `ErrorText` and `Warnings` from their sources. It keeps the source instead of only the string: `Func<string>? _errorSource` (covers both `Error` and `FolderNotLocal`) and `List<Error> _warningSources`. The VM's lifetime equals the app's (held by the singleton `MainViewModel`), so the subscription is never removed.
   - `App` posts `RebuildMainView` via `Dispatcher.UIThread.Post` — the event fires inside the ComboBox selection change, so rebuilding synchronously would tear down the control raising it.
5. `RebuildMainView`: `desktop.MainWindow.Content = new MainView()`. The OS window is never recreated, so there is no close/open animation or taskbar churn, and size/position/state need no copying. The new `MainView` inherits the window's `DataContext` and re-reads every `{x:Static}`; the page view inside it is re-created through the `App.axaml` DataTemplates. `InstallsView.OnLoaded` → `ActivateCommand` is run-once in the VM, so no rescan. (Recreating the whole window was tried first and visibly flashed.)

## Startup

`App.OnFrameworkInitializationCompleted`: build store → `Load()` → `language = saved ?? AppLanguages.FromCulture(CultureInfo.CurrentUICulture)` → register `ILocalizationService` singleton `new LocalizationService(language)`, the store, and VMs → create window. Error-dialog texts read `UiText` at show time.

## XAML

- `MainWindow`: Title "PzLauncher", icon, default size, `x:DataType`, and a `MainView` as content; nothing localized lives here except through `MainView`.
- `MainView` (`UserControl`, `x:DataType=MainViewModel`): `DockPanel`; top `Border` (narrow, subtle separator) with the ComboBox right-aligned; `ContentControl` for the page fills the rest.
- `InstallsView`: literals → `x:Static`; `Kind` / `Source` bindings use `Converter={x:Static loc:DisplayNames.InstallKind}` / `InstallSource` (`FuncValueConverter<TEnum, string>`, reflection-free).
- `DesignData.Main` gets a design `LocalizationService` and a no-op store.

## Risks

- **CJK font fallback (resolved).** `.WithInterFont()` has no CJK glyphs. Avalonia's implicit system fallback rendered SemiBold Chinese at regular weight (seen in smoke). Fix: resx key `Main_FontFamily` holds a per-language family list (Inter, then the script's CJK fonts for Windows/macOS/Linux); `MainView` and the error dialog set `FontFamily.Parse(UiText.Main_FontFamily)`. This also gives zh-Hant Traditional glyph shapes.
- **Native AOT satellite resources.** Not published as AOT today (`IsAotCompatible` only); trimming/AOT warnings are covered by AC7. Verifying a real AOT publish is deferred.
- **Translation quality.** zh-Hans / zh-Hant texts are written by the implementer; the user reviews them in the PR.

## Rollback

Self-contained in Desktop; revert the commit. A leftover `preferences.json` is harmless to older builds.
