# Directory Structure (Core)

## Core layout — feature folders

```
PzLauncher.Core/
├── Common/          # Result, Error, ErrorCode, path helpers
├── Platform/        # IPlatformEnvironment + real implementation (OS seam)
├── Steam/           # Steam root/library discovery, Workshop library order, KeyValues parsing
├── Installs/        # PZ game + dedicated server install detection
├── Java/            # Java runtime discovery (Windows + Linux sources)
├── Launch/          # game launch, ProjectZomboid64.json reading and vmArgs editing
├── Mod/             # any PZ mod: mod.info reading, lookup in the game's mod order
│   ├── ZombieBuddy/ # ZombieBuddy setup in a game install; Java mod preload check and preload list
│   └── Zabric/      # Zabric's entry in ZombieBuddy's preload list
├── Serialization/   # CoreJsonContext (source-gen JSON)
└── ServiceCollectionExtensions.cs   # AddPzLauncherCore()
```

Rules:

- One folder per feature; namespace `PzLauncher.Core.<Feature>`. A feature holds its interface, implementation, DTOs, and `<Feature>Errors`. No cross-cutting `Models/`, `Services/`, `Interfaces/` folders.
- Mod features nest under `Mod/` (`PzLauncher.Core.Mod.<Mod>`). What any mod needs lives in `Mod/` itself, what any ZombieBuddy Java mod needs in `Mod/ZombieBuddy/`; a mod's own folder keeps only its service and facts.
- OS-specific code lives in `Platform/` or OS-guarded files; everything else is portable.
- Domain constants (Steam app ids, install markers, Java locations, game launch files, GC flags, mod folders and `mod.info` keys, ZombieBuddy files, agent args, config location and preload keys, Zabric's mod and item id) live in one place each: `Steam/PzSteamApps.cs`, `Installs/PzInstallMarkers.cs`, `Java/JavaLocations.cs`, `Launch/PzLaunchFiles.cs`, `Launch/GarbageCollectors.cs`, `Mod/ModFolders.cs`, `Mod/ZombieBuddy/ZombieBuddyFiles.cs`, `Mod/Zabric/ZabricFiles.cs`.
- Behaviour for another OS is chosen from `IPlatformEnvironment.Os` (executable names, path-list separators), never from the host OS, so Linux paths are testable on Windows. Reference: `Java/JavaDiscoveryService.cs`.
