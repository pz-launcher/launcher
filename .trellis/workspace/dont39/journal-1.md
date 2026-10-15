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
