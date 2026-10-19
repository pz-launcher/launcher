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
    WorkshopItemState State)
{
    /// <summary>This item with <paramref name="latest"/> as its latest version and the state that follows from it.</summary>
    public WorkshopItem WithLatest(WorkshopItemDetails latest) => this with
    {
        Latest = latest,
        State = State == WorkshopItemState.NotSubscribed ? State : StateOf(DisabledLocally, Installed, latest.Manifest),
    };

    internal static WorkshopItemState StateOf(bool disabledLocally, WorkshopInstall? installed, string? latestManifest) =>
        disabledLocally ? WorkshopItemState.DisabledLocally
        : installed is null ? WorkshopItemState.NotInstalled
        : latestManifest is not null && !string.Equals(latestManifest, installed.Manifest, StringComparison.Ordinal) ? WorkshopItemState.NeedsUpdate
        : WorkshopItemState.UpToDate;
}
