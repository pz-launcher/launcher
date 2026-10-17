using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;

namespace PzLauncher.Core.Mod.ZBSkipOnlineChecks;

internal sealed partial class ZBSkipOnlineChecksService(
    IZombieBuddyService zombieBuddy,
    WorkshopLibraries workshop,
    IPlatformEnvironment platform,
    ILogger<ZBSkipOnlineChecksService> logger) : IZBSkipOnlineChecksService
{
    // Concurrent launches would otherwise interleave their config.json rewrites.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<Result> SyncAsync(string installRoot, string extraGameArgs, bool zabricPreloaded, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // The launch that follows reports a bad install or json itself.
        var status = await zombieBuddy.GetStatusAsync(installRoot, ct).ConfigureAwait(false);
        if (!status.IsSuccess
            || status.Value is not { IsSupported: true } zombieBuddyStatus
            || !PathNormalizer.TryNormalizeAbsolute(installRoot, out var root)
            || ModLocator.CacheFolder(extraGameArgs, PzLaunchFiles.ResolveGameFolder(root), platform.HomeDirectory) is not { } cache)
        {
            return Result.Ok();
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Only read here, so the newline for a missing file never matters.
            var active = await ActiveModsFile.ReadAsync(cache, "\n", ct).ConfigureAwait(false);
            if (!active.IsSuccess)
            {
                Logged(active.Error);
                return Result.Ok();
            }

            var configFolder = zombieBuddyStatus.ConfigFolder;
            if (!active.Value.ModIds.Contains(ZBSkipOnlineChecksFiles.ModId, StringComparer.Ordinal))
            {
                return await RemoveAsync(root, configFolder, ct).ConfigureAwait(false);
            }

            return zombieBuddyStatus.IsAllowAll
                ? await SetUpAsync(root, cache, configFolder, zabricPreloaded, ct).ConfigureAwait(false)
                : Result.Ok();
        }
        finally
        {
            _gate.Release();
        }
    }

    // Both parts run; the first failure is returned.
    private async Task<Result> SetUpAsync(string root, string cache, string? configFolder, bool zabricPreloaded, CancellationToken ct)
    {
        var libraries = await workshop.CandidatesAsync(root, ct).ConfigureAwait(false);
        var mod = await ModLocator.FindAsync(ZBSkipOnlineChecksFiles.ModId, ZBSkipOnlineChecksFiles.ItemId, cache, libraries, logger, ct).ConfigureAwait(false);
        if (mod is null)
        {
            return Result.Ok();
        }

        var entry = await ZombieBuddyJavaMod.CheckPreloadableAsync(ZBSkipOnlineChecksFiles.ModId, mod, ct).ConfigureAwait(false);
        if (!entry.IsSuccess)
        {
            return Logged(entry.Error);
        }

        var preload = Result.Ok();
        if (zabricPreloaded && configFolder is not null)
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
            root, entry.Value.JarPath, ZBSkipOnlineChecksFiles.JarFile, ZBSkipOnlineChecksFiles.PackageName, ct).ConfigureAwait(false);
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
