using PzLauncher.Core.Platform;
using PzLauncher.Desktop.Services.Preferences;

namespace PzLauncher.Desktop.Services.Mods;

public sealed class ModCollectionDefaultsService(IPreferenceStore store, UserPreferences saved, IPlatformEnvironment platform) : IModCollectionDefaultsService
{
    private readonly IPreferenceStore _store = store;
    private readonly InstallKeyedMap<string> _defaults = new InstallKeyedMap<string>(saved.DefaultModCollections, platform.PathComparison);

    public string? Get(string installRoot) => _defaults.GetValueOrDefault(installRoot);

    public void Save(string installRoot, string name)
    {
        var key = _defaults.Set(installRoot, name);

        // SaveDefaultModCollectionAsync never throws for I/O, and a failed save must not undo the change.
        _ = _store.SaveDefaultModCollectionAsync(key, name);
    }

    public void Clear(string installRoot)
    {
        if (_defaults.Remove(installRoot) is { } key)
        {
            _ = _store.SaveDefaultModCollectionAsync(key, null);
        }
    }
}
