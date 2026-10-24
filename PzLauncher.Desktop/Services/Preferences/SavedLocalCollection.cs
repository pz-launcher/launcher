using System;
using System.Collections.Generic;
using System.Linq;
using PzLauncher.Desktop.Services.Preferences.Records;

namespace PzLauncher.Desktop.Services.Preferences;

/// <summary>A named list of Steam Workshop items kept by the launcher, in the user's order.</summary>
/// <param name="Id">Stable across renames, so other saved values can refer to it.</param>
public sealed record SavedLocalCollection(Guid Id, string Name, IReadOnlyList<SavedWorkshopEntry> Items)
{
    // Compares the list by content; the generated equality would compare references.
    public bool Equals(SavedLocalCollection? other) =>
        other is not null && Id == other.Id && Name == other.Name && Items.SequenceEqual(other.Items);

    public override int GetHashCode() => HashCode.Combine(Id, Name, Items.Count);
}
