using System;
using System.Collections.Generic;
using System.Linq;
using PzLauncher.Core.Mod;

namespace PzLauncher.Desktop.Services.Mods;

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
}
