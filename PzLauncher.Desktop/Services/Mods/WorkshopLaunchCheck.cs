using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Steam.Workshop;

namespace PzLauncher.Desktop.Services.Mods;

/// <summary>Before a launch: the subscribed Workshop items behind the mods the launch will enable whose installed copy isn't the latest version.</summary>
public sealed partial class WorkshopLaunchCheck(
    IWorkshopService workshop,
    IModCollectionService collections,
    TimeProvider time,
    ILogger<WorkshopLaunchCheck> logger)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <param name="collection">The current collection, applied after the check; <c>null</c> launches with <c>default.txt</c>'s enabled list.</param>
    /// <returns>With <see cref="WorkshopItem.Latest"/> filled in. Empty also when the check failed or took too long: it never holds up a launch.</returns>
    public async Task<IReadOnlyList<WorkshopItem>> FindOutdatedAsync(string installRoot, string extraGameArgs, string? collection)
    {
        using var timeout = new CancellationTokenSource(Timeout, time);
        try
        {
            return await FindAsync(installRoot, extraGameArgs, collection, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            LogTimedOut(logger, Timeout.TotalSeconds);
            return [];
        }
    }

    private async Task<IReadOnlyList<WorkshopItem>> FindAsync(string installRoot, string extraGameArgs, string? collection, CancellationToken ct)
    {
        var state = await collections.GetAsync(installRoot, extraGameArgs, ct);
        if (!state.IsSuccess)
        {
            return Skipped(state.Error);
        }

        var enabled = EnabledIds(state.Value, collection);
        if (enabled.Count == 0)
        {
            return [];
        }

        var subscriptions = await workshop.GetSubscriptionsAsync(installRoot, extraGameArgs, ct);
        if (!subscriptions.IsSuccess)
        {
            return Skipped(subscriptions.Error);
        }

        List<WorkshopItem> used =
        [
            .. subscriptions.Value.Items.Where(item =>
                (item.State is WorkshopItemState.UpToDate or WorkshopItemState.NeedsUpdate) && item.Mods.Any(mod => enabled.Contains(mod.Id))),
        ];
        if (used.Count == 0)
        {
            return [];
        }

        var latest = await workshop.CheckUpdatesAsync([.. used.Select(item => item.Id)], ct);
        if (!latest.IsSuccess)
        {
            return Skipped(latest.Error);
        }

        var details = latest.Value.ToDictionary(item => item.Id);
        var outdated = new List<WorkshopItem>();
        foreach (var item in used)
        {
            var current = details.GetValueOrDefault(item.Id);

            // An item the Web API doesn't show keeps what Steam itself knows.
            var isOutdated = current is not null && item.Installed is { } installed
                ? !string.Equals(current.Manifest, installed.Manifest, StringComparison.Ordinal)
                : item.State == WorkshopItemState.NeedsUpdate;
            if (isOutdated)
            {
                outdated.Add(item with { Latest = current ?? item.Latest, State = WorkshopItemState.NeedsUpdate });
            }
        }

        return outdated;
    }

    // A current collection gone from the file isn't applied (the apply reports it), so the enabled list stays as it is.
    private static HashSet<string> EnabledIds(ModCollectionState state, string? collection)
    {
        var target = collection is null ? null : state.Collections.FirstOrDefault(candidate => candidate.Name == collection);
        var ids = target is null ? state.ActiveIds : ModLoadOrder.Resolve(target.ModIds, state.InstalledMods).EnabledIds;
        return ids.ToHashSet(StringComparer.Ordinal);
    }

    private List<WorkshopItem> Skipped(Error error)
    {
        LogSkipped(logger, error.Code.ToWireName());
        return [];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Workshop pre-launch check skipped ({Code}); launching without it")]
    private static partial void LogSkipped(ILogger logger, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workshop pre-launch check took longer than {Seconds} s; launching without it")]
    private static partial void LogTimedOut(ILogger logger, double seconds);
}
