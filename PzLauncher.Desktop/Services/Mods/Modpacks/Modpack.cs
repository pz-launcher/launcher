using System;
using System.Collections.Generic;
using System.Linq;
using PzLauncher.Desktop.Services.Mods.Library;
using PzLauncher.Desktop.Services.Preferences.Records;

namespace PzLauncher.Desktop.Services.Mods.Modpacks;

/// <summary>A mod preset of one install paired with a Workshop collection, kept by the launcher.</summary>
/// <param name="ExtraItems">Workshop items of the preset's mods that the collection lacks, in preset order.</param>
public sealed record Modpack(string Name, ModpackPreset Preset, ModpackCollection Collection, IReadOnlyList<ulong> ExtraItems)
{
    // Compares the list by content; the generated equality would compare references.
    public bool Equals(Modpack? other) =>
        other is not null && Name == other.Name && Preset == other.Preset && Collection == other.Collection && ExtraItems.SequenceEqual(other.ExtraItems);

    public override int GetHashCode() => HashCode.Combine(Name, Preset, Collection, ExtraItems.Count);

    public static Modpack FromSaved(SavedModpack saved) =>
        new(
            saved.Name,
            new ModpackPreset(saved.Preset.Name, saved.Preset.ModIds, saved.Preset.IsLinked),
            saved.Collection switch
            {
                SavedModpackCollection.Local local => new ModpackCollection.Local(
                    local.Linked, local.Name, [.. local.Items.Select(item => new WorkshopEntry(item.Id, item.Title))]),
                SavedModpackCollection.Steam steam => new ModpackCollection.Steam(steam.Id),
                _ => throw new ArgumentException($"Unknown modpack collection {saved.Collection}.", nameof(saved)),
            },
            saved.ExtraItems);

    public SavedModpack ToSaved() =>
        new(
            Name,
            new SavedModpackPreset(Preset.Name, Preset.ModIds, Preset.IsLinked),
            Collection switch
            {
                ModpackCollection.Local local => new SavedModpackCollection.Local(
                    local.Linked, local.Name, [.. local.Items.Select(item => new SavedWorkshopEntry(item.Id, item.Title))]),
                ModpackCollection.Steam steam => new SavedModpackCollection.Steam(steam.Id),
                _ => throw new InvalidOperationException($"Unknown modpack collection {Collection}."),
            },
            ExtraItems);
}

/// <summary>The modpack's copy of a mod preset.</summary>
/// <param name="IsLinked">The copy follows the install's preset named <paramref name="Name"/>; otherwise the modpack is independent of it.</param>
public sealed record ModpackPreset(string Name, IReadOnlyList<string> ModIds, bool IsLinked)
{
    // Compares the list by content; the generated equality would compare references.
    public bool Equals(ModpackPreset? other) =>
        other is not null && Name == other.Name && IsLinked == other.IsLinked && ModIds.SequenceEqual(other.ModIds, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Name, IsLinked, ModIds.Count);
}

public abstract record ModpackCollection
{
    private ModpackCollection()
    {
    }

    /// <summary>Referenced by id: its items are read from the Web API.</summary>
    public sealed record Steam(ulong Id) : ModpackCollection;

    /// <summary>The modpack's copy of a local collection.</summary>
    /// <param name="Linked">The local collection the copy follows; <c>null</c> when the modpack is independent of it.</param>
    public sealed record Local(Guid? Linked, string Name, IReadOnlyList<WorkshopEntry> Items) : ModpackCollection
    {
        // Compares the list by content; the generated equality would compare references.
        public bool Equals(Local? other) => other is not null && Linked == other.Linked && Name == other.Name && Items.SequenceEqual(other.Items);

        public override int GetHashCode() => HashCode.Combine(Linked, Name, Items.Count);
    }
}

/// <summary>A modpack that uses a source about to be deleted.</summary>
public sealed record ModpackUser(string InstallRoot, string Name);
