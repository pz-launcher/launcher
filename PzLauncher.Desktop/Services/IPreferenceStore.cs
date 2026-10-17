using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Themes;

namespace PzLauncher.Desktop.Services;

/// <summary>A <c>null</c> field means no usable saved choice.</summary>
public sealed record UserPreferences(AppLanguage? Language, AppTheme? Theme, WindowPlacement? Window)
{
    /// <summary>Absolute paths; empty when none are saved.</summary>
    public IReadOnlyList<string> ManualJavaHomes { get; init; } = [];

    /// <summary>Keyed by install root as saved (ordinal); empty when none are saved.</summary>
    public IReadOnlyDictionary<string, GameLaunchOverrides> InstallOverrides { get; init; } = new Dictionary<string, GameLaunchOverrides>();

    /// <summary>Keyed by install root as saved (ordinal); empty when none are saved.</summary>
    public IReadOnlyDictionary<string, ZombieBuddyChannel> ZombieBuddyChannels { get; init; } = new Dictionary<string, ZombieBuddyChannel>();

    /// <summary>Collection name per install root as saved (ordinal); empty when none are saved.</summary>
    public IReadOnlyDictionary<string, string> DefaultModCollections { get; init; } = new Dictionary<string, string>();

    // Compares the collections by content; the generated equality would compare references.
    public bool Equals(UserPreferences? other) =>
        other is not null
        && Language == other.Language
        && Theme == other.Theme
        && Window == other.Window
        && ManualJavaHomes.SequenceEqual(other.ManualJavaHomes, StringComparer.Ordinal)
        && InstallOverrides.Count == other.InstallOverrides.Count
        && InstallOverrides.All(entry => other.InstallOverrides.TryGetValue(entry.Key, out var value) && entry.Value == value)
        && ZombieBuddyChannels.Count == other.ZombieBuddyChannels.Count
        && ZombieBuddyChannels.All(entry => other.ZombieBuddyChannels.TryGetValue(entry.Key, out var value) && entry.Value == value)
        && DefaultModCollections.Count == other.DefaultModCollections.Count
        && DefaultModCollections.All(entry => other.DefaultModCollections.TryGetValue(entry.Key, out var value) && entry.Value == value);

    public override int GetHashCode() =>
        HashCode.Combine(Language, Theme, Window, ManualJavaHomes.Count, InstallOverrides.Count, ZombieBuddyChannels.Count, DefaultModCollections.Count);
}

public interface IPreferenceStore
{
    /// <summary>Synchronous: it runs before the first window exists.</summary>
    UserPreferences Load();

    /// <summary>Never throws for I/O failures; they are logged. Keeps the other saved fields.</summary>
    Task SaveLanguageAsync(AppLanguage language, CancellationToken ct = default);

    /// <inheritdoc cref="SaveLanguageAsync"/>
    Task SaveThemeAsync(AppTheme theme, CancellationToken ct = default);

    /// <inheritdoc cref="SaveLanguageAsync"/>
    Task SaveWindowPlacementAsync(WindowPlacement placement, CancellationToken ct = default);

    /// <inheritdoc cref="SaveLanguageAsync"/>
    Task SaveManualJavaHomesAsync(IReadOnlyList<string> homes, CancellationToken ct = default);

    /// <summary>Replaces this install's entry only. <inheritdoc cref="SaveLanguageAsync"/></summary>
    Task SaveInstallOverridesAsync(string installRoot, GameLaunchOverrides overrides, CancellationToken ct = default);

    /// <inheritdoc cref="SaveInstallOverridesAsync"/>
    Task SaveZombieBuddyChannelAsync(string installRoot, ZombieBuddyChannel channel, CancellationToken ct = default);

    /// <summary><c>null</c> removes this install's entry. <inheritdoc cref="SaveInstallOverridesAsync"/></summary>
    Task SaveDefaultModCollectionAsync(string installRoot, string? name, CancellationToken ct = default);
}
