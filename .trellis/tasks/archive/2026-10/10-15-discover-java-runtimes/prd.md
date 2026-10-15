# Discover Java runtimes

Parent: `10-15-launch-installs`. Must land before `10-15-launch-game-client`,
which offers this list as the per-install Java choice (D16, D17 there).

## Goal

The launcher finds the 64-bit Java runtimes installed on the machine and
lists them in 设置 → Java, with rescan, manual add and remove, so 版本设置 can
offer them as the Java for an install.

## Background (this machine and repo, read-only)

- `JAVA_HOME` = `C:\Program Files\Eclipse Adoptium\jdk-21.0.9.10-hotspot\`;
  `PATH` has Adoptium 21 and 25 (`bin\java(w).exe`). `C:\Program Files\Eclipse
  Adoptium\` holds `jdk-21.0.9.10-hotspot` and `jdk-25.0.1.8-hotspot`. No
  `HKLM\SOFTWARE\JavaSoft` keys.
- A Java home's `release` file gives version/vendor/arch without running java
  (PZ's `jre64\release`: `IMPLEMENTOR`, `IMPLEMENTOR_VERSION` =
  `Zulu25.30+17-CA`, `JAVA_RUNTIME_VERSION` = `25.0.1+8-LTS`).
- 设置 page: nav sidebar, group 启动器 → 外观
  (`PzLauncher.Desktop/ViewModels/Settings/SettingsSection.cs`).
- Folder picker exists: `PzLauncher.Desktop/Services/IFolderPicker.cs`.
- Registry access goes through `IPlatformEnvironment` only (`BannedSymbols.txt`).

## Decisions

- D16 Global Java list in 设置 → Java; per-install pick in 版本设置
  (game-client child).
- D18 Management: 添加… opens a folder picker for a Java home (the folder with
  `bin/javaw.exe` on Windows, `bin/java` on Linux; picking the `bin` folder
  itself is accepted). Manually added
  entries can be removed and are persisted; discovered entries can't be
  removed (a rescan finds them again). Each row shows version, vendor, path,
  source (自动 / 手动). Only 64-bit runtimes are listed.
- D19 PZ installs' bundled `jre64` folders are not in this list; each install
  offers its own as 游戏自带 in 版本设置.
- D20 (follows D8: Windows + Linux) Discovery covers Windows and Linux; on
  macOS the scan finds nothing, manual add still works. The user smoke-tests
  on Linux.

## Requirements

- R1 Core scans, de-duplicates by resolved Java home path (platform path
  comparison) and returns each runtime: home path, Java executable path, version
  (major + full string, or unknown), vendor (or unknown), source.
  Common sources: `JAVA_HOME`; `PATH` entries containing the Java executable
  (symlinks such as `/usr/bin/java` resolved to the real home);
  `~/.jdks`. Windows: registry
  (`HKLM`/`HKCU` `SOFTWARE\JavaSoft\{JDK,JRE,Java Runtime Environment,Java Development Kit}`,
  `Eclipse Adoptium`, `Eclipse Foundation`, `AdoptOpenJDK`, `Semeru`,
  `Azul Systems\Zulu`, `Microsoft\JDK`, `BellSoft\Liberica`; Corretto writes
  the JavaSoft keys — exact table in `research/registry-layouts.md`); folders
  under Program Files (`Java`, `Eclipse Adoptium`, `Zulu`, `Microsoft`,
  `BellSoft`, `Amazon Corretto`, `Semeru`). Linux: sub-folders of
  `/usr/lib/jvm`, `/usr/lib64/jvm`, `/usr/java`, `/opt`,
  `~/.sdkman/candidates/java`.
- R2 A candidate counts only if its Java executable exists and it is 64-bit
  (`release` `OS_ARCH`, else the executable's header: PE on Windows, ELF on
  Linux). Version/vendor
  come from `release`; missing → unknown, still listed.
- R3 Problems that don't stop the scan (unreadable folder, malformed
  `release`, unreadable registry key) are warnings; the scan returns the rest.
- R4 Core inspects a manually picked folder: valid 64-bit Java home (or its
  `bin`) → runtime; otherwise a typed error (invalid path, not found, not a
  Java home, 32-bit).
- R5 设置 gets a group/item for Java (e.g. group 游戏 → Java) showing the list,
  a busy state, an empty state, warnings, 重新扫描 and 添加…; manual rows have
  a remove button. A persisted manual entry whose folder is gone stays listed
  as 找不到 (removable, not selectable).
- R6 The first scan runs the first time the list is needed (opening 设置 → Java
  or 版本设置), once per session; 重新扫描 runs it again. The scanned list is
  shared app-wide for the session.
- R7 Manual entries persist in `preferences.json` (new field, paths only) and
  survive restarts.
- R8 New texts and error texts in all 10 `UiText*.resx` files.

## Acceptance Criteria

- [ ] AC1 (R1–R3) Core tests with a fake platform (env, registry values) and a
      temp-dir world: each source found; duplicates across sources reported
      once (incl. a `/usr/bin/java`-style symlink); `bin` without the Java
      executable skipped; 32-bit skipped (`release`, PE and ELF paths); Windows
      and Linux source sets each tested with a fake platform; missing/malformed `release` → unknown version + warning
      as specified; unreadable source → warning, others still scanned.
- [ ] AC2 (R4) Core tests assert each manual-inspect error code; `bin` folder
      accepted.
- [ ] AC3 (R5–R7) ViewModel tests: busy → loaded/empty, warnings shown, rescan,
      add success / error / picker cancelled, remove manual, missing manual
      shown as 找不到, scan runs once per session.
- [ ] AC4 (R7) Store tests for the new field: round-trip, missing, invalid,
      keeps other fields, unwritable file.
- [ ] AC5 (R8) Resx parity; new codes pinned in `ErrorCodeTests`; new DTOs
      round-trip through `CoreJsonContext`.
- [ ] AC6 `dotnet build PzLauncher.slnx -warnaserror`, `dotnet test PzLauncher.slnx`.
- [ ] AC7 Manual smoke (this machine): 设置 → Java lists Adoptium 21 and 25
      once each with versions/vendor; add a copied JRE folder by hand → listed
      as 手动 and kept after restart; remove it.
- [ ] AC8 Manual smoke (Linux, done by the user): 设置 → Java lists the
      distro's JDKs under `/usr/lib/jvm` (and SDKMAN ones if present) once each.

## Out of Scope

- Using the selected Java to launch, the per-install dropdown, the version
  warning (all `10-15-launch-game-client`).
- Downloading/installing Java; a global default Java; hiding discovered
  entries; macOS discovery sources; running `java -version`.
