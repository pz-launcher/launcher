namespace PzLauncher.Core.Steam.Workshop;

/// <param name="IsCollection">The Web API shows the id as a collection; any other id counts as an item.</param>
/// <param name="Title">A collection's title; <c>null</c> for an item, or when the Web API gives none.</param>
public sealed record WorkshopIdKind(ulong Id, bool IsCollection, string? Title);
