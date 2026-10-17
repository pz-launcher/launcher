namespace PzLauncher.Core.Mod.Collections;

/// <param name="ModIds">In the launcher's remembered order; ids not installed included.</param>
/// <param name="IsActive">Applying it enables at least one mod and gives the current enabled list's installed mods (order ignored).</param>
/// <param name="IsActiveInOrder"><paramref name="IsActive"/>, and applying it also keeps the enabled list's order of installed mods.</param>
public sealed record ModCollection(string Name, IReadOnlyList<string> ModIds, bool IsActive, bool IsActiveInOrder)
{
    /// <summary>The collection with its in-use flags against the enabled list <paramref name="activeIds"/> (<c>default.txt</c> order).</summary>
    public static ModCollection Create(string name, IReadOnlyList<string> modIds, IReadOnlyList<InstalledMod> installedMods, IReadOnlyList<string> activeIds)
    {
        var installed = installedMods.Select(mod => mod.Id).ToHashSet(StringComparer.Ordinal);
        var activeInstalled = activeIds.Where(installed.Contains).ToList();
        var enabled = ModLoadOrder.Resolve(modIds, installedMods).EnabledIds;
        var isActive = enabled.Count > 0 && enabled.ToHashSet(StringComparer.Ordinal).SetEquals(activeInstalled);
        return new ModCollection(name, modIds, isActive, isActive && enabled.SequenceEqual(activeInstalled, StringComparer.Ordinal));
    }
}
