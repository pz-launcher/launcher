using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using PzLauncher.Desktop.Services.Preferences;

namespace PzLauncher.Desktop.Services.Mods.Library;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LibraryFile))]
[JsonSerializable(typeof(LocalCollectionFile))]
[JsonSerializable(typeof(LocalCollectionFile[]))]
[JsonSerializable(typeof(ModpackFile))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(Dictionary<string, ModpackFile[]>))]
internal sealed partial class ModLibraryJsonContext : JsonSerializerContext;

/// <param name="LocalCollections">An array of <see cref="LocalCollectionFile"/> objects; an element, so neither a non-array nor one bad entry makes the whole file unreadable.</param>
/// <param name="Modpacks">An object keyed by install root holding arrays of <see cref="ModpackFile"/> objects; an element for the same reason.</param>
internal sealed record LibraryFile(JsonElement? LocalCollections, JsonElement? Modpacks)
{
    /// <summary>Fields this build doesn't know (another launcher version's), written back unchanged.</summary>
    // set, not init: System.Text.Json rejects init-only extension data.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <param name="Id">A GUID in its "D" form.</param>
/// <param name="Items">An array of <see cref="WorkshopEntryFile"/> objects, read like <see cref="LibraryFile.LocalCollections"/>.</param>
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

/// <param name="Items">An array of <see cref="WorkshopEntryFile"/> objects, read like <see cref="LibraryFile.LocalCollections"/>.</param>
internal sealed record ModpackLocalCollectionCopyFile(string? Name, JsonElement? Items);
