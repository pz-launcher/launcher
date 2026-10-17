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
├── ZombieBuddy/     # ZombieBuddy Workshop mod setup in a game install
├── Zabric/          # Zabric lookup and its entry in ZombieBuddy's preload list
├── Serialization/   # CoreJsonContext (source-gen JSON)
└── ServiceCollectionExtensions.cs   # AddPzLauncherCore()
```

Rules:

- One folder per feature; namespace `PzLauncher.Core.<Feature>`. A feature holds its interface, implementation, DTOs, and `<Feature>Errors`. No cross-cutting `Models/`, `Services/`, `Interfaces/` folders.
- OS-specific code lives in `Platform/` or OS-guarded files; everything else is portable.
- Domain constants (Steam app ids, install markers, Java locations, game launch files, GC flags, ZombieBuddy files, agent args and config location, Zabric files and preload entry) live in one place each: `Steam/PzSteamApps.cs`, `Installs/PzInstallMarkers.cs`, `Java/JavaLocations.cs`, `Launch/PzLaunchFiles.cs`, `Launch/GarbageCollectors.cs`, `ZombieBuddy/ZombieBuddyFiles.cs`, `Zabric/ZabricFiles.cs`.
- Behaviour for another OS is chosen from `IPlatformEnvironment.Os` (executable names, path-list separators), never from the host OS, so Linux paths are testable on Windows. Reference: `Java/JavaDiscoveryService.cs`.
