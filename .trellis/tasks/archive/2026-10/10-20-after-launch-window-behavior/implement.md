# Implement: launcher window behavior after game launch

## Checklist

1. Core: make the early-exit window a public constant; `GameLaunchService` uses it.
2. Preference: `AfterLaunchAction` + `PreferenceName` mapping, `UserPreferences.AfterLaunch` (+ equality),
   `SaveAfterLaunchAsync` in `IPreferenceStore` / `JsonPreferenceStore` / `DesktopJsonContext` as needed; update the
   field table in `.trellis/spec/desktop/preferences.md`.
3. `AfterLaunchSettings` holder; register in `App.axaml.cs`.
4. `ILauncherWindow` + main-window implementation absorbing `WindowActivation`; hidden-window restore.
5. Tray icon in the composition root; localized menu, re-set on language change.
6. `IDialogService.ChooseAfterLaunchAsync` + `AvaloniaDialogService` implementation; fakes in `PzLauncher.Desktop.Tests`
   and `DesignData`.
7. `HomeViewModel.LaunchAsync`: prompt, apply, Close timer, restore on exit (inject `AfterLaunchSettings`,
   `ILauncherWindow`, `IAppLifetime`, `TimeProvider`).
8. Settings: `SettingsSection.General` first, General page view + bindings.
9. `UiText` keys in all ten resx files.
10. Tests (written in implement, run in check):
    - `JsonPreferenceStoreTests`: `afterLaunch` round-trip, missing, unknown, keeps other fields, unwritable file.
    - `HomeViewModelTests`: no saved choice → prompt; choice saved and not asked again; dismiss → Keep, nothing saved,
      asked again; each action applied only after a successful start; cancelled/failed launch → no window change;
      Minimize/Tray restore on exit before the error dialog; Close → exits after the window with a fake `TimeProvider`,
      not when the game exited early (restore + error), not when the user restored the window.
    - `SettingsViewModelTests`: default section is General; changing the choice saves it.

## Validation

- Implement: `dotnet build` only.
- Check: `dotnet test`; `dotnet format` on changed `.cs` files at finish.
- Manual (Windows): each of the four options from Home and from Steam's Play; tray click / menu; second instance
  restores from tray; language switch updates tray menu; Close mode with a bad JVM arg shows the error.

## Risky files

- `PzLauncher.Desktop/App.axaml.cs` (composition root, tray lifetime)
- `PzLauncher.Desktop/Views/WindowActivation.cs` (replaced)
- `PzLauncher.Desktop/ViewModels/Home/HomeViewModel.cs` (launch flow)
