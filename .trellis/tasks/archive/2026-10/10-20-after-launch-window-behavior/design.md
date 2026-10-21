# Design: launcher window behavior after game launch

Desktop-only, plus one Core constant made public. No change to how the game process is started or tracked.

## Preference

- New enum `AfterLaunchAction { Keep, Minimize, MinimizeToTray, Close }` (`PzLauncher.Desktop/Services/Launch/`), the
  declaration order = display order (R1).
- `preferences.json` field `afterLaunch`: `keep`, `minimize`, `tray`, `close`; anything else loads as `null` (= not
  chosen, ask on next launch). Explicit `PreferenceName` switch + `PreferenceNames.Parse`, per
  `.trellis/spec/desktop/preferences.md`. `UserPreferences.AfterLaunch` (+ `Equals`/`GetHashCode`),
  `IPreferenceStore.SaveAfterLaunchAsync`.
- App-lifetime holder `AfterLaunchSettings` (observable `AfterLaunchAction? Action`, setter saves fire-and-forget),
  shared by Home (reads / sets after the prompt) and the Settings General page (two-way). A Settings view shows nothing
  selected while it is `null`.

## Window control

- New `ILauncherWindow` (`PzLauncher.Desktop/Services/Shell/`): `Minimize()`, `HideToTray()`, `Restore()` (show if
  hidden, return to the last non-minimized state, activate), `bool IsShown` / `event Restored` (user brought it back),
  implemented over the main window in `Views/`.
- `WindowActivation` folds into that implementation: it already tracks the last non-minimized state for single-instance
  activation (`PzLauncher.Desktop/Views/WindowActivation.cs`); activation, tray click and "Show launcher" all call
  `Restore()`, which now also handles a hidden window (R7).
- Tray icon: an Avalonia `TrayIcon` built in the composition root (`App.axaml.cs`) with the window icon
  (`/Assets/avalonia-logo.ico`), tooltip `PzLauncher`, always visible (R7). Menu texts are set in code and re-set on
  `LanguageChanged`, since `{x:Static}` texts are read once. "Exit launcher" → `IAppLifetime.Close()` (normal close
  path: flushes, staged update).

## Launch flow (`HomeViewModel.LaunchAsync`)

1. First step: if `AfterLaunchSettings.Action` is `null`, show the new prompt
   (`IDialogService.ChooseAfterLaunchAsync() → AfterLaunchAction?`: four radio options in R1 order, preselected Keep,
   hint naming Settings › General, OK). A choice is saved; dismiss → this launch uses Keep, nothing saved (R2).
2. Existing steps unchanged; any early `return` (cancel, failed start) skips the action (R3).
3. After `_runState.IsGameRunning = true`: apply the action — Minimize → `Minimize()`, MinimizeToTray / Close →
  `HideToTray()`.
4. Close only: wait for `WhenAny(exit, delay(EarlyExitWindow))` on the injected `TimeProvider`. If the delay won, the
  game still runs and the user has not restored the window meanwhile → `IAppLifetime.Close()` (R5).
5. On exit, for any action other than Keep → `Restore()` before the existing exit error dialog (R5, R6).

`EarlyExitWindow` moves from a private field of `GameLaunchService` (`PzLauncher.Core/Launch/GameLaunchService.cs:22`)
to a public constant in Core (one owner), used by both `GameLaunchService` and Home.

## Settings

`SettingsSection.General` becomes the first section (and thus the default one opened), at the top of the launcher group
in `SettingsView.axaml`; its page has one radio group bound to `AfterLaunchSettings.Action`.

## Localization

New `UiText` keys (section name, page/option labels, prompt title/hint, tray menu items) in `UiText.resx` and all nine
translations (`PzLauncher.Desktop/Lang/`), per `.trellis/spec/desktop/localization.md`.

## Trade-offs / risks

- Close mode loses crash reporting after the early-exit window (accepted, R5).
- Steam started via the Play hook: whether Steam keeps showing "Playing" after the launcher exits (Close mode) depends
  on Steam tracking the child process; verify manually, not blocking.
- The game's stderr pipe outlives its reader in Close mode; the JVM only writes start-up errors there, which happen
  inside the early-exit window. Verify manually that the game keeps running.
- Linux tray depends on the desktop's StatusNotifier support (Avalonia `TrayIcon`); where missing, MinimizeToTray still
  hides the window and re-running the launcher brings it back.

## Rollback

Self-contained: revert the commit. An `afterLaunch` value left in `preferences.json` is ignored by older versions.
