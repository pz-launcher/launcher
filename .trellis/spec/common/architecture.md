# Architecture

> Projects, dependency direction, and which spec layer covers which project.

---

## Projects

| Project | What it is | Spec layer |
|---|---|---|
| `PzLauncher.Core` | Class library with all launcher logic. UI-agnostic, AOT-compatible. | [core/](../core/index.md) |
| `PzLauncher.Desktop` | Avalonia desktop app (Windows first; macOS, Linux desktop). Thin MVVM shell over Core; the future web UI won't share its ViewModels. Assembly and exe name: `PzLauncher`. | [desktop/](../desktop/index.md) |
| `PzLauncher.Core.Tests` | xUnit v3 tests for Core | [common/test-strategy.md](./test-strategy.md) |
| `PzLauncher.Desktop.Tests` | xUnit v3 tests for desktop ViewModels | [common/test-strategy.md](./test-strategy.md) |
| `PzLauncher.Server` | **Future.** ASP.NET Core host + web UI for headless Linux servers. Web stack and auth not decided. | `server/`: create the layer when the project exists |

Solution file: `PzLauncher.slnx`.

## Dependency direction

```
PzLauncher.Desktop ──┐
PzLauncher.Server ───┼──> PzLauncher.Core
*.Tests ─────────────┘
```

- Core never references a host project, Avalonia, or ASP.NET Core.
- Hosts never reference each other.

## Targets

Windows ships first. macOS and Linux desktop run the same Avalonia app.
Headless Linux runs `PzLauncher.Server`.
