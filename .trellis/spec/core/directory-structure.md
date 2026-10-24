# Directory Structure (Core)

## Core layout — feature folders

```
PzLauncher.Core/
├── Common/          # Result, Error, ErrorCode, ErrorDetails, path helpers, file reads (ReadFile), atomic writes, strict UTF-8 text (Utf8TextFile), line-preserving text files (GameTextFile), file comparison
├── Platform/        # IPlatformEnvironment + real implementation (OS seam)
│   └── Processes/   # process start, detached start and lookup (IProcessRunner)
├── Steam/           # Steam root/library discovery, Workshop library order, KeyValues parsing
│   ├── LaunchHook/  # launch hook in the current account's launch options
│   └── Workshop/    # Workshop items and collections, without mods
│       ├── Ids/     # Workshop id formats, pasted ids and links
│       ├── Sources/ # Steam's local files, Web API
│       ├── Actions/ # runs the helper process
│       └── Helper/  # code inside the helper process
├── Installs/        # PZ game + dedicated server install detection
├── Java/            # Java runtime discovery (Windows + Linux sources)
├── Launch/          # game launch
│   ├── Config/      # ProjectZomboid64.json reading and vmArgs editing
│   └── Command/     # java command shared by the game and the server: arguments, Linux environment
├── Mod/             # any PZ mod: mod.info reading, lookup in the game's mod order, installed-mod catalog
│   ├── Collections/ # the game's mod collections and active mod list: load order, share text, apply
│   │   └── Files/   # the game's collection, collection order, active-list and mod-list settings files
│   ├── Modpacks/    # modpack share text: a Workshop collection with a mod preset
│   ├── Workshop/    # Workshop items with their mods and collections
│   ├── ZombieBuddy/ # ZombieBuddy setup in a game install
│   │   ├── Preload/     # Java mod preload check and preload list
│   │   └── LaunchSteps/ # launch entry point running each mod's launch step
│   ├── Zabric/      # launch step: Zabric's entry in ZombieBuddy's preload list
│   └── ZBSkipOnlineChecks/ # launch step: ZB Skip Online Checks' preload entry and patches_jar agent start
├── Server/          # dedicated server hosting: named servers, detached start, running file
│   ├── Config/      # server ini access
│   ├── ConsoleLog/  # server-console.txt tail and its line DTOs
│   └── Rcon/        # Source RCON client
├── Update/          # launcher self-update: download + verify + stage, install with rollback
│   └── Releases/    # GitHub/Gitee release feeds
├── Net/             # the one shared HttpClient (LauncherHttpClient), HttpJson, runtime-switchable proxy, proxy settings, connection test
├── Serialization/   # CoreJsonContext (source-gen JSON)
└── ServiceCollectionExtensions.cs   # AddPzLauncherCore()
```

Rules:

- One folder per feature; namespace `PzLauncher.Core.<Feature>`. A feature holds its interface, implementation, DTOs, and `<Feature>Errors`. No cross-cutting `Models/`, `Services/`, `Interfaces/` folders.
- A feature folder over the size limit splits into subfolders per [Folder size](../common/coding-rules.md#folder-size).
- Mod features nest under `Mod/` (`PzLauncher.Core.Mod.<Mod>`). What any mod needs lives in `Mod/` itself, what any ZombieBuddy Java mod needs in `Mod/ZombieBuddy/`; a mod's own folder keeps only its launch step and facts. `Mod/ZombieBuddy/` names no specific mod; launch steps run in their registration order in `ServiceCollectionExtensions`.
- OS-specific code lives in `Platform/` or OS-guarded files; everything else is portable.
- Steamworks is called only inside the helper process (`Steam/Workshop/Helper/`), through the install's own `steam_api`.
- Domain constants (Steam app ids, install markers, Java locations, game launch files, GC flags, mod folders and `mod.info` keys, ZombieBuddy files, agent args, config location and preload keys, Zabric's mod and item id, ZB Skip Online Checks' mod id, item id, package and game-folder jar, collection and active-list file names and syntax, dedicated server files, ini keys and arguments, release feed URLs and update package/staging names, Steam client files and the launch-hook syntax, Workshop files and Web API, launcher command-line arguments) live in one place each: `Steam/PzSteamApps.cs`, `Steam/SteamClientFiles.cs`, `Steam/Workshop/WorkshopFiles.cs`, `Installs/PzInstallMarkers.cs`, `Java/JavaLocations.cs`, `Launch/PzLaunchFiles.cs`, `Launch/GarbageCollectors.cs`, `Mod/ModFolders.cs`, `Mod/ZombieBuddy/ZombieBuddyFiles.cs`, `Mod/Zabric/ZabricFiles.cs`, `Mod/ZBSkipOnlineChecks/ZBSkipOnlineChecksFiles.cs`, `Mod/Collections/Files/ModCollectionFiles.cs`, `Server/PzServerFiles.cs`, `Update/Releases/LauncherReleases.cs`, `Update/LauncherArguments.cs`.
- Behaviour for another OS is chosen from `IPlatformEnvironment.Os` (executable names, path-list separators), never from the host OS, so Linux paths are testable on Windows. Reference: `Java/JavaDiscoveryService.cs`.
