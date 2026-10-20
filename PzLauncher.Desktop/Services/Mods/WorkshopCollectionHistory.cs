using System.Collections.Generic;
using PzLauncher.Desktop.Services.Preferences;

namespace PzLauncher.Desktop.Services.Mods;

/// <summary>The Steam Workshop collections opened in the launcher, most recent first, persisted through <see cref="IPreferenceStore"/>.</summary>
public sealed class WorkshopCollectionHistory(IPreferenceStore store, UserPreferences saved)
{
    private readonly WorkshopEntryList _items = new(saved.WorkshopCollections);

    public IReadOnlyList<WorkshopEntry> Items => _items.Entries;

    /// <summary>Moves the collection to the top with its current title. Applies at once; the file write is fire-and-forget.</summary>
    public void Remember(ulong id, string? title)
    {
        if (_items.MoveToTop(id, title))
        {
            Save();
        }
    }

    /// <summary>Applies at once; the file write is fire-and-forget.</summary>
    public void Forget(ulong id)
    {
        if (_items.Remove(id))
        {
            Save();
        }
    }

    /// <summary>Through <see cref="WorkshopTitles"/> only.</summary>
    internal void UpdateTitles(IEnumerable<WorkshopEntry> seen)
    {
        if (_items.SetTitles(seen))
        {
            Save();
        }
    }

    // SaveWorkshopCollectionsAsync never throws for I/O, and a failed save must not undo the change.
    private void Save() => _ = store.SaveWorkshopCollectionsAsync(_items.ToSaved());
}
