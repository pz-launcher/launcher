using System.Collections.Generic;

namespace PzLauncher.Desktop.ViewModels.Mods;

/// <summary>The ticked rows of a list, by key, so a reload keeps what was ticked.</summary>
internal sealed class RowSelection<TKey>(IEqualityComparer<TKey>? comparer = null)
    where TKey : notnull
{
    private readonly HashSet<TKey> _keys = new(comparer);

    public int Count => _keys.Count;

    public bool Contains(TKey key) => _keys.Contains(key);

    public void Set(TKey key, bool selected)
    {
        if (selected)
        {
            _keys.Add(key);
        }
        else
        {
            _keys.Remove(key);
        }
    }

    /// <summary>Drops the keys a reload no longer lists.</summary>
    public void IntersectWith(IEnumerable<TKey> keys) => _keys.IntersectWith(keys);
}
