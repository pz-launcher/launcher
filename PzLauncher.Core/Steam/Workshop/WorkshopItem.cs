using PzLauncher.Core.Mod;

namespace PzLauncher.Core.Steam.Workshop;

/// <summary>A subscribed Workshop item.</summary>
/// <param name="Latest"><c>null</c> until a check or collection lookup fetched it, or when the Web API doesn't show the item.</param>
/// <param name="Mods">The mods in the installed copy, in folder order.</param>
/// <param name="Collections">Names of the install's mod collections that list one of <paramref name="Mods"/>.</param>
public sealed record WorkshopItem(
    ulong Id,
    DateTimeOffset Subscribed,
    bool DisabledLocally,
    WorkshopInstall? Installed,
    WorkshopItemDetails? Latest,
    IReadOnlyList<InstalledMod> Mods,
    IReadOnlyList<string> Collections,
    WorkshopItemState State);
