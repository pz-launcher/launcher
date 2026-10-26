using System;
using System.Collections.Generic;
using System.Linq;
using PzLauncher.Desktop.Services.Preferences.Records;

namespace PzLauncher.Desktop.Services.Mods.Library;

/// <summary>A modpack as the library file holds it.</summary>
/// <param name="ExtraItems">In preset order.</param>
public sealed record SavedModpack(string Name, SavedModpackPreset Preset, SavedModpackCollection Collection, IReadOnlyList<ulong> ExtraItems)
{
    // Compares the list by content; the generated equality would compare references.
    public bool Equals(SavedModpack? other) =>
        other is not null && Name == other.Name && Preset == other.Preset && Collection == other.Collection && ExtraItems.SequenceEqual(other.ExtraItems);

    public override int GetHashCode() => HashCode.Combine(Name, Preset, Collection, ExtraItems.Count);
}

/// <param name="IsLinked">The copy follows the install's preset named <paramref name="Name"/>.</param>
public sealed record SavedModpackPreset(string Name, IReadOnlyList<string> ModIds, bool IsLinked)
{
    // Compares the list by content; the generated equality would compare references.
    public bool Equals(SavedModpackPreset? other) =>
        other is not null && Name == other.Name && IsLinked == other.IsLinked && ModIds.SequenceEqual(other.ModIds, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Name, IsLinked, ModIds.Count);
}

public abstract record SavedModpackCollection
{
    private SavedModpackCollection()
    {
    }

    public sealed record Steam(ulong Id) : SavedModpackCollection;

    /// <param name="Linked">The local collection the copy follows; <c>null</c> when independent of it.</param>
    public sealed record Local(Guid? Linked, string Name, IReadOnlyList<SavedWorkshopEntry> Items) : SavedModpackCollection
    {
        // Compares the list by content; the generated equality would compare references.
        public bool Equals(Local? other) => other is not null && Linked == other.Linked && Name == other.Name && Items.SequenceEqual(other.Items);

        public override int GetHashCode() => HashCode.Combine(Linked, Name, Items.Count);
    }
}
