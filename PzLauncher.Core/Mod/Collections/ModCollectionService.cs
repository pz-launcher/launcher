using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod.Collections.Files;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;

namespace PzLauncher.Core.Mod.Collections;

internal sealed partial class ModCollectionService(
    WorkshopLibraries workshop,
    IPlatformEnvironment platform,
    ILogger<ModCollectionService> logger) : IModCollectionService
{
    // Two writes would otherwise both re-read the same file and the second would drop the first's edit.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<Result<ModCollectionState>> GetAsync(string installRoot, string extraGameArgs, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var location = ResolveCache(installRoot, extraGameArgs);
        if (!location.IsSuccess)
        {
            return location.Error;
        }

        var (root, cache) = location.Value;
        var installed = await InstalledModsAsync(root, cache, ct).ConfigureAwait(false);
        var settings = await ModListSettingsFile.ReadAsync(cache, ct).ConfigureAwait(false);
        if (!settings.IsSuccess)
        {
            return Logged(settings.Error);
        }

        var active = await ActiveModsFile.ReadAsync(cache, NewFileNewline, ct).ConfigureAwait(false);
        if (!active.IsSuccess)
        {
            return Logged(active.Error);
        }

        var orders = await ModCollectionOrderFile.ReadAsync(cache, logger, ct).ConfigureAwait(false);
        var installedIds = new HashSet<string>(installed.Select(mod => mod.Id), StringComparer.Ordinal);
        var activeInstalled = new HashSet<string>(active.Value.ModIds.Where(installedIds.Contains), StringComparer.Ordinal);
        var collections = settings.Value.Collections
            .Select(collection => ModCollection.Create(collection.Name, Ordered(collection, orders), installed, active.Value.ModIds))
            .ToList();

        return new ModCollectionState(
            cache,
            collections,
            installed,
            active.Value.ModIds,
            activeInstalled.Count == 0 || collections.Any(collection => collection.IsActive));
    }

    public async Task<Result> SaveAsync(SaveModCollectionRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!ModCollectionName.IsValid(request.Name))
        {
            return ModCollectionErrors.NameInvalid(request.Name);
        }

        if (request.ModIds.FirstOrDefault(id => !ModCollectionName.IsValidModId(id)) is { } invalid)
        {
            return ModCollectionErrors.ModIdInvalid(invalid);
        }

        var location = ResolveCache(request.InstallRoot, request.ExtraGameArgs);
        if (!location.IsSuccess)
        {
            return location.Error;
        }

        var cache = location.Value.Cache;
        IReadOnlyList<string> ids = [.. request.ModIds.Distinct(StringComparer.Ordinal)];
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var settings = await ModListSettingsFile.ReadAsync(cache, ct).ConfigureAwait(false);
            if (!settings.IsSuccess)
            {
                return Logged(settings.Error);
            }

            if (!settings.Value.Save(request.OriginalName, request.Name, ids))
            {
                return ModCollectionErrors.NotFound(request.OriginalName ?? request.Name);
            }

            var written = await settings.Value.WriteAsync(ct).ConfigureAwait(false);
            if (!written.IsSuccess)
            {
                return Logged(written.Error);
            }

            var orders = await ModCollectionOrderFile.ReadAsync(cache, logger, ct).ConfigureAwait(false);
            if (request.OriginalName is { } original)
            {
                orders.Remove(original);
            }

            orders[request.Name] = ids;
            var result = await WriteOrdersAsync(cache, settings.Value, orders, ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                LogSaved(logger, request.Name, ModCollectionFiles.SettingsFile(cache));
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result> DeleteAsync(string installRoot, string extraGameArgs, string name, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var location = ResolveCache(installRoot, extraGameArgs);
        if (!location.IsSuccess)
        {
            return location.Error;
        }

        var cache = location.Value.Cache;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var settings = await ModListSettingsFile.ReadAsync(cache, ct).ConfigureAwait(false);
            if (!settings.IsSuccess)
            {
                return Logged(settings.Error);
            }

            if (!settings.Value.Delete(name))
            {
                return ModCollectionErrors.NotFound(name);
            }

            var written = await settings.Value.WriteAsync(ct).ConfigureAwait(false);
            if (!written.IsSuccess)
            {
                return Logged(written.Error);
            }

            var orders = await ModCollectionOrderFile.ReadAsync(cache, logger, ct).ConfigureAwait(false);
            var result = await WriteOrdersAsync(cache, settings.Value, orders, ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                LogDeleted(logger, name, ModCollectionFiles.SettingsFile(cache));
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<ApplyModCollectionResult>> ApplyAsync(string installRoot, string extraGameArgs, string name, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var location = ResolveCache(installRoot, extraGameArgs);
        if (!location.IsSuccess)
        {
            return location.Error;
        }

        var (root, cache) = location.Value;
        var installed = await InstalledModsAsync(root, cache, ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var settings = await ModListSettingsFile.ReadAsync(cache, ct).ConfigureAwait(false);
            if (!settings.IsSuccess)
            {
                return Logged(settings.Error);
            }

            if (settings.Value.Collections.FirstOrDefault(collection => collection.Name == name) is not { } collection)
            {
                return ModCollectionErrors.NotFound(name);
            }

            var orders = await ModCollectionOrderFile.ReadAsync(cache, logger, ct).ConfigureAwait(false);
            var resolved = ModLoadOrder.Resolve(Ordered(collection, orders), installed);
            var active = await ActiveModsFile.ReadAsync(cache, NewFileNewline, ct).ConfigureAwait(false);
            if (!active.IsSuccess)
            {
                return Logged(active.Error);
            }

            active.Value.SetModIds(resolved.EnabledIds);
            var written = await active.Value.WriteAsync(ct).ConfigureAwait(false);
            if (!written.IsSuccess)
            {
                return Logged(written.Error);
            }

            LogApplied(logger, name, ModCollectionFiles.ActiveModsFile(cache), resolved.EnabledIds.Count, resolved.MissingIds.Count, resolved.SkippedIncompatible.Count);
            return resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    // The game writes default.txt with the OS's line separator.
    private string NewFileNewline => platform.Os switch
    {
        PlatformKind.Windows => "\r\n",
        PlatformKind.Linux or PlatformKind.MacOS => "\n",
    };

    private static IReadOnlyList<string> Ordered(SharedModCollection collection, Dictionary<string, IReadOnlyList<string>> orders) =>
        ModCollectionOrderFile.Reconcile(collection.ModIds, orders.GetValueOrDefault(collection.Name));

    // Orders of collections no longer in the game's file go, so a new collection of that name starts from its own order.
    private static Task<Result> WriteOrdersAsync(
        string cache, ModListSettingsFile settings, Dictionary<string, IReadOnlyList<string>> orders, CancellationToken ct)
    {
        var names = settings.Collections.Select(collection => collection.Name).ToHashSet(StringComparer.Ordinal);
        var kept = orders.Where(entry => names.Contains(entry.Key)).ToDictionary(StringComparer.Ordinal);
        return ModCollectionOrderFile.WriteAsync(cache, kept, ct);
    }

    private Result<(string Root, string Cache)> ResolveCache(string installRoot, string extraGameArgs)
    {
        if (!PathNormalizer.TryNormalizeAbsolute(installRoot, out var root))
        {
            return InstallErrors.InvalidPath(installRoot);
        }

        return ModLocator.CacheFolder(extraGameArgs, PzLaunchFiles.ResolveGameFolder(root), platform.HomeDirectory) is { } cache
            ? (root, cache)
            : ModCollectionErrors.CacheFolderUnresolved(root);
    }

    private async Task<IReadOnlyList<InstalledMod>> InstalledModsAsync(string root, string cache, CancellationToken ct)
    {
        var libraries = await workshop.CandidatesAsync(root, ct).ConfigureAwait(false);
        return await ModCatalog.ReadAsync(cache, libraries, logger, ct).ConfigureAwait(false);
    }

    private Error Logged(Error error)
    {
        LogWarning(logger, error.Code.ToWireName(), error.Message);
        return error;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mod collections: {Code}: {Detail}")]
    private static partial void LogWarning(ILogger logger, string code, string detail);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mod collection '{Name}' saved to {Path}")]
    private static partial void LogSaved(ILogger logger, string name, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mod collection '{Name}' deleted from {Path}")]
    private static partial void LogDeleted(ILogger logger, string name, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mod collection '{Name}' applied to {Path}: {Enabled} enabled, {Missing} missing, {Skipped} skipped")]
    private static partial void LogApplied(ILogger logger, string name, string path, int enabled, int missing, int skipped);
}
