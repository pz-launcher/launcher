using System.Collections.Generic;
using PzLauncher.Desktop.Services.Preferences;

namespace PzLauncher.Desktop.Services.Mods;

/// <summary>The Steam Workshop collections opened in the launcher, most recent first, persisted through <see cref="IPreferenceStore"/>.</summary>
public sealed class WorkshopCollectionHistory(IPreferenceStore store, UserPreferences saved)
{
    private readonly List<SavedWorkshopCollection> _items = [.. saved.WorkshopCollections];

    public IReadOnlyList<SavedWorkshopCollection> Items => _items;

    /// <summary>Moves the collection to the top with its current title. Applies at once; the file write is fire-and-forget.</summary>
    public void Remember(ulong id, string? title)
    {
        var index = _items.FindIndex(item => item.Id == id);
        var entry = new SavedWorkshopCollection(id, title ?? (index >= 0 ? _items[index].Title : null));
        if (index == 0 && _items[0] == entry)
        {
            return;
        }

        if (index >= 0)
        {
            _items.RemoveAt(index);
        }

        _items.Insert(0, entry);
        Save();
    }

    /// <summary>Applies at once; the file write is fire-and-forget.</summary>
    public void Forget(ulong id)
    {
        if (_items.RemoveAll(item => item.Id == id) > 0)
        {
            Save();
        }
    }

    // SaveWorkshopCollectionsAsync never throws for I/O, and a failed save must not undo the change.
    private void Save() => _ = store.SaveWorkshopCollectionsAsync([.. _items]);
}
