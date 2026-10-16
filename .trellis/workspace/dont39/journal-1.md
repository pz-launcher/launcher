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
