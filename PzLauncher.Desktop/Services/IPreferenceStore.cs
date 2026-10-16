using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PzLauncher.Core.Launch;
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

    // Compares the collections by content; the generated equality would compare references.
    public bool Equals(UserPreferences? other) =>
        other is not null
        && Language == other.Language
        && Theme == other.Theme
        && Window == other.Window
        && ManualJavaHomes.SequenceEqual(other.ManualJavaHomes, StringComparer.Ordinal)
        && InstallOverrides.Count == other.InstallOverrides.Count
        && InstallOverrides.All(entry => other.InstallOverrides.TryGetValue(entry.Key, out var value) && entry.Value == value);

    public override int GetHashCode() => HashCode.Combine(Language, Theme, Window, ManualJavaHomes.Count, InstallOverrides.Count);
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
}
