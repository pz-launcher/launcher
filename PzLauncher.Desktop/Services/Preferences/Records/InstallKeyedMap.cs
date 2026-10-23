using System;
using System.Collections.Generic;
using System.Linq;

namespace PzLauncher.Desktop.Services.Preferences.Records;

/// <summary>Saved per-install values keyed by install root under the platform's path comparison.</summary>
public sealed class InstallKeyedMap<T>
    where T : notnull
{
    private readonly Dictionary<string, T> _values;

    public InstallKeyedMap(IReadOnlyDictionary<string, T> saved, StringComparison pathComparison)
    {
        _values = new Dictionary<string, T>(StringComparer.FromComparison(pathComparison));
        foreach (var (root, value) in saved)
        {
            _values.TryAdd(root, value);
        }
    }

    public T this[string installRoot] => _values[installRoot];

    public T? GetValueOrDefault(string installRoot) => _values.GetValueOrDefault(installRoot);

    public T GetValueOrDefault(string installRoot, T defaultValue) => _values.GetValueOrDefault(installRoot, defaultValue);

    /// <summary>Returns the key to persist the value under.</summary>
    public string Set(string installRoot, T value)
    {
        var key = SavedKey(installRoot);
        _values[key] = value;
        return key;
    }

    /// <summary>Returns the key to persist the removal under, or null when there was no value.</summary>
    public string? Remove(string installRoot)
    {
        var key = SavedKey(installRoot);
        return _values.Remove(key) ? key : null;
    }

    // The saved spelling of the path, so a differently cased root replaces its entry instead of adding one.
    private string SavedKey(string installRoot) =>
        _values.Keys.FirstOrDefault(root => _values.Comparer.Equals(root, installRoot)) ?? installRoot;
}
