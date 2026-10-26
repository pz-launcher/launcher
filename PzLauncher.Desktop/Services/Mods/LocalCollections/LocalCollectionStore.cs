using System;
using System.Collections.Generic;
using System.Linq;
using PzLauncher.Core.Mod.Modpacks;
using PzLauncher.Desktop.Services.Mods.Library;
using PzLauncher.Desktop.Services.Preferences.Records;

namespace PzLauncher.Desktop.Services.Mods.LocalCollections;

/// <summary>A named list of Steam Workshop items kept by the launcher, each id once, in the user's order.</summary>
/// <param name="Id">Stable across renames.</param>
public sealed record LocalCollection(Guid Id, string Name, IReadOnlyList<WorkshopEntry> Items);

/// <summary>
/// The launcher's local collections, shared by every install and Steam account, persisted through <see cref="IModLibraryFile"/>.
/// Names are unique (ordinal) and valid by <see cref="ModpackNames"/>. Every change applies at once; the file write is fire-and-forget.
/// </summary>
public sealed class LocalCollectionStore(IModLibraryFile file, ModLibrary saved)
{
    private readonly List<LocalCollection> _collections =
    [
        .. saved.LocalCollections.Select(collection =>
            new LocalCollection(collection.Id, collection.Name, [.. collection.Items.Select(item => new WorkshopEntry(item.Id, item.Title))])),
    ];

    /// <summary>Raised after each change that applied, a new title included; never when nothing changed.</summary>
    public event EventHandler? Changed;

    /// <summary>In the user's order: new ones go to the end.</summary>
    public IReadOnlyList<LocalCollection> All => _collections;

    public LocalCollection? Find(Guid id) => _collections.Find(collection => collection.Id == id);

    /// <param name="except">The collection being renamed, which its own name doesn't clash with.</param>
    public bool IsNameTaken(string name, Guid? except = null)
    {
        var trimmed = name.Trim();
        return _collections.Exists(collection => collection.Name == trimmed && collection.Id != except);
    }

    /// <summary>A repeated id is kept once, where it first appears.</summary>
    public LocalCollection Create(string name, IReadOnlyList<WorkshopEntry> items) => TryCreate(name, items) ?? throw NotFree(name);

    /// <summary><see cref="Create"/>; <c>null</c>, changing nothing, when the name is invalid or taken.</summary>
    public LocalCollection? TryCreate(string name, IReadOnlyList<WorkshopEntry> items)
    {
        if (!IsFreeName(name, except: null))
        {
            return null;
        }

        var created = new LocalCollection(Guid.NewGuid(), name.Trim(), [.. items.DistinctBy(item => item.Id)]);
        _collections.Add(created);
        Commit();
        return created;
    }

    public void Rename(Guid id, string name)
    {
        var trimmed = CheckedName(name, id);
        Update(id, collection => collection.Name == trimmed ? null : collection with { Name = trimmed });
    }

    /// <summary>Callers ask the user first.</summary>
    public void Delete(Guid id)
    {
        if (_collections.RemoveAll(collection => collection.Id == id) > 0)
        {
            Commit();
        }
    }

    /// <summary>Appends the ids not in the collection yet, in the given order, and counts them.</summary>
    public int Add(Guid id, IReadOnlyList<WorkshopEntry> items)
    {
        var added = 0;
        Update(id, collection =>
        {
            List<WorkshopEntry> fresh = [.. items.Where(item => !collection.Items.Any(listed => listed.Id == item.Id)).DistinctBy(item => item.Id)];
            added = fresh.Count;
            return added == 0 ? null : collection with { Items = [.. collection.Items, .. fresh] };
        });
        return added;
    }

    /// <summary>Replaces the collection's items, each id once; a title known for an id is kept when the new entry has none.</summary>
    /// <returns>The collection as replaced; <c>null</c> when there is none with this id.</returns>
    public LocalCollection? Replace(Guid id, IReadOnlyList<WorkshopEntry> items)
    {
        Update(id, collection =>
        {
            List<WorkshopEntry> replaced =
            [
                .. items.DistinctBy(item => item.Id).Select(item =>
                    item with { Title = item.Title ?? collection.Items.FirstOrDefault(listed => listed.Id == item.Id)?.Title }),
            ];
            return replaced.SequenceEqual(collection.Items) ? null : collection with { Items = replaced };
        });
        return Find(id);
    }

    public void Remove(Guid id, ulong itemId) =>
        Update(id, collection => collection.Items.Any(item => item.Id == itemId)
            ? collection with { Items = [.. collection.Items.Where(item => item.Id != itemId)] }
            : null);

    /// <summary>Indexes out of range change nothing.</summary>
    public void Move(Guid id, int from, int to) =>
        Update(id, collection =>
        {
            var count = collection.Items.Count;
            if (from == to || from < 0 || to < 0 || from >= count || to >= count)
            {
                return null;
            }

            List<WorkshopEntry> items = [.. collection.Items];
            var item = items[from];
            items.RemoveAt(from);
            items.Insert(to, item);
            return collection with { Items = items };
        });

    /// <summary>Keeps the titles seen for listed ids in every collection; other ids and unknown titles are ignored. Through <see cref="WorkshopTitles"/> only.</summary>
    internal void UpdateTitles(IEnumerable<WorkshopEntry> seen)
    {
        var titles = new Dictionary<ulong, string>();
        foreach (var (id, title) in seen)
        {
            if (title is not null)
            {
                titles[id] = title;
            }
        }

        var changed = false;
        for (var i = 0; i < _collections.Count; i++)
        {
            var collection = _collections[i];
            if (collection.Items.Any(item => titles.TryGetValue(item.Id, out var title) && item.Title != title))
            {
                _collections[i] = collection with
                {
                    Items = [.. collection.Items.Select(item => titles.TryGetValue(item.Id, out var title) ? item with { Title = title } : item)],
                };
                changed = true;
            }
        }

        if (changed)
        {
            Commit();
        }
    }

    private bool IsFreeName(string name, Guid? except) => ModpackNames.IsValid(name) && !IsNameTaken(name, except);

    private string CheckedName(string name, Guid? except) => IsFreeName(name, except) ? name.Trim() : throw NotFree(name);

    // A name the caller didn't validate is a bug, not an expected failure.
    private static ArgumentException NotFree(string name) => new($"'{name}' is not a free local collection name.", nameof(name));

    /// <param name="change">The changed collection; <c>null</c> when nothing changes.</param>
    private void Update(Guid id, Func<LocalCollection, LocalCollection?> change)
    {
        var index = _collections.FindIndex(collection => collection.Id == id);
        if (index >= 0 && change(_collections[index]) is { } changed)
        {
            _collections[index] = changed;
            Commit();
        }
    }

    private void Commit()
    {
        SavedLocalCollection[] collections =
        [
            .. _collections.Select(collection =>
                new SavedLocalCollection(collection.Id, collection.Name, [.. collection.Items.Select(item => new SavedWorkshopEntry(item.Id, item.Title))])),
        ];

        // SaveLocalCollectionsAsync never throws for I/O, and a failed save must not undo the change.
        _ = file.SaveLocalCollectionsAsync(collections);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
