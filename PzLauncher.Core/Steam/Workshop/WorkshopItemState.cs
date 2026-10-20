namespace PzLauncher.Core.Steam.Workshop;

public enum WorkshopItemState
{
    UpToDate,

    /// <summary>The installed copy differs from the latest the Web API reported, or from the latest Steam knows of before a check.</summary>
    NeedsUpdate,

    /// <summary>Subscribed, but Steam has no installed copy in the install's Steam libraries.</summary>
    NotInstalled,

    DisabledLocally,

    NotSubscribed,
}
