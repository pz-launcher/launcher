using System.Collections.Generic;
using System.Linq;

namespace PzLauncher.Desktop.Services.Mods;

/// <summary>Records the titles seen for Workshop ids in every store that lists them; a store whose titles didn't change isn't saved.</summary>
public sealed class WorkshopTitles(WorkshopCollectionHistory history, WorkshopUserCollectionsCache userCollections, WorkshopFavorites favorites)
{
    /// <param name="seen"><c>null</c> titles are ignored.</param>
    public void SeenCollections(IEnumerable<WorkshopEntry> seen)
    {
        List<WorkshopEntry> titled = [.. seen.Where(entry => entry.Title is not null)];
        history.UpdateTitles(titled);
        userCollections.UpdateTitles(titled);

        // Last: it raises Changed, and its listeners read the other stores.
        favorites.UpdateTitles(WorkshopFavoriteKind.Collection, titled);
    }

    /// <inheritdoc cref="SeenCollections"/>
    public void SeenItems(IEnumerable<WorkshopEntry> seen) => favorites.UpdateTitles(WorkshopFavoriteKind.Item, seen);
}
