using System;
using System.Collections.Generic;
using System.Linq;
using PzLauncher.Core.Platform;

namespace PzLauncher.Desktop.Services;

public sealed class ModCollectionDefaultsService : IModCollectionDefaultsService
{
    private readonly IPreferenceStore _store;
    private readonly Dictionary<string, string> _defaults;

    public ModCollectionDefaultsService(IPreferenceStore store, UserPreferences saved, IPlatformEnvironment platform)
    {
        _store = store;
        _defaults = new Dictionary<string, string>(StringComparer.FromComparison(platform.PathComparison));
        foreach (var (root, name) in saved.DefaultModCollections)
        {
            _defaults.TryAdd(root, name);
        }
    }

    public string? Get(string installRoot) => _defaults.GetValueOrDefault(installRoot);

    public void Save(string installRoot, string name)
    {
        var key = SavedKey(installRoot);
        _defaults[key] = name;

        // SaveDefaultModCollectionAsync never throws for I/O, and a failed save must not undo the change.
        _ = _store.SaveDefaultModCollectionAsync(key, name);
    }

    public void Clear(string installRoot)
    {
        var key = SavedKey(installRoot);
        if (_defaults.Remove(key))
        {
            _ = _store.SaveDefaultModCollectionAsync(key, null);
        }
    }

    // The saved spelling of the path, so a differently cased root replaces its entry instead of adding one.
    private string SavedKey(string installRoot) =>
        _defaults.Keys.FirstOrDefault(root => _defaults.Comparer.Equals(root, installRoot)) ?? installRoot;
}
