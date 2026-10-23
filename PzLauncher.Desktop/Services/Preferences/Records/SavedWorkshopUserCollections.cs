using System;
using System.Linq;
using PzLauncher.Core.Steam.Workshop.Actions;

namespace PzLauncher.Desktop.Services.Preferences.Records;

/// <summary>The last answer Steam gave for the account's favorited and published collections.</summary>
public sealed record SavedWorkshopUserCollections(DateTimeOffset FetchedAt, WorkshopUserCollections Collections)
{
    // Compares the lists by content; the generated equality would compare references.
    public bool Equals(SavedWorkshopUserCollections? other) =>
        other is not null
        && FetchedAt == other.FetchedAt
        && Collections.AccountId == other.Collections.AccountId
        && Collections.Favorited.SequenceEqual(other.Collections.Favorited)
        && Collections.Published.SequenceEqual(other.Collections.Published);

    public override int GetHashCode() => HashCode.Combine(FetchedAt, Collections.AccountId, Collections.Favorited.Count, Collections.Published.Count);
}
