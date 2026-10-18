# Steam launch hook setting

## Goal

When the user clicks Play for Project Zomboid in Steam, PzLauncher opens
instead of the game, so the launcher's setup (Java, memory, mod collection,
ZombieBuddy) is never skipped. 设置 gets a Steam section to turn this on and
off; while it is off, each launcher start reminds the user, with a "don't
remind me again" option.

## Background (repository + local machine, read-only)

- Steam keeps per-account launch options in
  `<steam>/userdata/<accountId>/config/localconfig.vdf` at
  `UserLocalConfigStore/Software/Valve/Steam/apps/108600/LaunchOptions` (key
  casing varies). `"<launcher exe>" %command%` makes Steam start the launcher
  and pass the game's command line as arguments; game files are untouched, so
  "verify integrity" does not undo it.
- Steam rewrites `localconfig.vdf` while running and on exit, so an edit made
  while it runs is lost. Steam was running during inspection.
- This machine has ~40 `userdata/<id>` folders; the current account is the
  `MostRecent` user in `<steam>/config/loginusers.vdf`.
- Launch options are user data: one local account has `-agentlib:zbNative --`
  (ZombieBuddy's manual setup), another has `""`.
- Steam root discovery and a read-only KV1 parser exist
  (`PzLauncher.Core/Steam/SteamLocator.cs`, `SteamKeyValues.cs`); app id 108600
  is `PzSteamApps.Game`. Writing files we don't own follows
  `.trellis/spec/core/error-handling.md` "Writing files we don't own".
- The launcher has no single-instance guard and ignores command-line args
  (`PzLauncher.Desktop/Program.cs`, `App.axaml.cs`). Self-update installs in
  place and restarts by starting the new exe while the old one is still
  closing (`UpdateService.StartInstalledAsync`, `UpdateCoordinator`).
- The game is started by the launcher's own java command (`GameLaunchService`),
  not by `ProjectZomboid64.exe`.
- Release targets `win-x64` and `linux-x64` (`.github/workflows/release.yml`).

## Decisions

- D1: Started by Steam, the launcher only opens its window; the user starts
  the game with 启动游戏. The launcher stays open after the game exits (Steam
  shows "in game" until the launcher closes). Arguments Steam passes are
  ignored.
- D2: Toggling while Steam runs asks first ("关闭 Steam 并应用" / 取消). On
  confirm the launcher shuts Steam down gracefully, waits for it to exit
  (bounded), writes the launch options, then starts Steam again. Cancel
  changes nothing.
- D3: Only the current Steam account (`MostRecent`) is read and written; other
  accounts are never touched.
- D4: Single instance per install directory: a second start of the same
  launcher folder brings the running window to the front (restoring it if
  minimised) and exits. Launchers in different folders (release install, dev
  `bin/Debug`) run side by side as today. A self-update restart waits for the
  old instance instead of exiting.
- D5: Windows only. On Linux/macOS the Steam section is hidden, no reminder
  shows and no single-instance guard is added.
- D6: The reminder dialog offers 立即开启 (runs the same enable flow as the
  switch, including D2) and 暂不, plus a 不再提醒 checkbox that is saved
  whichever way the dialog closes.
- D7 (proposed with the plan): the hook counts as on only when it points to
  this launcher's exe. A hook pointing to another launcher path (moved folder,
  dev build) shows as off with that path, and turning it on re-points it.
- D8 (proposed with the plan): 设置 → Steam also has a 启动时提醒 checkbox, so
  "don't remind again" can be undone.

## Requirements

- R1: 设置 gets a Steam section (游戏 group, Windows only) showing the current
  Steam account's name, a switch for the hook, and a hint that Steam launch
  options other than the hook reach the launcher, not the game (game args
  belong in 版本设置).
- R2: The switch reflects what `localconfig.vdf` holds now, re-read each time
  the section opens. A failed or cancelled change leaves the switch showing
  the real state and reports the error.
- R3: Enabling keeps existing launch options: options without `%command%`
  become `"<exe>" %command% <old>`; options with `%command%` get `"<exe>" `
  inserted before the first `%command%`; an existing hook to another
  PzLauncher path is replaced. Disabling removes only the hook token (and a
  bare leading `%command%` left behind), restoring the old meaning.
- R4: `localconfig.vdf` is changed only at that one value: re-read right
  before writing, every other byte kept, atomic write, rolling
  `localconfig.vdf.pzlauncher.bak`, no write when unchanged. A file the
  edit can't place unambiguously is reported as malformed, never "fixed".
- R5: The section is unavailable (switch disabled, reason shown) when Steam is
  not found, no current account is found, or that account's `localconfig.vdf`
  has no `108600` entry.
- R6: The reminder (D6) shows once per launcher start, after the main window
  opens, only when: Windows, the reminder preference is not off, and the hook
  status is off for an available account (R5). Nothing shows otherwise.
- R7: Single instance and activation per D4.

## Acceptance Criteria

- [ ] AC1 (R1, R2, D5): On Windows, 设置 shows Steam under 游戏 with the
      account name and the switch in the state read from `localconfig.vdf`;
      on Linux the section is absent.
- [ ] AC2 (R3, R4): With Steam closed, turning the switch on writes
      `"<exe path>" %command% <old options>`; turning it off restores the old
      options; a fixture copied from a real `localconfig.vdf` round-trips with
      only that value changed, and a `.pzlauncher.bak` holds the previous file.
- [ ] AC3 (D2): With Steam running, toggling shows the confirm dialog; Cancel
      changes nothing; confirm closes Steam, writes, restarts Steam, and the
      switch shows the new state. A shutdown that times out reports an error
      and writes nothing.
- [ ] AC4 (D1): With the hook on, Steam's Play opens the launcher window and
      no game starts until 启动游戏 is clicked.
- [ ] AC5 (D4): Starting the same launcher again (double-click or Steam Play)
      activates the existing window and the new process exits; a launcher in
      another folder still starts; "restart and update" still restarts.
- [ ] AC6 (R6, D6): With the hook off, each start shows the reminder; 立即开启
      enables it (via AC3 when Steam runs); 不再提醒 stops future reminders;
      the 启动时提醒 checkbox (D8) turns them back on. No reminder when the hook
      is on or R5 makes the section unavailable.
- [ ] AC7 (D7): A hook to another launcher path shows as off with that path;
      turning it on re-points it to this exe.
- [ ] AC8: New preference follows `.trellis/spec/desktop/preferences.md`
      (round-trip, missing, unknown, other fields kept, unwritable file);
      new error codes have resx text in all 10 languages;
      `dotnet build PzLauncher.slnx -warnaserror` and `dotnet test
      PzLauncher.slnx` pass.

## Out of Scope

- Linux/macOS Steam hook and single instance (D5).
- Accounts other than the current one (D3).
- Forwarding Steam's launch arguments to the game; auto-starting the game
  when launched by Steam (D1).
- Replacing or renaming game files; the dedicated server (app 380870).
