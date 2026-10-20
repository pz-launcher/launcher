# Launcher window behavior after game launch

## Goal

After the launcher starts the game, the window does what the user chose: stay as is, minimize, minimize to the system
tray, or close the launcher. The user is asked once, on the first launch, and can change it later in Settings.

## Background (current behavior)

- The launch runs in `HomeViewModel.LaunchAsync` (`PzLauncher.Desktop/ViewModels/Home/HomeViewModel.cs:166`): after
  `_launcher.LaunchAsync` succeeds it sets `GameRunState.IsGameRunning = true`, awaits `WaitForExitAsync`, then clears it
  and shows a dialog if the game crashed or failed to start (`HomeViewModel.cs:223-238`). The window never changes.
- A JVM that exits non-zero within `EarlyExitWindow` (30 s, `PzLauncher.Core/Launch/GameLaunchService.cs:22`) is
  reported as a start failure.
- After exit the launcher also reloads collections/mods and writes a mod order queued while the game ran
  (`PzLauncher.Desktop/Services/Mods/ModCollectionOrderWriter.cs:127`).
- The game is a plain child process (`PzLauncher.Core/Platform/ProcessRunner.cs:17`, no job object), so it survives the
  launcher exiting; its stderr is a pipe the launcher reads.
- Closing the window (`PzLauncher.Desktop/Views/WindowPlacementPersistence.cs`) saves placement, flushes pending writes
  and installs a staged update; `IAppLifetime.Close()` takes the same path.
- No tray icon exists; `WindowActivation` (`PzLauncher.Desktop/Views/WindowActivation.cs`) restores a minimized window
  on single-instance activation but not a hidden one.
- `IDialogService` (`PzLauncher.Desktop/Services/Shell/IDialogService.cs`) has no dialog with more than three choices.
- Steam's Play can start the launcher (`"<launcher exe>" %command%`, `PzLauncher.Core/Steam/SteamLaunchOptions.cs`).

## Requirements

- R1: One global setting (not per install), saved in preferences, with four choices listed in this order everywhere:
  Keep, Minimize, Minimize to tray, Close (Close last). Not chosen yet = no saved value.
- R2: First-launch prompt: when no choice is saved, clicking Launch first shows a dialog (before the workshop update
  check, collection apply and ZombieBuddy setup) asking which choice to use and saying it can be changed later in
  Settings › General. OK saves the choice; it is never asked again. Dismissing it (Esc / ×) launches with Keep this time,
  saves nothing, and asks again on the next launch.
- R3: The choice applies only once the game process has started; a cancelled or failed launch leaves the window alone.
- R4: Keep: no change (today's behavior).
- R5: Close: the window hides as soon as the game starts. Once the game has run past the early-exit window (30 s) the
  launcher exits through the normal close path. If the game exits before that, the window comes back and the error
  shows as today. If the user brings the window back during the wait, the launcher stays open. Crashes after the
  launcher exited are not reported (accepted).
- R6: Minimize / Minimize to tray: when the game exits (normally or not) the window is restored to its pre-minimize
  state (maximized stays maximized) and activated, before any exit error dialog.
- R7: A tray icon is shown for the whole launcher lifetime, whatever the setting. Click: show and activate the window.
  Menu: "Show launcher", "Exit launcher" (normal close path; a running game keeps running). Starting the launcher again
  (e.g. Steam's Play) also brings the window back from the tray. Menu texts follow a language switch.
- R8: Settings gets a new "General" section at the top of the launcher group, holding this choice; it is the section
  Settings opens on. Changing it there saves it. While nothing is saved, no option is selected.
- R9: All new texts are localized in every supported language.

## Acceptance Criteria

- [ ] AC1 (R1, R2): With no saved choice, Launch shows the prompt with the four options in order; choosing one and OK
      saves it, and the next Launch shows no prompt.
- [ ] AC2 (R2): Dismissing the prompt launches with Keep, saves nothing, and the next Launch prompts again.
- [ ] AC3 (R3): Cancelling the workshop prompt or a failed start leaves the window as it was, for every choice.
- [ ] AC4 (R4): Keep leaves the window untouched while the game runs.
- [ ] AC5 (R6): Minimize minimizes the window on start; on game exit it is restored (maximized if it was) and active.
- [ ] AC6 (R6, R7): Minimize to tray hides the window (not in the taskbar) on start; on game exit it is restored and
      active; an exit error dialog appears over the restored window.
- [ ] AC7 (R5): Close hides the window on start; with the game still running after 30 s the launcher process exits and
      the game keeps running; pending saves are flushed.
- [ ] AC8 (R5): Close with a game that fails within 30 s: the window comes back with the start-failure error and the
      launcher stays open.
- [ ] AC9 (R5): Close, user restores the window from the tray within 30 s: the launcher does not exit.
- [ ] AC10 (R7): The tray icon is present from startup; click and "Show launcher" restore a hidden or minimized window;
      "Exit launcher" closes the launcher and leaves a running game running; a second launcher start restores a hidden
      window; switching language updates the menu texts.
- [ ] AC11 (R8): Settings opens on General; the choice there matches the saved value (none selected when unsaved);
      changing it persists across restarts.
- [ ] AC12 (R1): `preferences.json` stores `afterLaunch` as `keep`/`minimize`/`tray`/`close`; a missing or unknown value
      loads as not chosen; saving it keeps the other fields.
- [ ] AC13 (R9): Every new `UiText` key exists in all ten resx files.

## Out of Scope

- Closing the window with × going to the tray instead of exiting (× keeps exiting the launcher).
- Per-install choices.
- Reporting game crashes after the launcher has exited in Close mode.
- Changing how the game process is started or tracked.
- A dedicated app/tray icon asset (the current window icon is reused).

## Risks / Deferred (verify manually, not blocking)

- Steam's Play: whether Steam still shows the game as running after the launcher exits in Close mode.
- Close mode: the game keeps running after the launcher, which read its stderr pipe, has exited.
- Linux tray depends on desktop StatusNotifier support; where missing, re-running the launcher still restores it.
