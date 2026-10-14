# Research: PZ install markers and real Steam files

Date: 2026-10-14. Machine: developer's Windows 11 workstation. Everything here
was read-only (registry query, `cat`, `ls`). Nothing was written to Steam or
game folders.

## Steam layout found

- `HKCU\Software\Valve\Steam\SteamPath` = `c:/program files (x86)/steam`
  (note: lower-case, forward slashes, so normalize before comparing it with
  the `libraryfolders.vdf` paths).
- `libraryfolders.vdf` lists two libraries:
  - `"0"` → `C:\\Program Files (x86)\\Steam` (the Steam root itself, written
    with different casing and separators than the registry value, so the
    de-duplication has to be case-insensitive on Windows)
  - `"1"` → `E:\\SteamLibrary`
- Both PZ apps are in `E:\SteamLibrary`:
  - `appmanifest_108600.acf`: `installdir` = `ProjectZomboid`, `buildid` = `25485521`
  - `appmanifest_380870.acf`: `installdir` = `Project Zomboid Dedicated Server`,
    `buildid` = `25485538`
- Neither manifest has a `BetaKey` (both on the public branch); `UserConfig` /
  `MountedConfig` only hold `language`.
- Quirk: `libraryfolders.vdf` lists `"380870" "0"` (size 0) in the `apps`
  block even though the server is installed (7 GB on disk). Don't use the
  `apps` block to decide whether something is installed; use the manifest.

## Root-level files

Game (`E:\SteamLibrary\steamapps\common\ProjectZomboid`), files only:

```
Lighting64.dll  PZBullet64.dll  PZClipper64.dll  PZPathFind64.dll  PZPopMan64.dll
ProjectZomboid64.bat  ProjectZomboid64.exe  ProjectZomboid64.json
ProjectZomboid64ShowConsole.bat  ProjectZomboidOpenGLDebug64.bat
ProjectZomboidServer.bat  RakNet64.dll  ZNetJNI64.dll  ZNetNoSteam64.dll
bink2w64.dll  bink64.dll  fmod*.dll  imgui-java64.dll  jassimp64.dll
jnidispatch.dll  projectzomboid.jar  stdlib.lbc  stdlib.lua  steam_api64.dll
steam_appid.txt  terms and conditions.txt  zbNative.dll
(dirs: jre64/ launcher/ license/ media/ mods/ steamapps/ Workshop/ ...)
(plus a few mod-tool leftovers such as ZombieBuddy.jar, ProjectZomboid64.json.*-backup)
```

Dedicated server (`E:\SteamLibrary\steamapps\common\Project Zomboid Dedicated Server`):

```
ProjectZomboid64.json  StartServer64.bat  StartServer64_nosteam.bat
stdlib.lua  steam_appid.txt  steamclient.dll  steamclient64.dll
steamwebrtc.dll  steamwebrtc64.dll  tier0_s.dll  tier0_s64.dll
vstdlib_s.dll  vstdlib_s64.dll  zbNative.dll  ZombieBuddy.jar
(dirs: appcache/ config/ java/ jre64/ license/ logs/ media/ natives/)
```

## Conclusions for `PzInstallMarkers`

- Windows markers in design.md are correct:
  - server: `StartServer64.bat` (present in server, absent from game)
  - game: `ProjectZomboid64.exe` (present in game, absent from server)
- The **game** install ships `ProjectZomboidServer.bat`, so that file must
  never be used as a server marker. `ProjectZomboid64.json` and
  `steam_appid.txt` exist in both, so they can't be markers either.
- Checking server markers first remains the right order.
- macOS/Linux markers (`start-server.sh`, `ProjectZomboid64`,
  `projectzomboid.sh`, `Project Zomboid.app`) could not be verified on this
  machine and are kept as in design.md. Verify them when the macOS/Linux
  ports are tested.

Final table (unchanged from design.md):

| Kind | Markers |
|---|---|
| DedicatedServer | `StartServer64.bat`, `start-server.sh` |
| Game | `ProjectZomboid64.exe`, `ProjectZomboid64`, `projectzomboid.sh`, `Project Zomboid.app` |

## Fixtures

`PzLauncher.Core.Tests/Fixtures/Steam/` holds copies of the real
`libraryfolders.vdf` and `appmanifest_108600.acf`, with `contentid`,
`LastOwner`, depot manifest ids and timestamps replaced by placeholder values.
No user names appeared in any path.
