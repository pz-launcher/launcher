namespace PzLauncher.Core.Steam.Workshop;

public enum WorkshopAction
{
    Subscribe,
    Unsubscribe,

    /// <summary>Makes Steam fetch each item's latest version and download it, even when Steam hadn't noticed it changed.</summary>
    Update,
}

public sealed record WorkshopActionRequest(WorkshopAction Action, IReadOnlyList<ulong> Ids);
