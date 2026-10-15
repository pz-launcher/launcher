# Research: Java runtime discovery sources (registry, `release`, Linux paths)

- **Query**: Windows registry layouts per vendor; `release` file format; Linux JDK locations and symlink de-dup
- **Scope**: mixed (this machine read-only + vendor installer sources + other launchers' code)
- **Date**: 2026-10-15

Confidence: **High** = primary source (installer source / vendor doc / this machine). **Medium** = several independent
launchers agree, no vendor doc found. **Low** = single or unverified source.

## 1. Windows registry

### This machine (read-only `reg query`)

```
HKLM\SOFTWARE\Eclipse Adoptium\JDK\21.0.9.10\hotspot\MSI
    Path    REG_SZ    C:\Program Files\Eclipse Adoptium\jdk-21.0.9.10-hotspot\
    Main / EnvironmentPath / EnvironmentPathSetForSystem / JavaHome / JavaHomeSetForSystem   REG_DWORD 0x1
HKLM\SOFTWARE\Eclipse Adoptium\JDK\25.0.1.8\hotspot\MSI
    Path    REG_SZ    C:\Program Files\Eclipse Adoptium\jdk-25.0.1.8-hotspot\
    (same DWORDs)
```

- `HKCU\SOFTWARE\Eclipse Adoptium`, `HKLM\SOFTWARE\JavaSoft`, `HKLM\SOFTWARE\WOW6432Node\JavaSoft`,
  `HKLM\SOFTWARE\WOW6432Node\Eclipse Adoptium`: not present.
- In the Adoptium key, `JavaHome` is a **REG_DWORD flag**, not the path. The path is `Path`, and it ends with `\`.

### Per-vendor layouts

| Vendor | Key (under `SOFTWARE\`) | Subkey structure | Home value | Hives | Confidence / source |
|---|---|---|---|---|---|
| Oracle 9+ (also Corretto 11+, and Temurin/Zulu/MS when their `FeatureOracleJavaSoft` option is installed) | `JavaSoft\JDK`, `JavaSoft\JRE` | Base key has value `CurrentVersion` (e.g. `17`). Subkeys `<major>` (e.g. `17`) **and** `<full>` (e.g. `17.0.9`) each hold `JavaHome` | `JavaHome` (REG_SZ) | HKLM only | High: Adoptium WiX `SetOracleJavaSoftKeys` [A1]; Corretto maintainer [C1] |
| Oracle ≤8 (also Corretto 8, Zulu 8) | `JavaSoft\Java Development Kit`, `JavaSoft\Java Runtime Environment` | `CurrentVersion` = `1.8`. Subkeys `1.8` and `1.8.0_392` each hold `JavaHome` (JRE keys also hold `RuntimeLib`). Oracle JRE adds a `<ver>\MSI` sub-subkey that has no `JavaHome` | `JavaHome` | HKLM only | High [A1][C2][C3] |
| Eclipse Adoptium (Temurin) | `Eclipse Adoptium\JDK`, `Eclipse Adoptium\JRE` | `<4-part ver>\hotspot\MSI` (e.g. `21.0.9.10\hotspot\MSI`) | `Path` | HKMU: HKLM for per-machine installs, HKCU for per-user | High: this machine + WiX `Key="SOFTWARE\{vendor}\{JDK|JRE}\{ver}\{jvm}\MSI" Name="Path"` [A1] |
| Eclipse Foundation (early Temurin MSIs, 2021) | `Eclipse Foundation\JDK` (`\JRE` checked by some launchers) | `<ver>\hotspot\MSI` | `Path` | HKMU | Medium: same WiX with old vendor string; Gradle, Prism, Azure Pipelines read it [G1][P1][Z1] |
| AdoptOpenJDK | `AdoptOpenJDK\JDK`, `AdoptOpenJDK\JRE` | `<ver>\hotspot\MSI` **or** `<ver>\openj9\MSI` | `Path` | HKMU | High-Medium: same WiX lineage; Azure Pipelines reads both jvm names [Z1] |
| IBM Semeru | `Semeru\JDK`, `Semeru\JRE` | `<ver>\openj9\MSI` | `Path` | HKMU | Medium: Prism, Azure Pipelines (Microsoft), JavaInfo [P1][Z1][J1] |
| Microsoft Build of OpenJDK | `Microsoft\JDK` | `<ver>\hotspot\MSI` | `Path` | HKMU | High-Medium: Adoptium WiX has a `microsoft` template (`JVM=microsoft` → `hotspot`, VENDOR=Microsoft) [A2]; dotnet/android, Prism read it [D1][P1] |
| Azul Zulu | `Azul Systems\Zulu` (32-bit: `WOW6432Node\Azul Systems\Zulu 32-bit` and `HKCU\...\Zulu 32-bit`) | `zulu-<major>` (JDK) / `zulu-<major>-jre` (JRE). Values on that subkey: `InstallationPath`, `CurrentVersion`, `MajorVersion`, `MinorVersion` | `InstallationPath` | HKLM per-machine (plus generic JavaSoft keys **only if Oracle keys are not already there**); HKCU per-user (Zulu keys only) | High for keys/hives (Azul doc [Z2]); Medium for value name (Prism, dotnet/android `AzulJdkLocations`, PhoenixPE writes [P1][D1]) |
| BellSoft Liberica | `BellSoft\Liberica` | one subkey per install (exact name not confirmed) | `InstallationPath` | HKLM (HKCU per-user not confirmed) | Medium-Low: Prism, GDLauncher, potato-launcher, find-java agree; no BellSoft doc found |
| Amazon Corretto | No vendor key confirmed. 11+: `HKLM\SOFTWARE\JavaSoft\JDK\<full ver>` (+ `\MSI` sub-subkey). 8: `JavaSoft\Java Runtime Environment\1.8` and `\1.8.0_xxx` | — | `JavaHome` | HKLM | High for JavaSoft keys [C1][C2]. A `SOFTWARE\Amazon Corretto` key is read by a few hobby projects, but no source shows it is written: **Low** |

WOW64: `HKLM\SOFTWARE` is redirected (32-bit installers write to `WOW6432Node`). `HKCU\SOFTWARE` is **shared**, so a
32-bit per-user Adoptium install appears in the same HKCU key as a 64-bit one [M1]. Reading only the 64-bit view
skips 32-bit HKLM entries. HKCU entries still need the R2 arch check.

Adoptium's `FeatureOracleJavaSoft` is `Level="2"` (off by default) [A1]. This machine has no JavaSoft keys, which matches.

### Proposed single data table (both hives `LocalMachine` + `CurrentUser`, 64-bit view)

Algorithm per row: enumerate subkeys of `Key`. For each subkey, read `Value` at `Key\<sub>\<Suffix>`. Treat the
value as the Java home: trim whitespace and a trailing `\`, normalize, and expect `bin\javaw.exe` under it.

| # | Key (`SOFTWARE\…`) | Enumerate subkeys | Suffix after subkey | Value |
|---|---|---|---|---|
| 1 | `JavaSoft\JDK` | yes | — | `JavaHome` |
| 2 | `JavaSoft\JRE` | yes | — | `JavaHome` |
| 3 | `JavaSoft\Java Development Kit` | yes | — | `JavaHome` |
| 4 | `JavaSoft\Java Runtime Environment` | yes | — | `JavaHome` |
| 5 | `Eclipse Adoptium\JDK` | yes | `hotspot\MSI` | `Path` |
| 6 | `Eclipse Adoptium\JRE` | yes | `hotspot\MSI` | `Path` |
| 7 | `Eclipse Foundation\JDK` | yes | `hotspot\MSI` | `Path` |
| 8 | `AdoptOpenJDK\JDK` | yes | `hotspot\MSI` | `Path` |
| 9 | `AdoptOpenJDK\JDK` | yes | `openj9\MSI` | `Path` |
| 10 | `AdoptOpenJDK\JRE` | yes | `hotspot\MSI` | `Path` |
| 11 | `AdoptOpenJDK\JRE` | yes | `openj9\MSI` | `Path` |
| 12 | `Semeru\JDK` | yes | `openj9\MSI` | `Path` |
| 13 | `Semeru\JRE` | yes | `openj9\MSI` | `Path` |
| 14 | `Microsoft\JDK` | yes | `hotspot\MSI` | `Path` |
| 15 | `Azul Systems\Zulu` | yes | — | `InstallationPath` |
| 16 | `BellSoft\Liberica` | yes | — | `InstallationPath` |

Notes for the table:
- Rows 1–4 are written only to HKLM. Reading them in HKCU is harmless (the key is absent).
- JavaSoft subkeys `17` and `17.0.9` point to the same home. De-dup collapses them.
- Corretto has no row of its own; rows 1/3/4 cover it. R1's `Amazon Corretto` registry source has no confirmed layout.
- A row whose key is missing → no warning (normal). A security/IO error opening an existing key → `java.source_unreadable` (R3).

### Default install folders (for R1 known-folder scan)

| Vendor | Typical folder | Confidence |
|---|---|---|
| Adoptium | `C:\Program Files\Eclipse Adoptium\jdk-21.0.9.10-hotspot` (per-user: `%LOCALAPPDATA%\Programs\Eclipse Adoptium\…`) | High (this machine; WiX `[ProgramFiles64Folder]` / `[LocalAppDataFolder]Programs\` [A1]) |
| Microsoft | `C:\Program Files\Microsoft\jdk-21.0.x.y-hotspot` | Medium (MS doc example uses `INSTALLDIR="c:\Program Files\Microsoft\"` [M2]) |
| Zulu | `C:\Program Files\Zulu\zulu-21` | Medium (Corretto issue export shows `C:\Program Files\Zulu\zulu-8\` [C3]) |
| Corretto | `C:\Program Files\Amazon Corretto\jdk21.0.x_y`, `jre8` / `jdk1.8.0_xxx` | Medium [C2] |
| Liberica | `C:\Program Files\BellSoft\LibericaJDK-21` (also `-Full`, `LibericaJRE-21`) | Low-Medium |
| Oracle | `C:\Program Files\Java\jdk-21`, `jre1.8.0_xxx` | Medium |
| Semeru | `C:\Program Files\Semeru\jdk-17.x-openj9` | Low |

- Oracle's PATH shim folder `...\Common Files\Oracle\Java\javapath` contains `javaw.exe`. The derived home
  (`...\Oracle\Java`) has no `bin\javaw.exe`, so the existing validation rejects it unless link resolution maps it
  to a real home (older Oracle used symlinks there). Confidence Low on current shim type.
- The per-user Adoptium folder is not in R1's Program Files list. Only the HKCU registry row finds it.

## 2. `release` file

### Observed on this machine (key order as in file)

| Key | Temurin 25.0.1 | Temurin 21.0.9 | PZ `jre64` (Zulu) |
|---|---|---|---|
| `IMPLEMENTOR` | `Eclipse Adoptium` | `Eclipse Adoptium` | `Azul Systems, Inc.` |
| `IMPLEMENTOR_VERSION` | `Temurin-25.0.1+8` | `Temurin-21.0.9+10` | `Zulu25.30+17-CA` |
| `JAVA_RUNTIME_VERSION` | `25.0.1+8-LTS` | `21.0.9+10-LTS` | `25.0.1+8-LTS` |
| `JAVA_VERSION` | `25.0.1` | `21.0.9` | `25.0.1` |
| `JAVA_VERSION_DATE` | `2025-10-21` | `2025-10-21` | `2025-10-21` |
| `LIBC` | `default` | `default` | `default` |
| `MODULES` | long space-separated list | … | … |
| `OS_ARCH` | `x86_64` | `x86_64` | `x86_64` |
| `OS_NAME` | `Windows` | `Windows` | `Windows` |
| `SOURCE` | `.:git:61d6a2c1b35e` | … | `.:git:90b923e6d1bf` |
| Temurin extras | `BUILD_SOURCE`, `BUILD_SOURCE_REPO`, `SOURCE_REPO`, `FULL_VERSION`, `SEMANTIC_VERSION`, `BUILD_INFO`, `JVM_VARIANT`, `JVM_VERSION`, `IMAGE_TYPE` (`JDK`) | same | — |

### Format rules

- Each line is `KEY="value"`, with the value always double-quoted and no escaping observed. JDK 9+ jlink writes the
  keys sorted, via `pw.format("%s=%s%n")` [O1][O2]. JDK 8 writes them with `printf '%s="%s"\n'` [O3].
- **Line endings vary.** The Temurin 25 file is **mixed CRLF + LF** (jlink part CRLF, Temurin-appended part LF). The
  Zulu file is all CRLF. The parser must strip `\r`.
- Unknown keys are normal (vendor extras). Values can be very long (`MODULES` > 1000 chars).

### `OS_ARCH` values

| JDK | Source of value | x64 | x86 32-bit | ARM64 |
|---|---|---|---|---|
| 9+ | `RELEASE_FILE_OS_ARCH=${OPENJDK_TARGET_CPU}` (jdk11u/17u/master) [O4] | `x86_64` | `x86` | `aarch64` |
| 8 | `OPENJDK_TARGET_CPU_LEGACY` [O3][O5] | `amd64` (macOS: `x86_64`) | `i586` | `aarch64` |

Other `OPENJDK_TARGET_CPU` values [O4]: `arm`, `ppc`, `ppc64`, `ppc64le`, `s390`, `s390x`, `riscv32`, `riscv64`,
`sparcv9`, `loongarch64`, `mips64(el)`, `ia64`, `x32`. `i386`/`i686` are not emitted by OpenJDK builds but appear in
some docs/tools.

### `JAVA_VERSION` formats

| Example | Origin | Major |
|---|---|---|
| `1.8.0_392` (`1.7.0_80`) | JDK ≤8 (`JDK_VERSION`) [O3] | second number when the string starts with `1.` |
| `17` | JDK 9+ GA (jlink keeps only the numeric version, trailing zeros trimmed by `Runtime.Version`) [O2] | first number |
| `21.0.9`, `25.0.1`, `9.0.4` | JDK 9+ updates | first number |
| `11.0.9.1`, `17.0.4.1` | JDK 9+ patch releases (4th component) | first number |
| `26` with `JAVA_RUNTIME_VERSION="26-ea+20"` | EA builds (`-ea` is only in `JAVA_RUNTIME_VERSION`) | first number |

### Executable header constants (fallback when there is no `OS_ARCH`)

- PE `Machine`: `0x8664` AMD64, `0xAA64` ARM64, `0x014C` i386, `0x01C4` ARMNT.
- ELF: `EI_CLASS` (byte 4) `2` = 64-bit. `e_machine` (offset 0x12, u16) `0x3E` x86-64, `0xB7` AArch64.

## 3. Linux locations

| Location | Layout | Confidence |
|---|---|---|
| `/usr/lib/jvm` (Debian/Ubuntu) | Real dir `java-17-openjdk-amd64/` (`bin/java`, `release`). Symlink `java-1.17.0-openjdk-amd64 → java-17-openjdk-amd64`. `default-java` symlink (package `default-jre-headless`) → the default JDK. Hidden `.java-1.17.0-openjdk-amd64.jinfo` files | High (packages.debian.org file lists [L1]) |
| `/usr/lib/jvm` (Fedora/RHEL, current spec) | Real dir `java-25-openjdk/` (older releases: `java-17-openjdk-17.0.x.y-z.fc39.x86_64/`). Alternatives-managed links `jre`, `jre-openjdk`, `jre-25`, `jre-25-openjdk`, `java`, `java-openjdk`, `java-25` → `/etc/alternatives/...` → the real dir | High (Fedora `java-25-openjdk.spec` `alternatives --install` lines [L2]) |
| `/usr/lib/jvm` (Arch) | `java-21-openjdk/`, `default` and `default-runtime` symlinks | Medium |
| `/usr/lib64/jvm` (openSUSE; also scanned by Gradle/Prism) | `java-17-openjdk-17` etc. **Not in R1's list** | Medium [G2][P1] |
| `/usr/java` (Oracle RPM) | `jdk-21…/` plus `latest` / `default` symlinks | Medium [G2][P1] |
| `/opt` | Hand-unpacked tarballs `/opt/jdk-21…`. Prism also scans `/opt/jdk/*`, `/opt/jdks/*`, `/opt/ibm/*`; Gradle scans `/opt/java/*` | Medium [P1][G2] |
| SDKMAN | `${SDKMAN_DIR:-~/.sdkman}/candidates/java/<id>` (e.g. `21.0.5-tem`). `current` is a symlink to one of them | High (Prism honours `SDKMAN_DIR` [P1]) |
| `~/.jdks` | IntelliJ downloads (`temurin-21.0.1`, …) | High [P1] |
| Others other launchers scan (not in R1) | `~/.gradle/jdks`, `~/.asdf/installs/java`, Gentoo `/usr/lib64/openjdk-*` and `/opt/openjdk-bin-*`, Flatpak `/app/jdk` | Medium [P1] |

`/usr/bin/java` resolution: `/usr/bin/java → /etc/alternatives/java → /usr/lib/jvm/<real>/bin/java`. Both links are
absolute, and update-alternatives manages them. **JDK 8 caveat:** on JDK 8 the alternative target is
`<home>/jre/bin/java`, so "parent of `bin`" gives `<home>/jre`. That folder is a valid JRE, but it duplicates
`<home>` and on Debian may have no `release` file. Prism probes both `<dir>/bin/java` and `<dir>/jre/bin/java` [P1].

### De-dup findings

- .NET `ResolveLinkTarget(returnFinalTarget: true)` (Unix) calls `readlink` on the **final component** only. It returns
  `null` when that component is not a link, and it does not canonicalize symlinked ancestors. Relative targets are
  combined with the link's directory [N1]. Consequences:
  - `/usr/lib/jvm/default-java` and SDKMAN `current`: the **home dir itself** is the link. Resolving the `bin/java`
    file returns null, so the home directory must be resolved too.
  - `/usr/bin/java` → alternatives chain: resolving the executable file works, then take `bin/..`.
  - Arch has `/usr/lib64 → /usr/lib`. A candidate under `/usr/lib64/jvm/x` has a symlinked **ancestor**, and no
    single `ResolveLinkTarget` call collapses it. Only per-component resolution (realpath-style walk) catches this.
- Existing pattern: `SteamLocator.ResolveLink` (`PzLauncher.Core/Steam/SteamLocator.cs:77-92`) resolves the directory
  with `DirectoryInfo.ResolveLinkTarget(returnFinalTarget: true)`, then `PathNormalizer.Normalize`, and falls back on
  IO/permission errors.
- A home reached via `…/jre` (JDK 8) whose parent has `bin/java` is the same runtime as the parent.
- Registry values end with `\`. Windows comparison must ignore case and trailing separators.

## External References

- [A1] Adoptium installer WiX `Main.wxs.template` / `OpenJDK.Variables.wxi.template`: https://github.com/adoptium/installer/tree/master/wix
- [A2] Adoptium `Build.OpenJDK_generic.cmd` (`VENDOR` default `Eclipse Adoptium`, `microsoft` template): https://github.com/adoptium/installer/blob/master/wix/Build.OpenJDK_generic.cmd
- [C1] Corretto 11 issue #301 (keys under `HKLM\SOFTWARE\JavaSoft\JDK`, `…\11.0.17\MSI`): https://github.com/corretto/corretto-11/issues/301
- [C2] Corretto 8 issue #40 (`Java Runtime Environment\1.8` → `C:\Program Files\Amazon Corretto\jre8`): https://github.com/corretto/corretto-8/issues/40
- [C3] Corretto 8 issue #14 (Zulu 8 JavaSoft export): https://github.com/corretto/corretto-8/issues/14
- [Z2] Azul "Install Azul Zulu on Windows", section "Azul Zulu Registry Keys": https://docs.azul.com/core/install/windows
- [M1] Microsoft "Registry Keys Affected by WOW64" (HKCU\SOFTWARE shared): https://learn.microsoft.com/en-us/windows/win32/winprog64/shared-registry-keys
- [M2] Microsoft Build of OpenJDK install (MSI features, `FeatureOracleJavaSoft`): https://learn.microsoft.com/en-us/java/openjdk/install
- [G1] Gradle `WindowsInstallationSupplier.java`: https://github.com/gradle/gradle/blob/master/platforms/jvm/jvm-services/src/main/java/org/gradle/jvm/toolchain/internal/WindowsInstallationSupplier.java
- [G2] Gradle `LinuxInstallationSupplier.java` (`/usr/lib/jvm`, `/usr/lib64/jvm`, `/usr/java`, `/usr/local/java`, `/opt/java`): same folder
- [P1] Prism Launcher `JavaUtils.cpp` (registry table, Linux dirs, SDKMAN_DIR): https://github.com/PrismLauncher/PrismLauncher/blob/develop/launcher/java/JavaUtils.cpp
- [D1] dotnet/android `Jdks/*JdkLocations.cs` (C#: Azul `zulu-*` + `InstallationPath`, Microsoft `hotspot\MSI` + `Path`): https://github.com/dotnet/android/tree/main/src/Xamarin.Android.Tools.AndroidSdk/Jdks
- [Z1] Azure Pipelines agent `Add-JavaCapabilities.ps1` (Semeru/AdoptOpenJDK openj9): https://github.com/microsoft/azure-pipelines-agent/blob/master/src/Misc/layoutbin/powershell/Add-JavaCapabilities.ps1
- [J1] JavaInfo registry list: https://github.com/Bill-Stewart/JavaInfo
- [O1] OpenJDK `make/ReleaseFile.gmk`: https://github.com/openjdk/jdk/blob/master/make/ReleaseFile.gmk
- [O2] OpenJDK `ReleaseInfoPlugin.java` (`JAVA_VERSION`, `MODULES`, `%n`): https://github.com/openjdk/jdk/blob/master/src/jdk.jlink/share/classes/jdk/tools/jlink/internal/plugins/ReleaseInfoPlugin.java
- [O3] jdk8u `jdk/make/Images.gmk` (`JAVA_VERSION`, `OS_VERSION`, `OS_ARCH=$(OPENJDK_TARGET_CPU_LEGACY)`): https://github.com/openjdk/jdk8u/blob/master/jdk/make/Images.gmk
- [O4] OpenJDK `make/autoconf/platform.m4` (`RELEASE_FILE_OS_ARCH`, CPU list): https://github.com/openjdk/jdk/blob/master/make/autoconf/platform.m4
- [O5] jdk8u `common/autoconf/platform.m4` (`i586`, `amd64`): https://github.com/openjdk/jdk8u/blob/master/common/autoconf/platform.m4
- [L1] Debian file lists: https://packages.debian.org/bookworm/amd64/openjdk-17-jre-headless/filelist , https://packages.debian.org/bookworm/amd64/default-jre-headless/filelist
- [L2] Fedora `java-25-openjdk.spec`: https://src.fedoraproject.org/rpms/java-25-openjdk/blob/rawhide/f/java-25-openjdk.spec
- [N1] .NET `FileSystem.Unix.cs` `ResolveLinkTarget`: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/IO/FileSystem.Unix.cs

## Related Specs

- `.trellis/tasks/10-15-discover-java-runtimes/design.md` — "Registry" and "`release` file" sections, which this file verifies.

## Caveats / Not Found

- Liberica: no BellSoft doc found. The subkey naming and HKCU per-user behaviour are unverified. Only the value name
  `InstallationPath` has multi-source agreement.
- Corretto: no vendor key confirmed. JavaSoft keys are confirmed only from maintainer comments, not installer source
  (the Corretto MSI source is not public in the corretto-11/17/21 repos).
- Semeru: no IBM doc. The layout is inferred from the shared Adoptium WiX plus third-party readers.
- Zulu's `InstallationPath` value name is not in Azul's doc (the doc lists key paths only).
- `aarch64` counts as 64-bit under R2. PZ's natives (`win64`, `linux64`) are x86-64 only, so an ARM64 Java would pass
  R2 but could not load them.
