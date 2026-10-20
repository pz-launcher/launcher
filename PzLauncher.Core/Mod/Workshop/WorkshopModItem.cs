using PzLauncher.Core.Steam.Workshop;

namespace PzLauncher.Core.Mod.Workshop;

/// <summary>A Workshop item with the mods the game finds in its installed copy.</summary>
/// <param name="Mods">In folder order; empty when not installed. A mod the game loads from another copy is left out, unless all of the item's are.</param>
/// <param name="Collections">Names of the install's mod collections that list one of <paramref name="Mods"/>.</param>
public sealed record WorkshopModItem(WorkshopItem Item, IReadOnlyList<InstalledMod> Mods, IReadOnlyList<string> Collections)
{
    /// <summary>
    /// A list's items (a collection's, the starred ones) in list order: a subscribed one as <paramref name="subscriptions"/> has it, the list's
    /// own details filling in a title not fetched yet; any other one not subscribed.
    /// </summary>
    public static IReadOnlyList<WorkshopModItem> InListOrder(IReadOnlyList<WorkshopCollectionItem> items, IReadOnlyList<WorkshopModItem> subscriptions)
    {
        var subscribed = new Dictionary<ulong, WorkshopModItem>();
        foreach (var subscription in subscriptions)
        {
            subscribed.TryAdd(subscription.Item.Id, subscription);
        }

        return items.Select(entry => subscribed.TryGetValue(entry.Id, out var item)
                ? item.Item.Latest is null && entry.Details is not null ? item with { Item = item.Item with { Latest = entry.Details } } : item
                : new WorkshopModItem(WorkshopItem.NotSubscribed(entry.Id, entry.Details), [], []))
            .ToList();
    }
}
