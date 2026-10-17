using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;

namespace PzLauncher.Core.Mod.Zabric;

internal sealed partial class ZabricService(
    IZombieBuddyService zombieBuddy,
    WorkshopLibraries workshop,
    IPlatformEnvironment platform,
    ILogger<ZabricService> logger) : IZabricService
{
    // Concurrent launches would otherwise interleave their config.json rewrites.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<Result<ZombieBuddyPreload>> EnsurePreloadAsync(string installRoot, string extraGameArgs, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // The launch that follows reports a bad install or json itself.
        var status = await zombieBuddy.GetStatusAsync(installRoot, ct).ConfigureAwait(false);
        if (!status.IsSuccess
            || status.Value is not { IsSupported: true, IsAllowAll: true, ConfigFolder: { } configFolder }
            || !PathNormalizer.TryNormalizeAbsolute(installRoot, out var root))
        {
            return ZombieBuddyPreload.NotApplicable;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var libraries = await workshop.CandidatesAsync(root, ct).ConfigureAwait(false);
            var cache = ModLocator.CacheFolder(extraGameArgs, PzLaunchFiles.ResolveGameFolder(root), platform.HomeDirectory);
            if (await ModLocator.FindAsync(ZabricFiles.ModId, ZabricFiles.ItemId, cache, libraries, logger, ct).ConfigureAwait(false) is not { } zabric)
            {
                return ZombieBuddyPreload.NotApplicable;
            }

            var entry = await ZombieBuddyJavaMod.CheckPreloadableAsync(ZabricFiles.ModId, zabric, ct).ConfigureAwait(false);
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

            return result;
        }
        finally
        {
            _gate.Release();
        }
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
