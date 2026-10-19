using System.Collections.Generic;
using PzLauncher.Desktop.Services.Preferences;

namespace PzLauncher.Desktop.Services.Mods;

/// <summary>Workshop ids with their last known titles, most recent first, each id once. Every change returns whether the list changed.</summary>
internal sealed class WorkshopEntryList(IEnumerable<SavedWorkshopEntry> saved)
{
    private readonly List<SavedWorkshopEntry> _entries = [.. saved];

    public IReadOnlyList<SavedWorkshopEntry> Entries => _entries;

    public bool Contains(ulong id) => _entries.Exists(entry => entry.Id == id);

    /// <param name="title"><c>null</c> keeps the title already known.</param>
    public bool MoveToTop(ulong id, string? title)
    {
        var index = _entries.FindIndex(entry => entry.Id == id);
        var entry = new SavedWorkshopEntry(id, title ?? (index >= 0 ? _entries[index].Title : null));
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

    /// <summary>Keeps the entry's place; an id not in the list or a <c>null</c> title changes nothing.</summary>
    public bool SetTitle(ulong id, string? title)
    {
        var index = _entries.FindIndex(entry => entry.Id == id);
        if (index < 0 || title is null || _entries[index].Title == title)
        {
            return false;
        }

        _entries[index] = _entries[index] with { Title = title };
        return true;
    }

    public SavedWorkshopEntry[] ToArray() => [.. _entries];
}
