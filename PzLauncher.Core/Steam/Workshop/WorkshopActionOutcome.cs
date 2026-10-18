namespace PzLauncher.Core.Steam.Workshop;

public enum WorkshopItemFailure
{
    SubscribeFailed,
    UnsubscribeFailed,
    DownloadNotStarted,
    DownloadFailed,
    TimedOut,

    /// <summary>The helper finished without reporting this item.</summary>
    NotReported,
}

/// <param name="Failure"><c>null</c> when the action succeeded for this item.</param>
/// <param name="SteamResult">Steam's EResult for a failure; 0 when Steam gave none.</param>
public sealed record WorkshopItemOutcome(ulong Id, WorkshopItemFailure? Failure, int SteamResult);

/// <param name="Items">One per requested id, in request order.</param>
public sealed record WorkshopActionOutcome(IReadOnlyList<WorkshopItemOutcome> Items);
