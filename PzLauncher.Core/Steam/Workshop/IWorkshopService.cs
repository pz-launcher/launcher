using PzLauncher.Core.Common;

namespace PzLauncher.Core.Steam.Workshop;

/// <summary>
/// The Workshop items of the account Steam signs in with, read from Steam's local files and the keyless Steam Web API.
/// Writes (subscribe, update) go through the Steam helper, not this service.
/// </summary>
public interface IWorkshopService
{
    /// <summary>
    /// Subscriptions from Steam's local files in the install's Steam libraries, without going online. <see cref="WorkshopItem.Latest"/>
    /// and the update state use what <see cref="CheckUpdatesAsync"/> or <see cref="GetCollectionAsync"/> fetched this session.
    /// </summary>
    Task<Result<WorkshopSubscriptions>> GetSubscriptionsAsync(string installRoot, string extraGameArgs, CancellationToken ct = default);

    /// <summary>The latest version of each id the Web API shows, in request order; an answer a few minutes old is reused.</summary>
    Task<Result<IReadOnlyList<WorkshopItemDetails>>> CheckUpdatesAsync(IReadOnlyList<ulong> ids, CancellationToken ct = default);

    Task<Result<WorkshopCollection>> GetCollectionAsync(ulong id, CancellationToken ct = default);
}
