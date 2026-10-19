namespace PzLauncher.Core.Steam.Workshop.Actions;

/// <summary>The game's Workshop collections the signed-in Steam account favorited or published, in Steam's order (newest first).</summary>
/// <param name="AccountId">The account Steam answered for: the low 32 bits of its SteamID64.</param>
public sealed record WorkshopUserCollections(
    uint AccountId, IReadOnlyList<WorkshopCollectionSummary> Favorited, IReadOnlyList<WorkshopCollectionSummary> Published);

/// <param name="Title"><c>null</c> when Steam returned none.</param>
/// <param name="ChildCount">Direct children; a nested collection counts once.</param>
public sealed record WorkshopCollectionSummary(ulong Id, string? Title, uint ChildCount);
