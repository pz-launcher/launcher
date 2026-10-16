# Research: GC override, GraalVM + Shenandoah, Viewpoint

- **Date**: 2026-10-16
- **Scope**: the user's Windows machine (read-only), the user's crash output, the Viewpoint mod's jar (strings only, never run)

## GraalVM + Shenandoah crashes the game — Confirmed

The user chose `C:\Users\dontc\.jdks\graalvm-ce-25.0.2` in 版本设置. The json's `windows` → `10.0.17134` entry gave
`-XX:+UseShenandoahGC`. The JVM died at start with
`jdk.graal.compiler.debug.GraalError: Shenandoah garbage collector is not supported by Graal`, then
`fatal error: Fatal JVMCI exception`, and wrote `<game>\hs_err_pid<pid>.log` (the game folder is the working
directory). The launcher saw a normal start and an exit, and showed nothing.

## Detecting a Graal JIT from the release file — Confirmed (GraalVM CE 25.0.2)

```
IMPLEMENTOR="GraalVM Community"
JAVA_RUNTIME_VERSION="25.0.2+10-jvmci-b01"
GRAALVM_VERSION="25.0.2"
```

`GRAALVM_VERSION` present, or `jvmci` in `JAVA_RUNTIME_VERSION`, means the Graal JIT is the default compiler.
`IMPLEMENTOR` alone can't tell Oracle GraalVM from Oracle JDK. `-XX:-UseJVMCICompiler` switches back to C2, which
supports Shenandoah. Only Shenandoah is known to fail; other GCs are not blocked.

## Viewpoint mod (workshop 3809306528) — Confirmed from `Viewpoint.jar` strings

- `viewpoint/platform/Collector`, `LauncherCollector`, `OnboardingRestart`: on first run it checks that the json's GC
  is ZGC and that the game really started with it, then **offers** (skippable: `skipCollector`) to switch.
- Onboarding text: "The game uses ZGC to manage its memory. ZGC keeps much memory for itself. Shenandoah keeps less.
  Viewpoint changes the game's start settings to use it."
- It rewrites `-XX:+UseZGC` → `-XX:+UseShenandoahGC` in `ProjectZomboid64.json`, keeps the old file as
  `ProjectZomboid64.json.viewpoint-backup`, then the game saves and quits.
- If the running GC is ZGC but not from the json ("ZGC, but not from …"), or not ZGC, it doesn't offer anything, so a
  launcher GC override doesn't fight it.
- On this machine: the backup and the json are both dated 2026-10-11 22:44; the only difference is that entry.
  The stock json uses ZGC for `10.0.17134`.

Shenandoah is not in any stock json seen so far; the user reports Viewpoint is the only common source of it.

## JVM start failures seen on this machine (2026-10-16)

- Oracle GraalVM 21.0.7 + `-XX:+UseShenandoahGC`: `Error occurred during initialization of VM` / `Option
  -XX:+UseShenandoahGC not supported`, exit 1, no hs_err (Oracle builds don't include Shenandoah).
- Two GC flags: `Multiple garbage collectors selected`, exit 1, no hs_err.
- Unknown option: `Unrecognized VM option '...'` / `Could not create the Java Virtual Machine.`, exit 1, no hs_err.
- GraalVM CE 25.0.2 and Oracle GraalVM 25 start fine with ZGC and G1.

## Viewpoint and a launcher GC override

With the GC overridden by the launcher the game runs a GC that isn't the json's, so Viewpoint's check
("ZGC, but not from ..." / "not ZGC") offers nothing; no conflict to handle.
