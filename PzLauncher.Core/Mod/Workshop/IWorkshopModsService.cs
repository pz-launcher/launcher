using PzLauncher.Core.Common;
using PzLauncher.Core.Steam.Workshop;

namespace PzLauncher.Core.Mod.Workshop;

/// <summary>The account's Workshop items joined with an install's mods and mod collections.</summary>
public interface IWorkshopModsService
{
    /// <summary>
    /// <see cref="IWorkshopService.GetSubscriptionsAsync"/>'s items, each with its mods and the collections using them; mod collections that
    /// can't be read are a warning.
    /// </summary>
    Task<Result<WorkshopModSubscriptions>> GetSubscriptionsAsync(string installRoot, string extraGameArgs, CancellationToken ct = default);

    /// <summary>
    /// Before a launch: the installed items behind the mods the launch will enable whose copy isn't the latest the Web API shows; an item it
    /// doesn't show keeps what Steam knows. Goes online only when such an item exists.
    /// </summary>
    /// <param name="collection">The collection the launch applies; <c>null</c>, or one no longer in the game's file, keeps <c>default.txt</c>'s enabled list.</param>
    /// <returns>With <see cref="WorkshopItem.Latest"/> filled in, in the order of Steam's subscription file.</returns>
    Task<Result<IReadOnlyList<WorkshopItem>>> FindOutdatedForLaunchAsync(
        string installRoot, string extraGameArgs, string? collection, CancellationToken ct = default);
}
