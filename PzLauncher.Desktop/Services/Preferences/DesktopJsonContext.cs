using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using PzLauncher.Desktop.Services.Preferences.Records;
using PzLauncher.Desktop.Services.Preferences.Secrets;

namespace PzLauncher.Desktop.Services.Preferences;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PreferencesFile))]
[JsonSerializable(typeof(InstallOverridesFile))]
[JsonSerializable(typeof(ProxyFile))]
[JsonSerializable(typeof(WorkshopEntryFile))]
[JsonSerializable(typeof(WorkshopEntryFile[]))]
[JsonSerializable(typeof(WorkshopUserCollectionsFile))]
[JsonSerializable(typeof(WorkshopUserCollectionFile))]
[JsonSerializable(typeof(WorkshopFavoritesFile))]
[JsonSerializable(typeof(LocalCollectionFile))]
[JsonSerializable(typeof(LocalCollectionFile[]))]
[JsonSerializable(typeof(ModpackFile[]))]
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
/// <param name="WorkshopCollections">An array of <see cref="WorkshopEntryFile"/> objects; an element, so neither a non-array nor one bad entry makes the whole file unreadable.</param>
/// <param name="WorkshopUserCollections">A <see cref="WorkshopUserCollectionsFile"/>; an element for the same reason as <paramref name="JavaRuntimes"/>.</param>
/// <param name="WorkshopFavorites">A <see cref="WorkshopFavoritesFile"/>; an element for the same reason as <paramref name="JavaRuntimes"/>.</param>
/// <param name="AfterLaunch"><see cref="AfterLaunchPreferences.PreferenceName"/>; an element for the same reason as <paramref name="JavaRuntimes"/>.</param>
/// <param name="LocalCollections">An array of <see cref="LocalCollectionFile"/> objects, read like <paramref name="WorkshopCollections"/>.</param>
/// <param name="Modpacks">Keyed by install root; arrays of <see cref="ModpackFile"/> objects, elements for the same reason as <paramref name="JavaRuntimes"/>.</param>
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
    JsonElement? WorkshopUserCollections,
    JsonElement? WorkshopFavorites,
    JsonElement? AfterLaunch,
    JsonElement? LocalCollections,
    IReadOnlyDictionary<string, JsonElement>? Modpacks);

/// <summary>Nullable members, so a missing one is told apart from a zero.</summary>
internal sealed record WindowFile(int? X, int? Y, double? Width, double? Height, bool? Maximized);

/// <param name="Gc"><see cref="GarbageCollectorPreferences.PreferenceName"/>; an unknown value follows the json.</param>
internal sealed record InstallOverridesFile(string? JavaHome, int? MaxMemoryMb, int? MinMemoryMb, string? JvmArgs, string? GameArgs, string? Gc);

/// <param name="Mode"><see cref="ProxyModePreferences.PreferenceName"/>.</param>
/// <param name="Password"><see cref="ISecretProtector"/> output, prefixed with its scheme.</param>
internal sealed record ProxyFile(string? Mode, string? Host, int? Port, string? Username, string? Password);

/// <param name="Id">Decimal text: a JSON number can't hold every 64-bit id exactly in other readers.</param>
internal sealed record WorkshopEntryFile(string? Id, string? Title);

/// <param name="Favorited">Elements, so one bad entry doesn't drop the whole list.</param>
internal sealed record WorkshopUserCollectionsFile(
    uint? AccountId, DateTimeOffset? FetchedAt, IReadOnlyList<JsonElement>? Favorited, IReadOnlyList<JsonElement>? Published);

/// <param name="Id">Decimal text, as in <see cref="WorkshopEntryFile"/>.</param>
internal sealed record WorkshopUserCollectionFile(string? Id, string? Title, uint? ChildCount);

/// <param name="Collections">An array of <see cref="WorkshopEntryFile"/> objects, read like <see cref="PreferencesFile.WorkshopCollections"/>.</param>
/// <param name="Items">As <paramref name="Collections"/>.</param>
internal sealed record WorkshopFavoritesFile(JsonElement? Collections, JsonElement? Items);

/// <param name="Id">A GUID in its "D" form.</param>
/// <param name="Items">An array of <see cref="WorkshopEntryFile"/> objects, read like <see cref="PreferencesFile.WorkshopCollections"/>.</param>
internal sealed record LocalCollectionFile(string? Id, string? Name, JsonElement? Items);

/// <param name="ExtraItems">Item ids as decimal text, as in <see cref="WorkshopEntryFile"/>.</param>
internal sealed record ModpackFile(string? Name, ModpackPresetFile? Preset, ModpackCollectionFile? Collection, IReadOnlyList<JsonElement>? ExtraItems);

/// <param name="Linked">The install's preset the copy follows; missing when the modpack is independent of it.</param>
internal sealed record ModpackPresetFile(string? Linked, ModpackPresetCopyFile? Copy);

/// <param name="ModIds">Strings; elements, so one bad id doesn't drop the whole modpack.</param>
internal sealed record ModpackPresetCopyFile(string? Name, IReadOnlyList<JsonElement>? ModIds);

/// <summary>Exactly one member is set.</summary>
/// <param name="Steam">The collection's id as decimal text, as in <see cref="WorkshopEntryFile"/>.</param>
internal sealed record ModpackCollectionFile(string? Steam, ModpackLocalCollectionFile? Local);

/// <param name="Linked">The local collection's id (a GUID in its "D" form) the copy follows; missing when the modpack is independent of it.</param>
internal sealed record ModpackLocalCollectionFile(string? Linked, ModpackLocalCollectionCopyFile? Copy);

/// <param name="Items">An array of <see cref="WorkshopEntryFile"/> objects, read like <see cref="PreferencesFile.WorkshopCollections"/>.</param>
internal sealed record ModpackLocalCollectionCopyFile(string? Name, JsonElement? Items);
