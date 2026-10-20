using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Steam.Workshop;

namespace PzLauncher.Core.Mod.Workshop;

internal sealed class WorkshopModsService(
    IWorkshopService workshop,
    IModCollectionService collections,
    ILogger<WorkshopModsService> logger) : IWorkshopModsService
{
    public async Task<Result<WorkshopModSubscriptions>> GetSubscriptionsAsync(string installRoot, string extraGameArgs, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var subscriptions = await workshop.GetSubscriptionsAsync(installRoot, ct).ConfigureAwait(false);
        if (!subscriptions.IsSuccess)
        {
            return subscriptions.Error;
        }

        var warnings = new List<Error>(subscriptions.Value.Warnings);
        var state = await collections.GetAsync(installRoot, extraGameArgs, ct).ConfigureAwait(false);
        if (!state.IsSuccess)
        {
            warnings.Add(state.Error);
        }

        var items = await JoinAsync(subscriptions.Value.Items, state.IsSuccess ? state.Value : null, ct).ConfigureAwait(false);
        return new WorkshopModSubscriptions(items, warnings);
    }

    public async Task<Result<IReadOnlyList<WorkshopItem>>> FindOutdatedForLaunchAsync(
        string installRoot, string extraGameArgs, string? collection, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var state = await collections.GetAsync(installRoot, extraGameArgs, ct).ConfigureAwait(false);
        if (!state.IsSuccess)
        {
            return state.Error;
        }

        var enabled = EnabledIds(state.Value, collection);
        if (enabled.Count == 0)
        {
            return Array.Empty<WorkshopItem>();
        }

        var subscriptions = await workshop.GetSubscriptionsAsync(installRoot, ct).ConfigureAwait(false);
        if (!subscriptions.IsSuccess)
        {
            return subscriptions.Error;
        }

        // Only the copy the game loads counts, so an item whose mods are all shadowed by another copy isn't used.
        var usedIds = state.Value.InstalledMods
            .Where(mod => mod.Source == InstalledModSource.SteamWorkshop && mod.WorkshopId is not null && enabled.Contains(mod.Id))
            .Select(mod => mod.WorkshopId!.Value)
            .ToHashSet();
        var used = subscriptions.Value.Items
            .Where(item => item.State is WorkshopItemState.UpToDate or WorkshopItemState.NeedsUpdate && usedIds.Contains(item.Id))
            .ToList();
        if (used.Count == 0)
        {
            return Array.Empty<WorkshopItem>();
        }

        var latest = await workshop.CheckUpdatesAsync([.. used.Select(item => item.Id)], ct).ConfigureAwait(false);
        if (!latest.IsSuccess)
        {
            return latest.Error;
        }

        var details = latest.Value.ToDictionary(item => item.Id);
        return used
            .Select(item => details.GetValueOrDefault(item.Id) is { } current ? item.WithLatest(current) : item)
            .Where(item => item.State == WorkshopItemState.NeedsUpdate)
            .ToList();
    }

    // A current collection gone from the file isn't applied (the apply reports it), so the enabled list stays as it is.
    private static HashSet<string> EnabledIds(ModCollectionState state, string? collection)
    {
        var target = collection is null ? null : state.Collections.FirstOrDefault(candidate => candidate.Name == collection);
        var ids = target is null ? state.ActiveIds : ModLoadOrder.Resolve(target.ModIds, state.InstalledMods).EnabledIds;
        return ids.ToHashSet(StringComparer.Ordinal);
    }

    private static List<string> UsedBy(IReadOnlyList<InstalledMod> mods, IReadOnlyList<ModCollection> collections)
    {
        if (mods.Count == 0)
        {
            return [];
        }

        var ids = mods.Select(mod => mod.Id).ToHashSet(StringComparer.Ordinal);
        return [.. collections.Where(collection => collection.ModIds.Any(ids.Contains)).Select(collection => collection.Name)];
    }

    // An item lists every mod in its own folder, including ones the game loads from another copy.
    private async Task<List<WorkshopModItem>> JoinAsync(IReadOnlyList<WorkshopItem> items, ModCollectionState? state, CancellationToken ct)
    {
        IReadOnlyList<ModCollection> modCollections = state?.Collections ?? [];
        var joined = new List<WorkshopModItem>(items.Count);
        foreach (var item in items)
        {
            var mods = item.Installed is { } installed
                ? await ModCatalog.ReadRootAsync(ModLocator.ModRoot.ForWorkshopItem(installed.Folder), logger, ct).ConfigureAwait(false)
                : [];
            joined.Add(new WorkshopModItem(item, mods, UsedBy(mods, modCollections)));
        }

        return joined;
    }
}
