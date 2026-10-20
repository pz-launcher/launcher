namespace PzLauncher.Core.Steam.Workshop;

/// <param name="Title"><c>null</c> when the Web API doesn't return the collection's own details.</param>
/// <param name="Items">In collection order, nested collections expanded in place; each item once.</param>
public sealed record WorkshopCollection(ulong Id, string? Title, IReadOnlyList<WorkshopCollectionItem> Items);

/// <param name="Details"><c>null</c> for an item the Web API doesn't show (removed, hidden).</param>
public sealed record WorkshopCollectionItem(ulong Id, WorkshopItemDetails? Details);
