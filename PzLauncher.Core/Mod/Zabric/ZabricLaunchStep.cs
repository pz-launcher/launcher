using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Mod.Zabric;

/// <summary>Registers the installed Zabric in ZombieBuddy's preload list when the json's agent arg is allow-all.</summary>
internal sealed partial class ZabricLaunchStep(IPlatformEnvironment platform, ILogger<ZabricLaunchStep> logger) : IZombieBuddyLaunchStep
{
    public async Task<Result> RunAsync(ZombieBuddyLaunch launch, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (launch.Status is not { IsAllowAll: true, ConfigFolder: { } configFolder }
            || await launch.FindPreloadableAsync(ZabricFiles.ModId, ZabricFiles.ItemId, logger, ct).ConfigureAwait(false) is not { } entry)
        {
            return Result.Ok();
        }

        if (!entry.IsSuccess)
        {
            return Logged(entry.Error);
        }

        // Zabric goes first so its transformer is in place when ZombieBuddy preloads the others.
        var result = await ZombieBuddyPreloadConfig.EnsureAsync(
            configFolder, ZabricFiles.ModId, entry.Value, first: true, platform.PathComparison, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Logged(result.Error);
        }

        if (result.Value == ZombieBuddyPreload.Written)
        {
            LogWritten(logger, Path.Combine(configFolder, ZombieBuddyFiles.ConfigFile), entry.Value.JarPath);
        }

        if (result.Value is ZombieBuddyPreload.Current or ZombieBuddyPreload.Written)
        {
            launch.MarkPreloaded(ZabricFiles.ModId);
        }

        return Result.Ok();
    }

    private Error Logged(Error error)
    {
        LogWarning(logger, error.Code.ToWireName(), error.Message);
        return error;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Zabric: {Code}: {Detail}")]
    private static partial void LogWarning(ILogger logger, string code, string detail);

    [LoggerMessage(Level = LogLevel.Information, Message = "Zabric: preload written to {ConfigPath}: {JarPath}")]
    private static partial void LogWritten(ILogger logger, string configPath, string jarPath);
}
