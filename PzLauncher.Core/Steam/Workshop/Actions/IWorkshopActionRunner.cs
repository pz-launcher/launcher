using PzLauncher.Core.Common;

namespace PzLauncher.Core.Steam.Workshop.Actions;

/// <summary>
/// Runs a Workshop action in a short-lived helper process that loads the install's own <c>steam_api</c>;
/// Steam shows the user playing the game while it runs. Runs one at a time: a second call waits for the first.
/// </summary>
public interface IWorkshopActionRunner
{
    /// <summary>Succeeds when the helper ran; each item's own failure is in the outcome.</summary>
    Task<Result<WorkshopActionOutcome>> RunAsync(
        string installRoot, WorkshopActionRequest request, IProgress<WorkshopActionEvent>? progress = null, CancellationToken ct = default);

    /// <summary>Read-only: queries Steam for the account's favorited and published collections of the game, every page.</summary>
    Task<Result<WorkshopUserCollections>> ListCollectionsAsync(string installRoot, CancellationToken ct = default);
}
