using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;
using PzLauncher.Core.ZombieBuddy;

namespace PzLauncher.Core.Zabric;

internal sealed partial class ZabricService(
    IZombieBuddyService zombieBuddy,
    WorkshopLibraries workshop,
    IPlatformEnvironment platform,
    ILogger<ZabricService> logger) : IZabricService
{
    // Concurrent launches would otherwise interleave their config.json rewrites.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<Result<ZabricPreload>> EnsurePreloadAsync(string installRoot, string extraGameArgs, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // The launch that follows reports a bad install or json itself.
        var status = await zombieBuddy.GetStatusAsync(installRoot, ct).ConfigureAwait(false);
        if (!status.IsSuccess
            || status.Value is not { IsSupported: true, IsAllowAll: true, ConfigFolder: { } configFolder }
            || !PathNormalizer.TryNormalizeAbsolute(installRoot, out var root))
        {
            return ZabricPreload.NotApplicable;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var libraries = await workshop.CandidatesAsync(root, ct).ConfigureAwait(false);
            var cache = ZabricLocator.CacheFolder(extraGameArgs, PzLaunchFiles.ResolveGameFolder(root), platform.HomeDirectory);
            if (await ZabricLocator.FindAsync(cache, libraries, logger, ct).ConfigureAwait(false) is not { } zabric)
            {
                return ZabricPreload.NotApplicable;
            }

            var entry = await zabric.CheckPreloadableAsync(ct).ConfigureAwait(false);
            if (!entry.IsSuccess)
            {
                return Logged(entry.Error);
            }

            var result = await ZabricPreloadConfig.EnsureAsync(configFolder, entry.Value, platform.PathComparison, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return Logged(result.Error);
            }

            if (result.Value == ZabricPreload.Written)
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
