using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;

namespace PzLauncher.Core.Mod.ZombieBuddy;

/// <summary>What every launch step needs, resolved once per launch, and what earlier steps preloaded.</summary>
internal sealed class ZombieBuddyLaunch
{
    private readonly WorkshopLibraries _workshop;
    private readonly HashSet<string> _preloaded = new(StringComparer.Ordinal);
    private IReadOnlyList<string>? _libraries;

    private ZombieBuddyLaunch(ZombieBuddyStatus status, string root, string? cache, WorkshopLibraries workshop)
    {
        Status = status;
        Root = root;
        Cache = cache;
        _workshop = workshop;
    }

    /// <summary>Always supported.</summary>
    public ZombieBuddyStatus Status { get; }

    public string Root { get; }

    /// <summary><c>null</c> when it doesn't resolve; the Steam libraries are still searched.</summary>
    public string? Cache { get; }

    /// <summary><c>null</c> when there is nothing to set up: the launch that follows reports a bad install or json itself.</summary>
    public static async Task<ZombieBuddyLaunch?> ResolveAsync(
        IZombieBuddyService zombieBuddy,
        WorkshopLibraries workshop,
        IPlatformEnvironment platform,
        string installRoot,
        string extraGameArgs,
        CancellationToken ct)
    {
        var status = await zombieBuddy.GetStatusAsync(installRoot, ct).ConfigureAwait(false);
        if (!status.IsSuccess
            || status.Value is not { IsSupported: true } supported
            || !PathNormalizer.TryNormalizeAbsolute(installRoot, out var root))
        {
            return null;
        }

        var cache = ModLocator.CacheFolder(extraGameArgs, PzLaunchFiles.ResolveGameFolder(root), platform.HomeDirectory);
        return new ZombieBuddyLaunch(supported, root, cache, workshop);
    }

    public async Task<IReadOnlyList<string>> LibrariesAsync(CancellationToken ct) =>
        _libraries ??= await _workshop.CandidatesAsync(Root, ct).ConfigureAwait(false);

    /// <summary><c>null</c> when no copy is found; a failure when the found copy can't be preloaded.</summary>
    public async Task<Result<ZombieBuddyPreloadEntry>?> FindPreloadableAsync(string modId, string itemId, ILogger logger, CancellationToken ct)
    {
        var libraries = await LibrariesAsync(ct).ConfigureAwait(false);
        if (await ModLocator.FindAsync(modId, itemId, Cache, libraries, logger, ct).ConfigureAwait(false) is not { } mod)
        {
            return null;
        }

        return await ZombieBuddyJavaMod.CheckPreloadableAsync(modId, mod, ct).ConfigureAwait(false);
    }

    /// <summary>Records that this mod is in <c>preload_mods</c> for this launch, for later steps to read.</summary>
    public void MarkPreloaded(string modId) => _preloaded.Add(modId);

    public bool IsPreloaded(string modId) => _preloaded.Contains(modId);
}
