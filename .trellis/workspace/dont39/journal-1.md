# Journal - dont39 (Part 1)

> AI development session journal
> Started: 2026-10-14

---



## Session 1: Detect PZ installs and restructure Trellis specs
<!-- trellis-session: v=2 fp=97ee8c8bcbb72ced -->

**Date**: 2026-10-14
**Task**: Detect PZ installs and restructure Trellis specs
**Branch**: `feat/detect-pz-installs`

### Summary

Added PzLauncher.Core (Steam install detection via ValveKeyValue, Result/ErrorCode) and the Desktop Installs page; renamed app to PzLauncher.Desktop (exe stays PzLauncher.exe); nullability and comment cleanups; concise tests. Specs restructured into common/core/desktop with rules + file references only. PR pz-launcher/launcher#1.

### Git Commits

| Hash | Message |
|------|---------|
| `ae77a83` | Add Trellis specs for common/core/desktop layers and detect-pz-installs task docs |
| `c94072d` | Add PzLauncher.Core with Project Zomboid install detection |
| `5dc73e1` | Rename app to PzLauncher.Desktop and add the Installs page |
| `b8a5d76` | Make tests concise without losing coverage |
| `97482d0` | Remove build/test gate from test-strategy spec |

### Status

[OK] **Completed**


## Session 2: resx localization: 10 language options, runtime switch, Fluent icon
<!-- trellis-session: v=2 fp=f1b6379df128e27b -->

**Date**: 2026-10-15
**Task**: resx localization: 10 language options, runtime switch, Fluent icon
**Branch**: `feat/resx-localization`

### Summary

Desktop UI text moved to Lang/UiText*.resx: English, zh-Hans, zh-Hant (Taiwan), Spanish (España/México/Chile/Argentina) and Portuguese (Portugal/Brasil) mirroring Project Zomboid's language.json labels, Russian. Default from OS UI culture, choice persisted to %AppData%/PzLauncher/preferences.json. Switching rebuilds MainView in place (recreating the window flashed); x:Static kept for compile-time key checks; UiText.Culture has a single writer (LocalizationService). Per-language Main_FontFamily fixes bold CJK. Globe icon via FluentIcons.Avalonia (Native AOT publish 0 IL warnings, exe +5.4 MB). Verified with build/tests (408) and a UI Automation smoke on Windows incl. the AOT exe. PR pz-launcher/launcher#2.

### Git Commits

| Hash | Message |
|------|---------|
| `414efe7` | Add resx localization with runtime language switch |
| `d6d8b97` | Move resx into Lang/ and rename the resource class to UiText |
| `7377e13` | Add Spanish, Russian and Portuguese UI languages |
| `542ddd8` | Mirror Project Zomboid's Spanish and Portuguese language options |
| `f1ed444` | Add a globe icon before the language drop-down |
| `0487771` | Use FluentIcons.Avalonia for the language globe icon |

### Status

[OK] **Completed**


## Session 3: Launcher shell: title-bar nav, page sidebars, PZ theme; analyzer rules as config
<!-- trellis-session: v=2 fp=d15eabb643f3ba0e -->

**Date**: 2026-10-15
**Task**: Launcher shell: title-bar nav, page sidebars, PZ theme; analyzer rules as config
**Branch**: `feat/home-shell`

### Summary

PCL-style shell: OS title bar replaced by MainView's own (tabs Home/Installs/Settings, globe language menu, caption buttons; BorderOnly on Windows/Linux, native traffic lights on macOS). Content-sized page sidebars: Home (right) shows the selected install and a disabled launch button; Installs filters by kind; Settings > Appearance picks light/dark. PZ-flavoured Shell.* palette, light by default, theme saved next to the language in one IPreferenceStore. Spec rules moved to tooling: .editorconfig (CS8509 error, CS8524 off, CA/IDE rules, naming, namespaces), compiled bindings by default, BannedApiAnalyzers (wall clock everywhere; OS/env/registry in Core outside Platform; Avalonia in ViewModels) with RS0030 off by default in .globalconfig. All CS8524 pragmas and the dead IsDefined guard removed; specs trimmed to rules + references. Build clean, 446 tests; window chrome checked by hand on Windows. Squash-merged as pz-launcher/launcher#3 (29c91bf).

### Git Commits

| Hash | Message |
|------|---------|
| `a8cc28f` | Add launcher shell with title-bar navigation, page sidebars and PZ theme |
| `542aa65` | Document shell, theming and preferences specs |
| `5d99e2a` | Move analyzer severities to .editorconfig and drop CS8524 pragmas |
| `aef6a68` | Enforce spec rules through .editorconfig and compiled bindings |
| `e79c9fa` | Ban seam-bypassing APIs with BannedApiAnalyzers |
| `1962bf8` | Trim shell and preferences specs to rules and references |

### Status

[OK] **Completed**


## Session 4: UI animations, native window animations and remembered window placement
<!-- trellis-session: v=2 fp=0bc90c4e9c6ca7e9 -->

**Date**: 2026-10-15
**Task**: UI animations, native window animations and remembered window placement
**Branch**: `feat/ui-animation-transitions`

### Summary

Page switches fade and rise in; shell controls blend colors via PremultipliedBrushTransition (BrushTransition flashes light from Transparent in dark theme). On Windows, MainWindow restores WS_CAPTION|WS_SYSMENU for DWM window animations, forwards WM_NCPAINT to keep the frame, and computes WM_GETMINMAXINFO so the 820x520 minimum holds. Main window opens centered, remembers position/size/maximized in preferences.json (awaited close-time save), falls back to centered when off-screen, and Settings > Appearance can reset it to 1000x640. Dropped ThrowIfNull on non-nullable params project-wide; Result<T> now requires T : notnull. PR pz-launcher/launcher#4 open with Auto-fix on.

### Git Commits

| Hash | Message |
|------|---------|
| `18d5c36` | Animate page switches, shell control states and window chrome |
| `d03bd35` | Remember the main window's placement and add a reset in Settings |
| `a6c5f55` | Drop ThrowIfNull guards on non-nullable parameters |

### Status

[OK] **Completed**


## Session 5: Plan launch feature; implement Java runtime discovery
<!-- trellis-session: v=2 fp=61d5cdf0b12dc73a -->

**Date**: 2026-10-15
**Task**: Plan launch feature; implement Java runtime discovery
**Branch**: `feat/java-discovery`

### Summary

Planned launching as parent 10-15-launch-installs with children: Java discovery, game client (java command built from ProjectZomboid64.json + per-install overrides: Java, min/max memory, extra args; version settings page; Windows + Linux), hosted dedicated server. Implemented and archived Java discovery: Core IJavaDiscoveryService (JAVA_HOME, PATH, Windows registry, Program Files, Linux /usr/lib/jvm etc., SDKMAN, ~/.jdks; release file + PE/ELF bitness; link-resolved de-dup), generic registry reads on IPlatformEnvironment, Settings > Game > Java list with rescan/add/remove and persisted manual homes. PR #5 open. Open: manual smoke AC7 (Windows) / AC8 (Linux); game-client plan must fold in research/linux-launch.md (deeper Linux root, extra preload, libjsig in jre64/lib) and be re-reviewed before start.

### Git Commits

| Hash | Message |
|------|---------|
| `cdf04c5` | chore(task): plan game launch and dedicated server hosting |
| `a17328c` | Discover installed Java runtimes and list them in Settings |

### Status

[OK] **Completed**


## Session 6: Launch the game client, GC choice and install selection
<!-- trellis-session: v=2 fp=e0873d8a20261e79 -->

**Date**: 2026-10-16
**Task**: Launch the game client, GC choice and install selection
**Branch**: `feat/launch-game-client`

### Summary

Home's 启动游戏 starts the selected game install with a launcher-built java command (Windows + Linux), with per-install launch settings, a GC choice, GraalVM+Shenandoah blocking (Viewpoint-aware), crash/failed-start notices, and install choice through 选择安装 / 启动设置. PR pz-launcher/launcher#6.

### Main Changes

- Core Launch/: IGameLaunchService builds the command from ProjectZomboid64.json plus overrides; Linux env mirrors the user's B42 projectzomboid.sh; nested projectzomboid/ game folder; IProcessRunner seam
- 启动设置 page: Java, max/min memory, extra JVM/game args, GC (follow json, ZGC, G1, Shenandoah); per-install preferences with debounced writes flushed on close
- GraalVM + Shenandoah blocked (launch.gc_incompatible[_viewpoint]); GC in extra args counts (launch.gc_conflict); hs_err crash and early-exit notices with captured stderr tail
- 选择安装: no 全部 filter; row click applies the install and goes Home; per-row 启动设置 button (gear); 使用此安装 on the page; 'version' wording replaced by install / launch settings in all 10 languages

### Git Commits

| Hash | Message |
|------|---------|
| `95bc8b0` | chore(task): fold Linux launch research into the game client plan |
| `5bfb616` | Launch the game client from Home with per-install launch settings |
| `caa2617` | chore(spec): keep launch spec additions to rules and references |
| `f076b2c` | chore(task): plan the GC override, GraalVM check and exit notices |
| `1bc491d` | Add a GC choice per install and report crashed or failed game starts |
| `319b250` | Offer ZGC before G1 in the version settings GC list |
| `52acdf7` | chore(task): plan choosing the install through version settings |
| `2c6deac` | Choose the install in use from its version settings page |
| `8854945` | chore(task): stop calling an install a version |
| `fc54408` | Rename version settings to launch settings |
| `d1c2716` | chore(task): clicking an install in 选择安装 applies it |
| `0c051d7` | Apply an install by clicking it, with a launch settings button per row |
| `068bdec` | Use the settings gear icon on the launch settings buttons |

### Testing

- [OK] dotnet build -warnaserror clean; Core 522 passed / 3 skipped (symlink), Desktop 665 passed (run by trellis-check)

### Status

[OK] **Completed**

### Next Steps

- Manual smoke: AC9 (Windows), AC10 (Linux), AC14 (GraalVM + Viewpoint json, forced crash, bad JVM arg, closing the launcher while the game runs), and a visual pass over 启动设置 and 选择安装 in light/dark
- Merge PR pz-launcher/launcher#6, then plan 10-15-host-dedicated-server on top of the Core launch feature


## Session 7: Initialize ZombieBuddy for subscribed installs
<!-- trellis-session: v=2 fp=f974f609652ca324 -->

**Date**: 2026-10-16
**Task**: Initialize ZombieBuddy for subscribed installs
**Branch**: `feat/zombiebuddy-init`

### Summary

Planned and implemented ZombieBuddy setup modelled on its CLI installer: Core IZombieBuddyService (detect stable/beta Workshop item, copy files, add/remove agent arg via atomic vmArgs-only json edit, allow-all policy switch), JavaRuntime.HasInstrumentLibrary; Home launch gate with progress dialog (Launch/Cancel/Retry); Launch settings ZombieBuddy section with channel choice (zombieBuddyChannels pref), Initialize/Remove, instrument-library warning, allow-all switch. Corrected the custom-Java instrument.dll assumption (selected Java's own copy loads first). Build clean; Core 646 passed/3 skipped, Desktop 786 passed. Manual smoke test and real-Linux check not done. PR pz-launcher/launcher#7.

### Git Commits

| Hash | Message |
|------|---------|
| `c60f2ac` | Initialize ZombieBuddy for subscribed installs |

### Status

[OK] **Completed**


## Session 8: Preload Zabric in allow-all mode
<!-- trellis-session: v=2 fp=e313d06d0835d646 -->

**Date**: 2026-10-17
**Task**: Preload Zabric in allow-all mode
**Branch**: `main`

### Summary

Under ZombieBuddy policy=allow-all the approval dialog never runs, so Zabric was never added to ZombieBuddy's preload_mods. Home Launch now registers the installed Zabric (game mod order, -cachedir, Zabric first, Zabric's own current-entry rules) in config.json; failures show the reason and still launch. Restructured Core into PzLauncher.Core.Mod (shared ModInfo/ModLocator; ZombieBuddy Java-mod preload check and preload_mods editor; Zabric service only holds its ids), preload errors as zombiebuddy.* with modId. Launch-settings ZombieBuddy dialog shows a single Confirm after success. PR #8 merged; real game launch not manually verified.

### Git Commits

| Hash | Message |
|------|---------|
| `ad346b8` | Preload Zabric when ZombieBuddy runs with allow-all (#8) |

### Status

[OK] **Completed**


## Session 9: Mod collection management in the launcher
<!-- trellis-session: v=2 fp=28581a4bdf20c1f0 -->

**Date**: 2026-10-17
**Task**: Mod collection management in the launcher
**Branch**: `feat/mod-collections`

### Summary

Planned and built mod collection management (PR #9): Core IModCollectionService over Lua/pz_modlist_settings.cfg and mods/default.txt (game-faithful apply, auto sort, share text, sidecar order, safe re-read writes with backups), installed-mod catalog incl. B42 common/-only mod.info. Desktop Mods tab (installed mods browser with sortable columns, collections editor with lift-out virtualized drag, top/bottom moves), single current-collection model with Home picker; launch applies it, removes ZombieBuddy when the collection lacks it, flushes pending reorders. Follow-up fixes: in-use for empty results, reorder flush on launch/close, not-installed wording. Manual UI checks still pending.

### Git Commits

| Hash | Message |
|------|---------|
| `678629a` | Manage mod collections and installed mods in the launcher |
| `5ec5e37` | Don't mark a collection that enables nothing as in use |
| `7d23758` | Write a pending collection reorder before launch and on close |
| `ac82efb` | Lift a collection row out when dragging it; add move to top and bottom |
| `1a6598e` | Virtualize a collection's mod list |
| `9591de1` | List mods whose mod.info is only in common/; say "not installed" |
| `234e8d9` | Sort installed mods by clicking a column header |
| `13057a6` | Replace apply and default with one current collection |

### Status

[OK] **Completed**


## Session 10: ZB Skip Online Checks setup at launch
<!-- trellis-session: v=2 fp=d8fa6d8682c4f779 -->

**Date**: 2026-10-17
**Task**: ZB Skip Online Checks setup at launch
**Branch**: `main`

### Summary

On Home launch the launcher keeps ZBSkipOnlineChecks in step with the enabled mod list: enabled under allow-all registers it in ZombieBuddy preload_mods after Zabric and adds its patches_jar agent start (jar copied to the game folder, entry in ProjectZomboid64.json); not enabled removes both. Check fixed two WithoutPatchesJar edge cases. Build and tests pass; in-game check not done yet.

### Git Commits

| Hash | Message |
|------|---------|
| `dd48a76` | Set up ZB Skip Online Checks at launch |

### Status

[OK] **Completed**


## Session 11: Unify ZombieBuddy Java mod launch setup
<!-- trellis-session: v=2 fp=6a6525ed4a8c29d5 -->

**Date**: 2026-10-17
**Task**: Unify ZombieBuddy Java mod launch setup
**Branch**: `zb-skip-online-checks`

### Summary

Refactor in PR #10: Home calls IZombieBuddyModsService.PrepareLaunchAsync once; ZombieBuddyLaunch resolves status/root/cache/libraries once and finds preloadable copies; Zabric and ZB Skip Online Checks are internal launch steps run in registration order, and the Zabric dependency moved from Home into Core. Behaviour unchanged; IReadOnlyList<Error> registered in CoreJsonContext. Build and tests pass (Core 978/3 skipped, Desktop 1075).

### Git Commits

| Hash | Message |
|------|---------|
| `651578e` | Run ZombieBuddy Java mod setups through one Core entry point |

### Status

[OK] **Completed**


## Session 12: Launcher auto-update and proxy settings
<!-- trellis-session: v=2 fp=1c2021dd93fd94d9 -->

**Date**: 2026-10-18
**Task**: Launcher auto-update and proxy settings
**Branch**: `main`

### Summary

Auto-update: races GitHub and Gitee release APIs, downloads the platform package into .update/, verifies it (SHA-256 from GitHub, archive layout), installs by renaming packaged files with rollback; banner Restart and update / Later (installs on close); Settings > Updates (source Auto/GitHub/Gitee, auto-check toggle, Check now); dev/CI builds never update. Proxy: one shared Core HttpClient (Net/LauncherHttpClient) with a switchable IWebProxy (System default / None / Manual HTTP with optional credentials, DPAPI on Windows), Settings > Proxy with Test connection, applied before the startup check. Smoke-tested on Windows (0.1.0 -> 0.2.0 via close and via restart; v2rayN system proxy, manual and closed-port proxy). Shipped as PR #11 (squash 6d35946).

### Git Commits

| Hash | Message |
|------|---------|
| `6d35946` | Update the launcher itself from GitHub or Gitee, with proxy settings (#11) |

### Status

[OK] **Completed**


## Session 13: Merge main into PR #10
<!-- trellis-session: v=2 fp=0e67bf58283c4706 -->

**Date**: 2026-10-18
**Task**: Merge main into PR #10
**Branch**: `zb-skip-online-checks`

### Summary

Merged origin/main (launcher auto-update and proxy settings, #11) into zb-skip-online-checks. Resolved directory-structure.md by keeping both sides (mod launch-step lines plus Update/ and Net/; both constants files), rebuilt journal-1.md and index.md in chronological order (sessions 10, 11 then 12) after git interleaved them. Merged tree builds with 0 warnings; tests pass (Core 1197/3 skipped, Desktop 1326).

### Git Commits

| Hash | Message |
|------|---------|
| `d97d785` | Merge remote-tracking branch 'origin/main' into zb-skip-online-checks |

### Status

[OK] **Completed**


## Session 14: Group Desktop Services into feature folders
<!-- trellis-session: v=2 fp=f567d743d3dc6e97 -->

**Date**: 2026-10-18
**Task**: Group Desktop Services into feature folders
**Branch**: `claude/nifty-chaplygin-fa8c72`

### Summary

Pure move: PzLauncher.Desktop/Services (41 flat files) into Shell, Preferences, Launch, Mods, ZombieBuddy, Update, Net with namespaces following folders; tests mirrored; desktop/common spec paths updated and Services/<Group> documented. Release build 0 warnings, tests 1197 + 1326 passed (built in a worktree outside the main repo: the nested .claude/worktrees checkout loads the parent repo's .globalconfig as well, which unsets RS0030 scoping and fails the build).

### Git Commits

| Hash | Message |
|------|---------|
| `98cb239` | Group Desktop Services into feature folders |

### Status

[OK] **Completed**


## Session 15: Steam launch hook setting
<!-- trellis-session: v=2 fp=db02343f1ab8d28c -->

**Date**: 2026-10-18
**Task**: Steam launch hook setting
**Branch**: `feat/steam-launch-hook`

### Summary

Settings > Steam (Windows) hooks the current Steam account's PZ launch options with "<launcher>" %command% so Steam's Play opens PzLauncher; keeps existing options; closes/restarts a running Steam after confirm (exit polled via FindByName + TimeProvider); byte-preserving localconfig.vdf edit with .bak; startup reminder with don't-remind-again (steamHookReminder); single instance per install dir with --pzlauncher-restart for self-update. PR pz-launcher/launcher#13; manual Steam checks AC2-AC5 still pending.

### Git Commits

| Hash | Message |
|------|---------|
| `f28b7f7` | Hook Steam's Play to open the launcher first |

### Status

[OK] **Completed**


## Session 16: Extract shared code and split oversized files
<!-- trellis-session: v=2 fp=415ed53a322403e0 -->

**Date**: 2026-10-18
**Task**: Extract shared code and split oversized files
**Branch**: `refactor/extract-shared-code`

### Summary

Surveyed the repo for duplication and oversized folders (none needed moving; Fakes.cs and LaunchSettingsViewModel were oversized). Implemented C1-C5 and D1-D8: Core Utf8TextFile, AtomicFile.ReplaceWithBackupAsync, PzLaunchConfigFile, FileContent.TryEqualAsync, ErrorDetails; vmArgs write failure moved to launch.config_write_failed (old zombiebuddy code removed). Desktop LocalizedText/LocalizedErrorList, InstallKeyedMap, PreferenceNames.Parse, unified confirm dialog, TextBlock style classes, PageActivation, LaunchSettings ZombieBuddy child VM (plus a stale-load race fix with regression test), Fakes.cs split per service group. Build clean; Core 1363 passed/3 skipped, Desktop 1459 passed; every page pixel-identical to main in light and dark. Specs updated in 7 files.

### Git Commits

| Hash | Message |
|------|---------|
| `e14b8b4` | Extract shared code and split oversized files |

### Status

[OK] **Completed**


## Session 17: Steam Workshop management
<!-- trellis-session: v=2 fp=9a42dbe67997efc2 -->

**Date**: 2026-10-19
**Task**: Steam Workshop management
**Branch**: `feat/workshop-management`

### Summary

Added 工坊订阅 and 工坊合集 sections to the Mods tab: subscriptions from Steam's local files plus the keyless Web API; subscribe/unsubscribe/force update via a short-lived helper process loading PZ's own steam_api (Native AOT, no Valve binaries shipped); Steam collection open/subscribe-missing/generate launcher collection; pre-launch outdated-item check. Spike and live smoke test confirmed force update fetches items Steam hadn't noticed and RunningAppID returns to 0 after the helper exits. Tests: Core 1534, Desktop 1680 passing; AOT publish 0 warnings.

### Git Commits

| Hash | Message |
|------|---------|
| `ce67881` | Manage Steam Workshop subscriptions and collections in the launcher |

### Status

[OK] **Completed**


## Session 18: Favorited and own Steam collections; Workshop reuse refactor
<!-- trellis-session: v=2 fp=2aaedf0cb27f58a7 -->

**Date**: 2026-10-19
**Task**: Favorited and own Steam collections; Workshop reuse refactor
**Branch**: `feat/workshop-management`

### Summary

Grouped Workshop files into Sources/Actions/Helper subfolders; shared duplicated Workshop code with existing helpers (HttpJson, SteamKeyValues read/parse, WorkshopItem.WithLatest, section/dialog base VMs, shared styles). Added favorited + own Steam collections to 工坊合集 via a read-only helper query (creator 0, consumer 108600, paged), cached per account, auto-fetched once per session plus 刷新; Steam-not-running doesn't consume the auto fetch. Live read-only check matched expected ids/counts with CJK titles. Tests: Core 1578, Desktop 1724.

### Git Commits

| Hash | Message |
|------|---------|
| `5288c3b` | Group Workshop files into Sources, Actions and Helper subfolders |
| `2059178` | Share duplicated Workshop code with existing helpers |
| `e3969a8` | List favorited and own Steam collections in 工坊合集 |

### Status

[OK] **Completed**
