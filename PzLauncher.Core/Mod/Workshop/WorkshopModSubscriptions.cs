using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.Collections;

namespace PzLauncher.Core.Mod.Workshop;

/// <param name="Items">In the order of Steam's subscription file.</param>
/// <param name="Warnings">Files that could only be read in part: a library's install list, the mod collections.</param>
public sealed record WorkshopModSubscriptions(IReadOnlyList<WorkshopModItem> Items, IReadOnlyList<Error> Warnings)
{
    /// <summary>These items with <see cref="WorkshopModItem.Collections"/> taken from the install's collections as read since, without reading the items again.</summary>
    public WorkshopModSubscriptions WithCollections(IReadOnlyList<ModCollection> collections) =>
        this with { Items = [.. Items.Select(item => item with { Collections = WorkshopModItem.UsedBy(item.Mods, collections) })] };
}
