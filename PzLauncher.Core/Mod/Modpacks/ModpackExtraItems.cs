namespace PzLauncher.Core.Mod.Modpacks;

/// <summary>The Workshop items a modpack lists after its collection's: those of its preset's mods that the collection lacks.</summary>
public static class ModpackExtraItems
{
    /// <summary>
    /// Keeps a stored item while its mods are unknown here (not installed), drops one that is installed with none of its mods in the preset any
    /// more or that the collection now holds, and appends the items of installed preset mods not listed yet, in preset order.
    /// </summary>
    /// <param name="installed">The install's mods, the copy the game loads of each.</param>
    /// <param name="collectionItems">Empty when the collection couldn't be read: listing items it holds is harmless.</param>
    public static IReadOnlyList<ulong> Compute(
        IReadOnlyList<ulong> stored, IReadOnlyList<string> presetModIds, IReadOnlyList<InstalledMod> installed, IReadOnlyCollection<ulong> collectionItems)
    {
        var preset = presetModIds.ToHashSet(StringComparer.Ordinal);
        var modsByItem = installed.Where(mod => mod.WorkshopId is not null).ToLookup(mod => mod.WorkshopId.GetValueOrDefault(), mod => mod.Id);
        var itemByMod = installed.DistinctBy(mod => mod.Id, StringComparer.Ordinal).ToDictionary(mod => mod.Id, mod => mod.WorkshopId, StringComparer.Ordinal);

        List<ulong> items =
        [
            .. stored
                .Where(id => !collectionItems.Contains(id) && (!modsByItem.Contains(id) || modsByItem[id].Any(preset.Contains)))
                .Distinct(),
        ];
        foreach (var modId in presetModIds)
        {
            if (itemByMod.GetValueOrDefault(modId) is { } id && !collectionItems.Contains(id) && !items.Contains(id))
            {
                items.Add(id);
            }
        }

        return items;
    }

    /// <summary>The extra items to share, as <see cref="Compute"/> gives them, and the preset's mods a receiver can't get from them.</summary>
    public static ModpackShareSummary Summarize(
        IReadOnlyList<ulong> stored, IReadOnlyList<string> presetModIds, IReadOnlyList<InstalledMod> installed, IReadOnlyCollection<ulong> collectionItems)
    {
        // The copy the game loads of each mod, as the extra items take it.
        var loaded = installed.DistinctBy(mod => mod.Id, StringComparer.Ordinal).ToDictionary(mod => mod.Id, StringComparer.Ordinal);
        return new ModpackShareSummary(
            Compute(stored, presetModIds, installed, collectionItems),
            presetModIds.Count(id => !loaded.ContainsKey(id)),
            presetModIds.Count(id => loaded.GetValueOrDefault(id) is { WorkshopId: null }));
    }
}

/// <param name="NotInstalled">Preset mods not installed here, so their Workshop items are unknown.</param>
/// <param name="NotWorkshop">Preset mods installed from outside the Workshop.</param>
public sealed record ModpackShareSummary(IReadOnlyList<ulong> ExtraItems, int NotInstalled, int NotWorkshop);
