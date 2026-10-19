using System;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Desktop.Services.Preferences;

namespace PzLauncher.Desktop.Services.Mods;

/// <summary>The last favorited and published collections Steam listed, persisted through <see cref="IPreferenceStore"/>.</summary>
public sealed class WorkshopUserCollectionsCache(IPreferenceStore store, UserPreferences saved, TimeProvider time)
{
    private SavedWorkshopUserCollections? _last = saved.WorkshopUserCollections;

    /// <summary><c>null</c> when none is saved or it belongs to another account.</summary>
    public SavedWorkshopUserCollections? For(uint accountId) => _last is { } last && last.Collections.AccountId == accountId ? last : null;

    /// <summary>Stamps the time now. Applies at once; the file write is fire-and-forget.</summary>
    public SavedWorkshopUserCollections Save(WorkshopUserCollections collections)
    {
        var entry = new SavedWorkshopUserCollections(time.GetUtcNow(), collections);
        _last = entry;

        // SaveWorkshopUserCollectionsAsync never throws for I/O, and a failed save must not undo the change.
        _ = store.SaveWorkshopUserCollectionsAsync(entry);
        return entry;
    }
}
