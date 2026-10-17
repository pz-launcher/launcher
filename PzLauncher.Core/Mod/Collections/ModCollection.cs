namespace PzLauncher.Core.Mod.Collections;

/// <param name="ModIds">In the launcher's remembered order; ids not installed included.</param>
/// <param name="IsActive">Applying it gives the current enabled list's installed mods (order ignored).</param>
public sealed record ModCollection(string Name, IReadOnlyList<string> ModIds, bool IsActive);
