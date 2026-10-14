# Common Guidelines

> Rules that apply to **every** project in the solution. Read
> [architecture.md](./architecture.md) first: it maps each project to its
> spec layer.

| Guide | Description | Status |
|-------|-------------|--------|
| [Architecture](./architecture.md) | Projects, dependency direction, project → spec layer map | Filled |
| [Coding Rules](./coding-rules.md) | Language-level rules: comments, nullability, async, AOT, type and naming conventions | Filled |
| [Test Strategy](./test-strategy.md) | Test projects, levels, rules, build/test gate | Filled |
| [Formatting](./formatting.md) | `dotnet format` this conversation's `.cs` files before the final report | Filled |

Project-specific rules stay in their layer: [core/](../core/index.md)
(`PzLauncher.Core`) and [desktop/](../desktop/index.md) (`PzLauncher.Desktop`).

Spec conventions:

- State rules; reference real code by repo path + symbol (no line numbers). Never paste or paraphrase code. Short hypothetical *Wrong* snippets are fine.
- Each fact lives in one file; other files link to it.
- Create a spec file only when there is real content for it.
