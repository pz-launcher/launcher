namespace PzLauncher.Core.Mod.Collections;

public sealed record SharedModCollection(string Name, IReadOnlyList<string> ModIds);
