using System;
using System.Collections.Generic;
using System.Linq;

namespace PzLauncher.Desktop.Services.Preferences.Records;

/// <summary>The Steam Workshop collections and items starred in the launcher, each list most recent first.</summary>
public sealed record SavedWorkshopFavorites(IReadOnlyList<SavedWorkshopEntry> Collections, IReadOnlyList<SavedWorkshopEntry> Items)
{
    public static SavedWorkshopFavorites None { get; } = new([], []);

    // Compares the lists by content; the generated equality would compare references.
    public bool Equals(SavedWorkshopFavorites? other) =>
        other is not null && Collections.SequenceEqual(other.Collections) && Items.SequenceEqual(other.Items);

    public override int GetHashCode() => HashCode.Combine(Collections.Count, Items.Count);
}
