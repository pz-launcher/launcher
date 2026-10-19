using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PzLauncher.Desktop.Services.Preferences;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PreferencesFile))]
[JsonSerializable(typeof(InstallOverridesFile))]
[JsonSerializable(typeof(ProxyFile))]
[JsonSerializable(typeof(WorkshopCollectionFile))]
[JsonSerializable(typeof(WorkshopCollectionFile[]))]
[JsonSerializable(typeof(WorkshopUserCollectionsFile))]
[JsonSerializable(typeof(WorkshopUserCollectionFile))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
internal sealed partial class DesktopJsonContext : JsonSerializerContext;

/// <summary>Stable strings, not enum names (<see cref="Localization.AppLanguages.CultureName"/>, <see cref="Themes.AppThemes.PreferenceName"/>), so renaming an enum keeps saved files valid.</summary>
/// <param name="JavaRuntimes">Elements, not strings, so one non-string entry doesn't make the whole file unreadable.</param>
/// <param name="Installs">Keyed by install root; elements for the same reason as <paramref name="JavaRuntimes"/>.</param>
/// <param name="ZombieBuddyChannels">Keyed by install root; <see cref="ZombieBuddyChannelPreferences.PreferenceName"/> values, elements for the same reason as <paramref name="JavaRuntimes"/>.</param>
/// <param name="DefaultModCollections">Keyed by install root; collection names, elements for the same reason as <paramref name="JavaRuntimes"/>.</param>
/// <param name="UpdateSource"><see cref="UpdateSourcePreferences.PreferenceName"/>; an element for the same reason as <paramref name="JavaRuntimes"/>.</param>
/// <param name="AutoCheckUpdates">A bool; an element for the same reason as <paramref name="JavaRuntimes"/>.</param>
/// <param name="Proxy">A <see cref="ProxyFile"/>; an element for the same reason as <paramref name="JavaRuntimes"/>.</param>
/// <param name="SteamHookReminder">A bool; an element for the same reason as <paramref name="JavaRuntimes"/>.</param>
/// <param name="WorkshopCollections">An array of <see cref="WorkshopCollectionFile"/> objects; an element, so neither a non-array nor one bad entry makes the whole file unreadable.</param>
/// <param name="WorkshopUserCollections">A <see cref="WorkshopUserCollectionsFile"/>; an element for the same reason as <paramref name="JavaRuntimes"/>.</param>
internal sealed record PreferencesFile(
    string? Language,
    string? Theme,
    WindowFile? Window,
    IReadOnlyList<JsonElement>? JavaRuntimes,
    IReadOnlyDictionary<string, JsonElement>? Installs,
    IReadOnlyDictionary<string, JsonElement>? ZombieBuddyChannels,
    IReadOnlyDictionary<string, JsonElement>? DefaultModCollections,
    JsonElement? UpdateSource,
    JsonElement? AutoCheckUpdates,
    JsonElement? Proxy,
    JsonElement? SteamHookReminder,
    JsonElement? WorkshopCollections,
    JsonElement? WorkshopUserCollections);

/// <summary>Nullable members, so a missing one is told apart from a zero.</summary>
internal sealed record WindowFile(int? X, int? Y, double? Width, double? Height, bool? Maximized);

/// <param name="Gc"><see cref="GarbageCollectorPreferences.PreferenceName"/>; an unknown value follows the json.</param>
internal sealed record InstallOverridesFile(string? JavaHome, int? MaxMemoryMb, int? MinMemoryMb, string? JvmArgs, string? GameArgs, string? Gc);

/// <param name="Mode"><see cref="ProxyModePreferences.PreferenceName"/>.</param>
/// <param name="Password"><see cref="ISecretProtector"/> output, prefixed with its scheme.</param>
internal sealed record ProxyFile(string? Mode, string? Host, int? Port, string? Username, string? Password);

/// <param name="Id">Decimal text: a JSON number can't hold every 64-bit id exactly in other readers.</param>
internal sealed record WorkshopCollectionFile(string? Id, string? Title);

/// <param name="Favorited">Elements, so one bad entry doesn't drop the whole list.</param>
internal sealed record WorkshopUserCollectionsFile(
    uint? AccountId, DateTimeOffset? FetchedAt, IReadOnlyList<JsonElement>? Favorited, IReadOnlyList<JsonElement>? Published);

/// <param name="Id">Decimal text, as in <see cref="WorkshopCollectionFile"/>.</param>
internal sealed record WorkshopUserCollectionFile(string? Id, string? Title, uint? ChildCount);
