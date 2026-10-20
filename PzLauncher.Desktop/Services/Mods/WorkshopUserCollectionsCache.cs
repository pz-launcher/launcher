using System;
using System.Collections.Generic;
using System.Linq;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Desktop.Services.Preferences;

namespace PzLauncher.Desktop.Services.Mods;

/// <summary>The last favorited and published collections Steam listed, persisted through <see cref="IPreferenceStore"/>.</summary>
public sealed class WorkshopUserCollectionsCache(IPreferenceStore store, UserPreferences saved, TimeProvider time)
{
    private FetchedWorkshopUserCollections? _last =
        saved.WorkshopUserCollections is { } last ? new FetchedWorkshopUserCollections(last.Collections, last.FetchedAt) : null;

    /// <summary><c>null</c> when none is saved or it belongs to another account.</summary>
    public FetchedWorkshopUserCollections? For(uint accountId) => _last is { } last && last.Collections.AccountId == accountId ? last : null;

    /// <summary>Stamps the time now. Applies at once; the file write is fire-and-forget.</summary>
    public void Save(WorkshopUserCollections collections) => Keep(new FetchedWorkshopUserCollections(collections, time.GetUtcNow()));

    /// <summary>Keeps the titles seen for listed ids, and when Steam answered. Through <see cref="WorkshopTitles"/> only.</summary>
    internal void UpdateTitles(IEnumerable<WorkshopEntry> seen)
    {
        if (_last is not { Collections: var collections } last)
        {
            return;
        }

        var titles = seen.Where(entry => entry.Title is not null).DistinctBy(entry => entry.Id).ToDictionary(entry => entry.Id, entry => entry.Title);
        var favorited = WithTitles(collections.Favorited, titles);
        var published = WithTitles(collections.Published, titles);
        if (favorited.SequenceEqual(collections.Favorited) && published.SequenceEqual(collections.Published))
        {
            return;
        }

        Keep(last with { Collections = collections with { Favorited = favorited, Published = published } });
    }

    private static List<WorkshopCollectionSummary> WithTitles(IReadOnlyList<WorkshopCollectionSummary> summaries, Dictionary<ulong, string?> titles) =>
        [.. summaries.Select(summary => titles.TryGetValue(summary.Id, out var title) ? summary with { Title = title } : summary)];

    private void Keep(FetchedWorkshopUserCollections fetched)
    {
        _last = fetched;

        // SaveWorkshopUserCollectionsAsync never throws for I/O, and a failed save must not undo the change.
        _ = store.SaveWorkshopUserCollectionsAsync(new SavedWorkshopUserCollections(fetched.FetchedAt, fetched.Collections));
    }
}
