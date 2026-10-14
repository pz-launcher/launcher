# Formatting

> At the end of every conversation that changed code, before the final report.

Format only the `.cs` files created or edited in this conversation (main session
and its sub-agents), never other uncommitted files:

```bash
dotnet format PzLauncher.slnx --include <file> [<file> ...]
```

Paths are repo-relative. Skip if no `.cs` file changed (`dotnet format` ignores
`.axaml`/`.csproj`). Mention in the final report what it changed.
