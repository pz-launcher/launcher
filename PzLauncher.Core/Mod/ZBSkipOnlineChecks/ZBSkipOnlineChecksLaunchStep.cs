using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.Collections.Files;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Mod.Zabric;
using PzLauncher.Core.Mod.ZombieBuddy.LaunchSteps;
using PzLauncher.Core.Mod.ZombieBuddy.Preload;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Mod.ZBSkipOnlineChecks;

/// <summary>Keeps ZB Skip Online Checks' preload entry and agent start in step with the game's enabled mod list.</summary>
internal sealed partial class ZBSkipOnlineChecksLaunchStep(
    IZombieBuddyService zombieBuddy,
    IPlatformEnvironment platform,
    ILogger<ZBSkipOnlineChecksLaunchStep> logger) : IZombieBuddyLaunchStep
{
    public async Task<Result> RunAsync(ZombieBuddyLaunch launch, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (launch.Cache is not { } cache)
        {
            return Result.Ok();
        }

        // Only read here, so the newline for a missing file never matters.
        var active = await ActiveModsFile.ReadAsync(cache, "\n", ct).ConfigureAwait(false);
        if (!active.IsSuccess)
        {
            Logged(active.Error);
            return Result.Ok();
        }

        var configFolder = launch.Status.ConfigFolder;
        if (!active.Value.ModIds.Contains(ZBSkipOnlineChecksFiles.ModId, StringComparer.Ordinal))
        {
            return await RemoveAsync(launch.Root, configFolder, ct).ConfigureAwait(false);
        }

        return launch.Status.IsAllowAll
            ? await SetUpAsync(launch, configFolder, ct).ConfigureAwait(false)
            : Result.Ok();
    }

    // Both parts run; the first failure is returned.
    private async Task<Result> SetUpAsync(ZombieBuddyLaunch launch, string? configFolder, CancellationToken ct)
    {
        var found = await launch.FindPreloadableAsync(ZBSkipOnlineChecksFiles.ModId, ZBSkipOnlineChecksFiles.ItemId, logger, ct).ConfigureAwait(false);
        if (found is not { } entry)
        {
            return Result.Ok();
        }

        if (!entry.IsSuccess)
        {
            return Logged(entry.Error);
        }

        var preload = Result.Ok();

        // The mod's preLaunch runs from Zabric, so its preload entry is only written with Zabric preloaded.
        if (launch.IsPreloaded(ZabricFiles.ModId) && configFolder is not null)
        {
            // The Zabric step keeps Zabric first; this entry keeps its place or goes last.
            var ensured = await ZombieBuddyPreloadConfig.EnsureAsync(
                configFolder, ZBSkipOnlineChecksFiles.ModId, entry.Value, first: false, platform.PathComparison, ct).ConfigureAwait(false);
            if (!ensured.IsSuccess)
            {
                preload = Logged(ensured.Error);
            }
            else if (ensured.Value == ZombieBuddyPreload.Written)
            {
                LogPreloadWritten(logger, ConfigPath(configFolder), entry.Value.JarPath);
            }
        }

        var agentStart = await zombieBuddy.EnsurePatchesJarAsync(
            launch.Root, entry.Value.JarPath, ZBSkipOnlineChecksFiles.JarFile, ZBSkipOnlineChecksFiles.PackageName, ct).ConfigureAwait(false);
        return preload.IsSuccess ? agentStart : preload;
    }

    // Under any policy: a stale entry would keep the skip on.
    private async Task<Result> RemoveAsync(string root, string? configFolder, CancellationToken ct)
    {
        var preload = Result.Ok();
        if (configFolder is not null)
        {
            var removed = await ZombieBuddyPreloadConfig.RemoveAsync(configFolder, ZBSkipOnlineChecksFiles.ModId, ct).ConfigureAwait(false);
            if (!removed.IsSuccess)
            {
                preload = Logged(removed.Error);
            }
            else if (removed.Value == ZombieBuddyPreload.Removed)
            {
                LogPreloadRemoved(logger, ConfigPath(configFolder));
            }
        }

        var agentStart = await zombieBuddy.RemovePatchesJarAsync(
            root, ZBSkipOnlineChecksFiles.JarFile, ZBSkipOnlineChecksFiles.PackageName, ct).ConfigureAwait(false);
        return preload.IsSuccess ? agentStart : preload;
    }

    private static string ConfigPath(string configFolder) => Path.Combine(configFolder, ZombieBuddyFiles.ConfigFile);

    private Error Logged(Error error)
    {
        LogWarning(logger, error.Code.ToWireName(), error.Message);
        return error;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "ZBSkipOnlineChecks: {Code}: {Detail}")]
    private static partial void LogWarning(ILogger logger, string code, string detail);

    [LoggerMessage(Level = LogLevel.Information, Message = "ZBSkipOnlineChecks: preload written to {ConfigPath}: {JarPath}")]
    private static partial void LogPreloadWritten(ILogger logger, string configPath, string jarPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "ZBSkipOnlineChecks: preload removed from {ConfigPath}")]
    private static partial void LogPreloadRemoved(ILogger logger, string configPath);
}
