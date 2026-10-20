using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Update;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.Themes;

namespace PzLauncher.Desktop.Tests.TestSupport;

public sealed class FakePreferenceStore : IPreferenceStore
{
    public List<AppLanguage> SavedLanguages { get; } = [];

    public List<AppTheme> SavedThemes { get; } = [];

    public List<IReadOnlyList<string>> SavedManualJavaHomes { get; } = [];

    public List<(string Root, GameLaunchOverrides Overrides)> SavedInstallOverrides { get; } = [];

    public UserPreferences Load() => new(null, null, null);

    public Task SaveLanguageAsync(AppLanguage language, CancellationToken ct = default)
    {
        SavedLanguages.Add(language);
        return Task.CompletedTask;
    }

    public Task SaveThemeAsync(AppTheme theme, CancellationToken ct = default)
    {
        SavedThemes.Add(theme);
        return Task.CompletedTask;
    }

    public Task SaveWindowPlacementAsync(WindowPlacement placement, CancellationToken ct = default) => Task.CompletedTask;

    public Task SaveManualJavaHomesAsync(IReadOnlyList<string> homes, CancellationToken ct = default)
    {
        SavedManualJavaHomes.Add(homes);
        return Task.CompletedTask;
    }

    /// <summary>Holds install override saves open until completed.</summary>
    public TaskCompletionSource? InstallOverridesSaveGate { get; set; }

    public Task SaveInstallOverridesAsync(string installRoot, GameLaunchOverrides overrides, CancellationToken ct = default)
    {
        SavedInstallOverrides.Add((installRoot, overrides));
        return InstallOverridesSaveGate?.Task ?? Task.CompletedTask;
    }

    public List<(string Root, ZombieBuddyChannel Channel)> SavedZombieBuddyChannels { get; } = [];

    public Task SaveZombieBuddyChannelAsync(string installRoot, ZombieBuddyChannel channel, CancellationToken ct = default)
    {
        SavedZombieBuddyChannels.Add((installRoot, channel));
        return Task.CompletedTask;
    }

    public List<(string Root, string? Name)> SavedDefaultModCollections { get; } = [];

    public Task SaveDefaultModCollectionAsync(string installRoot, string? name, CancellationToken ct = default)
    {
        SavedDefaultModCollections.Add((installRoot, name));
        return Task.CompletedTask;
    }

    public List<UpdateSource> SavedUpdateSources { get; } = [];

    public Task SaveUpdateSourceAsync(UpdateSource source, CancellationToken ct = default)
    {
        SavedUpdateSources.Add(source);
        return Task.CompletedTask;
    }

    public List<bool> SavedAutoCheckUpdates { get; } = [];

    public Task SaveAutoCheckUpdatesAsync(bool enabled, CancellationToken ct = default)
    {
        SavedAutoCheckUpdates.Add(enabled);
        return Task.CompletedTask;
    }

    public List<SavedProxy> SavedProxies { get; } = [];

    /// <summary>Holds proxy saves open until completed.</summary>
    public TaskCompletionSource? ProxySaveGate { get; set; }

    public Task SaveProxyAsync(SavedProxy proxy, CancellationToken ct = default)
    {
        SavedProxies.Add(proxy);
        return ProxySaveGate?.Task ?? Task.CompletedTask;
    }

    public List<bool> SavedSteamHookReminders { get; } = [];

    public Task SaveSteamHookReminderAsync(bool remind, CancellationToken ct = default)
    {
        SavedSteamHookReminders.Add(remind);
        return Task.CompletedTask;
    }

    public List<IReadOnlyList<SavedWorkshopEntry>> SavedWorkshopCollections { get; } = [];

    public Task SaveWorkshopCollectionsAsync(IReadOnlyList<SavedWorkshopEntry> collections, CancellationToken ct = default)
    {
        SavedWorkshopCollections.Add(collections);
        return Task.CompletedTask;
    }

    public List<SavedWorkshopUserCollections> SavedWorkshopUserCollections { get; } = [];

    public Task SaveWorkshopUserCollectionsAsync(SavedWorkshopUserCollections collections, CancellationToken ct = default)
    {
        SavedWorkshopUserCollections.Add(collections);
        return Task.CompletedTask;
    }

    public List<SavedWorkshopFavorites> SavedWorkshopFavorites { get; } = [];

    public Task SaveWorkshopFavoritesAsync(SavedWorkshopFavorites favorites, CancellationToken ct = default)
    {
        SavedWorkshopFavorites.Add(favorites);
        return Task.CompletedTask;
    }

    public List<AfterLaunchAction> SavedAfterLaunch { get; } = [];

    public Task SaveAfterLaunchAsync(AfterLaunchAction action, CancellationToken ct = default)
    {
        SavedAfterLaunch.Add(action);
        return Task.CompletedTask;
    }
}
