# Directory Structure (Core)

## Core layout — feature folders

```
PzLauncher.Core/
├── Common/          # Result, Error, ErrorCode, path helpers
├── Platform/        # IPlatformEnvironment + real implementation (OS seam)
├── Steam/           # Steam root/library discovery, KeyValues parsing
├── Installs/        # PZ game + dedicated server install detection
├── Serialization/   # CoreJsonContext (source-gen JSON)
└── ServiceCollectionExtensions.cs   # AddPzLauncherCore()
```

Rules:

- One folder per feature; namespace `PzLauncher.Core.<Feature>`. A feature holds its interface, implementation, DTOs, and `<Feature>Errors`. No cross-cutting `Models/`, `Services/`, `Interfaces/` folders.
- OS-specific code lives in `Platform/` or OS-guarded files; everything else is portable.
- Domain constants (Steam app ids, install markers) live in one place each: `Steam/PzSteamApps.cs`, `Installs/PzInstallMarkers.cs`.
