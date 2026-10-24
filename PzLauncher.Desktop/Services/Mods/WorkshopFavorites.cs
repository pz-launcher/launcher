using System;
using System.Collections.Generic;
using System.Linq;
using PzLauncher.Desktop.Services.Preferences.Records;
using PzLauncher.Desktop.Services.Preferences;

namespace PzLauncher.Desktop.Services.Mods;

public enum WorkshopFavoriteKind
{
    Collection,
    Item,
}

/// <summary>
/// The Steam Workshop collections and items starred in the launcher, most recent first, persisted through <see cref="IPreferenceStore"/>.
/// They are the launcher's own, never sent to Steam and shared by every Steam account.
/// </summary>
public sealed class WorkshopFavorites(IPreferenceStore store, UserPreferences saved)
{
    private readonly WorkshopEntryList _collections = new(saved.WorkshopFavorites.Collections);
    private readonly WorkshopEntryList _items = new(saved.WorkshopFavorites.Items);

    /// <summary>Raised after each change that applied, a new title included; never when nothing changed.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<WorkshopEntry> Collections => _collections.Entries;

    public IReadOnlyList<WorkshopEntry> Items => _items.Entries;

    public bool IsFavorite(WorkshopFavoriteKind kind, ulong id) => List(kind).Contains(id);

    /// <summary>Stars the id on top, or unstars it. Applies at once; the file write is fire-and-forget.</summary>
    /// <param name="title"><c>null</c> when unknown.</param>
    public void Toggle(WorkshopFavoriteKind kind, ulong id, string? title)
    {
        var list = List(kind);
        if (!list.Remove(id))
        {
            list.MoveToTop(id, title);
        }

        Commit();
    }

    /// <summary>Keeps the titles seen for starred ids; other ids and unknown titles are ignored. Through <see cref="WorkshopTitles"/> only.</summary>
    internal void UpdateTitles(WorkshopFavoriteKind kind, IEnumerable<WorkshopEntry> seen)
    {
        if (List(kind).SetTitles(seen))
        {
            Commit();
        }
    }

    /// <summary>Stars the ids not starred yet, on top in the given order, and counts them; starred ones keep their place and nothing is removed.</summary>
    public (int Collections, int Items) Add(IReadOnlyList<WorkshopEntry> collections, IReadOnlyList<WorkshopEntry> items)
    {
        var added = (AddNew(_collections, collections), AddNew(_items, items));
        if (added != (0, 0))
        {
            Commit();
        }

        return added;
    }

    private static int AddNew(WorkshopEntryList list, IReadOnlyList<WorkshopEntry> entries)
    {
        List<WorkshopEntry> added = [.. entries.Where(entry => !list.Contains(entry.Id)).DistinctBy(entry => entry.Id)];
        for (var i = added.Count - 1; i >= 0; i--)
        {
            list.MoveToTop(added[i].Id, added[i].Title);
        }

        return added.Count;
    }

    private WorkshopEntryList List(WorkshopFavoriteKind kind) => kind switch
    {
        WorkshopFavoriteKind.Collection => _collections,
        WorkshopFavoriteKind.Item => _items,
    };

    private void Commit()
    {
        // SaveWorkshopFavoritesAsync never throws for I/O, and a failed save must not undo the change.
        _ = store.SaveWorkshopFavoritesAsync(new SavedWorkshopFavorites(_collections.ToSaved(), _items.ToSaved()));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
