# Research: Project Zomboid Linux (and macOS) launch layout

- **Query**: Linux `projectzomboid.sh` (Build 42), Linux `ProjectZomboid64.json`, exact LD_LIBRARY_PATH / LD_PRELOAD; macOS bundle notes
- **Scope**: external (public copies in GitHub repos); no Linux/macOS install was inspected
- **Date**: 2026-10-15

Labels: **Confirmed** = verbatim public copy of the shipped file. **Partial** = verbatim fragment. **Inferred** =
derived from third-party launchers/packagers that replicate the script.

## Install layout (Linux depot) — Inferred, high agreement

```
steamapps/common/ProjectZomboid/
  projectzomboid.sh              <- client launcher script (top level)
  projectzomboid/                <- game payload = working dir, "install root" for launching
    ProjectZomboid64             <- native launcher (pzexe), hosts the JVM in-process
    ProjectZomboid64.json
    projectzomboid.jar
    jre64/  (bin/java, lib/, lib/server/libjvm.so, lib/libjsig.so)
    linux64/   natives/   libPZXInitThreads64.so (location: see caveats)
```

Sources: Storm `LauncherConfig.java` ("the linux depot's extra `projectzomboid/`" subdir, lines ~395-410); PZO
`pzo_optimizer.sh:385-446` (`.../ProjectZomboid/projectzomboid` with `ProjectZomboid64.json`); kraftwerk-gaming/strom
`default.nix` ("projectzomboid.sh (top-level launcher)… projectzomboid/ (game libs + bundled jre64). The launcher cd's
into ./projectzomboid before exec'ing ProjectZomboid64"); Konijima `test/run.sh`
(`GAME=…/common/ProjectZomboid/projectzomboid`).

**Impact for the PRD:** the Linux install root that holds the json and `jre64` is `<library>/ProjectZomboid/projectzomboid`,
not the Steam app folder. The Windows depot has no extra level.

## `projectzomboid.sh` (client, Build 42) — Partial

There is no full public copy of the B42 client script. The verbatim fragment (keventao/gamecheats README, quoting the
shipped script) is:

```sh
if "${INSTDIR}/jre64/bin/java" -version > /dev/null 2>&1; then
    export PATH="${INSTDIR}/jre64/bin:$PATH"
    XMODIFIERS= LD_PRELOAD="...:libPZXInitThreads64.so" ./ProjectZomboid64 "$@"
fi
```

The `...` is the quoter's elision. Other facts about the script (Inferred):
- It prepends to the inherited `LD_PRELOAD` (strom: "projectzomboid.sh preserves an inherited $LD_PRELOAD (it prepends to ${LD_PRELOAD})").
- It writes `$HOME/Zomboid/projectzomboid.sh.log` (strom, VeraciousNetwork `setup.sh`, PZ_Optimization docs).
- It clears `XMODIFIERS` (fragment above).
- LD_PRELOAD in replicas: `"${LD_PRELOAD}:libjsig.so:libPZXInitThreads64.so"` (Konijima `run.sh`, PZ_Optimization `harness/uninstall-e2e.sh`).

## Dedicated-server `start-server.sh` (same family, B41 pattern) — Confirmed (verbatim public copies)

Copied verbatim in uka-it/pzserver-manager `start-server.sh` and Pommaq/project-zomboid-container (Pommaq's copy is
patched to `exec`):

```sh
INSTDIR="`dirname $0`" ; cd "${INSTDIR}" ; INSTDIR="`pwd`"
if "${INSTDIR}/jre64/bin/java" -version > /dev/null 2>&1; then
	echo "64-bit java detected"
	export PATH="${INSTDIR}/jre64/bin:$PATH"
	export LD_LIBRARY_PATH="${INSTDIR}/linux64:${INSTDIR}/natives:${INSTDIR}:${INSTDIR}/jre64/lib/amd64:${LD_LIBRARY_PATH}"
	JSIG="libjsig.so"
	LD_PRELOAD="${LD_PRELOAD}:${JSIG}" ./ProjectZomboid64 "$@"
elif "${INSTDIR}/jre/bin/java" -client -version > /dev/null 2>&1; then
	... 32-bit branch (linux32, jre/lib/i386, ProjectZomboid32) ...
fi
```

This matches the PRD's current text (`linux64`, `natives`, root, `jre64/lib/amd64`, `LD_PRELOAD=libjsig.so`). The
B42 client script adds `libPZXInitThreads64.so` and `XMODIFIERS=`. `jre64/lib/amd64` is a JDK 8 path. On the bundled
Java 25 JRE, `libjsig.so` lives in `jre64/lib/`.

## Environment used by direct-JVM replicas (Inferred)

| Source | LD_LIBRARY_PATH | LD_PRELOAD |
|---|---|---|
| Storm `GameLaunch.nativeEnvironment` (spawns `java` directly, "Vanilla's projectzomboid.sh exports these") | existing dirs of `linux64`, root, `jre64/lib`, then inherited | inherited + `libjsig.so` (if `jre64/lib/libjsig.so` exists) + `libPZXInitThreads64.so` (if present in root) |
| Konijima `test/run.sh` (B42 client, direct `java`) | `natives:root:jre64/lib:jre64/lib/amd64:inherited`, `PATH=jre64/bin:…` | `inherited:libjsig.so:libPZXInitThreads64.so`, plus `XMODIFIERS=` |
| YellowOnion nix (system OpenJDK 25) | (patchelf instead) | `natives/libPZXInitThreads64.so:<jdk>/lib/libjsig.so` |

Why it matters: Storm notes that transitive natives (fmod, steam_api) resolve through the loader path, not
`java.library.path`. So a direct JVM spawn needs `LD_LIBRARY_PATH`. `libjsig.so` is preloaded by bare name, so its
folder (`jre64/lib`, or the chosen JDK's `lib`) must be on `LD_LIBRARY_PATH`, or the preload must use an absolute path.

## `ProjectZomboid64` native launcher behaviour (Inferred, from a box64 log of the server)

The log shows: `pzexe: looking for PZXInitThreads.so` → loads `libPZXInitThreads64.so` (calls `XInitThreads()`) →
`executing "java -classpath pzexe.jar -Djava.library.path=. zombie.pzexe"` to find `libjvm.so` (through `PATH`, which is
why the script prepends `jre64/bin`) → creates the JVM in-process from `ProjectZomboid64.json`. The log prints
`JVM=<root>/jre64/lib/server/libjvm.so` (VeraciousNetwork parses this). Source: ckrinitsin/emulator-bug-study
`results/scraper/box64/112` (box64 issue #112).

## Linux `ProjectZomboid64.json` (client) — Inferred (medium)

PZ_Optimization `config/launcher/ProjectZomboid64.stock.json`. The commit message says "Keep Steam's original". The
repo is Linux-hosted, and its findings doc says stock Linux B42 json carries `-XX:+UseZGC`.

```json
{
  "mainClass": "zombie/gameStates/MainScreenState",
  "classpath": [".", "projectzomboid.jar"],
  "vmArgs": [
    "-Djava.awt.headless=true",
    "--enable-native-access=ALL-UNNAMED",
    "--add-exports=java.base/jdk.internal.misc=ALL-UNNAMED",
    "-Xmx3072m", "-Dzomboid.steam=1", "-Dzomboid.znetlog=1",
    "-Djava.library.path=natives/",
    "-Djava.security.egd=file:/dev/./urandom",
    "-XX:+UseZGC", "-XX:-OmitStackTraceInFastThrow"
  ]
}
```

- **No OS-version map** (no `windows`/`linux` block). Storm's `PzGameJson` reads only a `windows` map and applies it
  on Windows only.
- Other Linux values seen: `-Djava.library.path=linux64/:natives/` (B41 / dedicated server jsons: openzomboid/pzlsm,
  serverjockey) and `linux64/:.` (uft-gsc `ProjectZomboid64.B42.json`, server). Replicas pass `./:./natives/`. The
  exact client value in current B42 is **not confirmed**. Read it from the json; don't hard-code it.
- Classpath separator on Linux is `:`. The json lists entries as an array.

## Windows reference (this machine, for comparison)

`E:\SteamLibrary\...\ProjectZomboid\ProjectZomboid64.json` has a `windows` map `{"10.0.17134": [-XX:+UseShenandoahGC],
"6.1": [-XX:+UseG1GC]}`. This copy is **user-modified**: `-agentlib:zbNative…`, `-Xmx23072m`, plus
`.pzopt-backup`/`.viewpoint-backup` files beside it. Do not use it as a stock reference.

## macOS app bundle (for later) — Inferred

From Storm `MacAppBundle.java` and PZO `pzo_optimizer.sh:135-360`:
- Location: `~/Library/Application Support/Steam/steamapps/common/ProjectZomboid/Project Zomboid.app` (PZO also probes `ProjectZomboid.app`).
- **No `ProjectZomboid64.json`.** `Contents/Info.plist` keys: `JVMMainClassName` (string), `JVMOptions` (string array;
  PZO also checks `VMOptions`, and repairs a dict-shaped `JVMOptions`).
- Classpath: every `*.jar` in `Contents/Java` (also the working dir).
- JRE: `Contents/PlugIns/jre-x86_64/Contents/Home/bin/java` and/or `jre-aarch64/...`. Launched by the bundle's `JavaAppLauncher`.
- Loader env: `DYLD_LIBRARY_PATH` (Storm). Steam overlay injects `DYLD_INSERT_LIBRARIES`.

## External References

- Storm launcher: https://github.com/guspuffygit/project-zomboid-storm/blob/HEAD/launcher/src/main/java/io/pzstorm/launcher/GameLaunch.java , `PzGameJson.java`, `MacAppBundle.java`, `LauncherConfig.java`, `docs/launcher.md`
- B42 script fragment: https://github.com/keventao/gamecheats/blob/HEAD/ProjectZomboid/README.md
- strom nix package: https://github.com/kraftwerk-gaming/strom/blob/HEAD/games/project-zomboid/default.nix
- Konijima replica: https://github.com/Konijima/pz-vehicle-events/blob/HEAD/test/run.sh
- Server script copies: https://github.com/uka-it/pzserver-manager/blob/HEAD/start-server.sh , https://github.com/Pommaq/project-zomboid-container/blob/HEAD/tools/runner/src/services/zomboid/patched-start-server.sh
- Stock json: https://github.com/xD3I/PZ_Optimization/blob/HEAD/config/launcher/ProjectZomboid64.stock.json , `docs/findings-pzo-launcher-2026-10-02.md`
- PZO script (Linux/mac paths, Info.plist patching): https://github.com/prop11/PZO-Launcher/blob/HEAD/pzo_optimizer.sh
- YellowOnion nix: https://github.com/YellowOnion/nixos-config/blob/HEAD/packages/projectzomboid/package.nix
- box64 log: https://github.com/ckrinitsin/emulator-bug-study/blob/HEAD/results/scraper/box64/112

## Caveats / Not Found

- No full verbatim B42 client `projectzomboid.sh` was found. The LD_LIBRARY_PATH of the B42 client script is unconfirmed.
- `libPZXInitThreads64.so` location conflicts: Storm checks the payload root, while the nix package finds it in `natives/`.
- The json above is "Steam's original" per a third-party commit, and it may have since changed.
- The macOS details come from two third-party tools only. No real bundle was inspected (matches PRD D8).
