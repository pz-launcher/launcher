using System;
using System.Collections.Generic;
using System.Linq;

namespace PzLauncher.Desktop.Services.Mods;

/// <summary>A mod preset of one install paired with a Workshop collection, kept by the launcher.</summary>
/// <param name="ExtraItems">Workshop items of the preset's mods that the collection lacks, in preset order.</param>
public sealed record Modpack(string Name, ModpackPreset Preset, ModpackCollection Collection, IReadOnlyList<ulong> ExtraItems)
{
    // Compares the list by content; the generated equality would compare references.
    public bool Equals(Modpack? other) =>
        other is not null && Name == other.Name && Preset == other.Preset && Collection == other.Collection && ExtraItems.SequenceEqual(other.ExtraItems);

    public override int GetHashCode() => HashCode.Combine(Name, Preset, Collection, ExtraItems.Count);
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

public static class ModpackName
{
    /// <summary>Non-blank and on one line, so share text can hold it on a line of its own; names are saved trimmed.</summary>
    public static bool IsValid(string name) => name.Trim().Length > 0 && !name.Any(char.IsControl);
}
