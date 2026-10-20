using System.Collections.Generic;
using System.Linq;
using PzLauncher.Desktop.Services.Preferences;

namespace PzLauncher.Desktop.Services.Mods;

/// <summary>Workshop ids with their last known titles, most recent first, each id once. Every change returns whether the list changed.</summary>
internal sealed class WorkshopEntryList(IEnumerable<SavedWorkshopEntry> saved)
{
    private readonly List<WorkshopEntry> _entries = [.. saved.Select(entry => new WorkshopEntry(entry.Id, entry.Title))];

    public IReadOnlyList<WorkshopEntry> Entries => _entries;

    public bool Contains(ulong id) => _entries.Exists(entry => entry.Id == id);

    /// <param name="title"><c>null</c> keeps the title already known.</param>
    public bool MoveToTop(ulong id, string? title)
    {
        var index = _entries.FindIndex(entry => entry.Id == id);
        var entry = new WorkshopEntry(id, title ?? (index >= 0 ? _entries[index].Title : null));
        if (index == 0 && _entries[0] == entry)
        {
            return false;
        }

        if (index >= 0)
        {
            _entries.RemoveAt(index);
        }

        _entries.Insert(0, entry);
        return true;
    }

    public bool Remove(ulong id) => _entries.RemoveAll(entry => entry.Id == id) > 0;

    /// <summary>Keeps each entry's place; ids not in the list and <c>null</c> titles change nothing.</summary>
    public bool SetTitles(IEnumerable<WorkshopEntry> seen)
    {
        var changed = false;
        foreach (var (id, title) in seen)
        {
            var index = _entries.FindIndex(entry => entry.Id == id);
            if (index >= 0 && title is not null && _entries[index].Title != title)
            {
                _entries[index] = _entries[index] with { Title = title };
                changed = true;
            }
        }

        return changed;
    }

    public SavedWorkshopEntry[] ToSaved() => [.. _entries.Select(entry => new SavedWorkshopEntry(entry.Id, entry.Title))];
}
