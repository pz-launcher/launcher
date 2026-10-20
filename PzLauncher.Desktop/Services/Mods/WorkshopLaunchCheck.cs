using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.Workshop;
using PzLauncher.Core.Steam.Workshop;

namespace PzLauncher.Desktop.Services.Mods;

/// <summary>Before a launch: the subscribed Workshop items behind the mods the launch will enable whose installed copy isn't the latest version.</summary>
public sealed partial class WorkshopLaunchCheck(IWorkshopModsService workshop, TimeProvider time, ILogger<WorkshopLaunchCheck> logger)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <param name="collection">The current collection, applied after the check; <c>null</c> launches with <c>default.txt</c>'s enabled list.</param>
    /// <returns>With <see cref="WorkshopItem.Latest"/> filled in. Empty also when the check failed or took too long: it never holds up a launch.</returns>
    public async Task<IReadOnlyList<WorkshopItem>> FindOutdatedAsync(string installRoot, string extraGameArgs, string? collection)
    {
        using var timeout = new CancellationTokenSource(Timeout, time);
        try
        {
            var outdated = await workshop.FindOutdatedForLaunchAsync(installRoot, extraGameArgs, collection, timeout.Token);
            if (outdated.IsSuccess)
            {
                return outdated.Value;
            }

            LogSkipped(logger, outdated.Error.Code.ToWireName());
            return [];
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            LogTimedOut(logger, Timeout.TotalSeconds);
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Workshop pre-launch check skipped ({Code}); launching without it")]
    private static partial void LogSkipped(ILogger logger, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workshop pre-launch check took longer than {Seconds} s; launching without it")]
    private static partial void LogTimedOut(ILogger logger, double seconds);
}
