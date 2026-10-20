namespace PzLauncher.Core.Steam.Workshop;

/// <summary>A Workshop item as the account's subscriptions and Steam's install list show it.</summary>
/// <param name="Subscribed"><c>default</c> for an item that isn't subscribed.</param>
/// <param name="Latest"><c>null</c> until a check or collection lookup fetched it, or when the Web API doesn't show the item.</param>
public sealed record WorkshopItem(
    ulong Id,
    DateTimeOffset Subscribed,
    bool DisabledLocally,
    WorkshopInstall? Installed,
    WorkshopItemDetails? Latest,
    WorkshopItemState State)
{
    /// <summary>An item the account isn't subscribed to, e.g. one a collection lists.</summary>
    public static WorkshopItem NotSubscribed(ulong id, WorkshopItemDetails? latest) =>
        new(id, default, false, null, latest, WorkshopItemState.NotSubscribed);

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
