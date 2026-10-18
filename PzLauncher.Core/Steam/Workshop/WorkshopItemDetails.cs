namespace PzLauncher.Core.Steam.Workshop;

/// <summary>An item's latest published version as the Steam Web API reports it.</summary>
/// <param name="Manifest">The content manifest id; equals the installed copy's when that copy is current.</param>
public sealed record WorkshopItemDetails(ulong Id, string Title, string Manifest, DateTimeOffset Updated, long Size);
