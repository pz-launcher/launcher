using System.Globalization;
using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Steam;

namespace PzLauncher.Core.Mod;

// The game's mod lookup order: staged Workshop items, Steam Workshop items, then the cache's mods folder; first found wins.
internal static partial class ModLocator
{
    /// <summary>The last <c>-cachedir=</c> game arg (relative to the game folder), else <c>&lt;home&gt;/Zomboid</c>; <c>null</c> when that doesn't resolve.</summary>
    public static string? CacheFolder(string extraGameArgs, string gameFolder, string homeDirectory)
    {
        var arg = LaunchArguments.Split(extraGameArgs).LastOrDefault(arg => arg.StartsWith(ModFolders.CacheDirArg, StringComparison.Ordinal));
        if (arg is not null)
        {
            return PathNormalizer.TryNormalizeAgainst(arg[ModFolders.CacheDirArg.Length..], gameFolder, out var cache) ? cache : null;
        }

        return PathNormalizer.TryNormalizeAbsolute(homeDirectory, out var home) ? Path.Combine(home, ModFolders.DefaultCacheFolder) : null;
    }

    /// <summary>
    /// The first copy whose <c>mod.info</c> has this id; <c>null</c> when none. Within a Steam library,
    /// <paramref name="preferredItemId"/> goes before the other items.
    /// </summary>
    public static async Task<ModInfo?> FindAsync(
        string modId, string? preferredItemId, string? cache, IReadOnlyList<string> libraries, ILogger logger, CancellationToken ct)
    {
        foreach (var modFolder in Candidates(modId, preferredItemId, cache, libraries, logger))
        {
            ct.ThrowIfCancellationRequested();
            if (await ReadAsync(modFolder, logger, ct).ConfigureAwait(false) is { } info
                && string.Equals(info.Value(ModFolders.IdKey), modId, StringComparison.Ordinal))
            {
                return info;
            }
        }

        return null;
    }

    internal static int[]? ParseVersion(string name)
    {
        var parts = name.Split('.');
        var version = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out version[i]))
            {
                return null;
            }
        }

        return version;
    }

    private static IEnumerable<string> Candidates(
        string modId, string? preferredItemId, string? cache, IReadOnlyList<string> libraries, ILogger logger)
    {
        if (cache is not null)
        {
            foreach (var item in Subfolders(ModFolders.StagedWorkshopFolder(cache), logger))
            {
                yield return ModFolders.StagedModFolder(item, modId);
            }
        }

        foreach (var library in libraries)
        {
            var items = Subfolders(WorkshopLibraries.ContentFolder(library), logger);
            foreach (var item in items.OrderBy(item => !string.Equals(Path.GetFileName(item), preferredItemId, StringComparison.Ordinal)))
            {
                yield return ModFolders.WorkshopModFolder(item, modId);
            }
        }

        if (cache is not null)
        {
            yield return ModFolders.LocalModFolder(cache, modId);
        }
    }

    // The highest version folder that holds a mod.info is the one read, whatever its id.
    private static async Task<ModInfo?> ReadAsync(string modFolder, ILogger logger, CancellationToken ct)
    {
        var versions = new List<(string Folder, int[] Version)>();
        foreach (var folder in Subfolders(modFolder, logger))
        {
            if (ParseVersion(Path.GetFileName(folder)) is { } version)
            {
                versions.Add((folder, version));
            }
        }

        versions.Sort((x, y) => CompareVersions(y.Version, x.Version));
        foreach (var (folder, _) in versions)
        {
            try
            {
                if (await ModInfo.ReadAsync(folder, ct).ConfigureAwait(false) is { } info)
                {
                    return info;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogSkipped(logger, folder, ex.Message);
                return null;
            }
        }

        return null;
    }

    // Ordinal order; empty when the folder is missing or unreadable.
    private static List<string> Subfolders(string folder, ILogger logger)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        try
        {
            var subfolders = Directory.EnumerateDirectories(folder).ToList();
            subfolders.Sort(StringComparer.Ordinal);
            return subfolders;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSkipped(logger, folder, ex.Message);
            return [];
        }
    }

    private static int CompareVersions(int[] x, int[] y)
    {
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            if (x[i] != y[i])
            {
                return x[i].CompareTo(y[i]);
            }
        }

        return x.Length.CompareTo(y.Length);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Mod lookup skipped {Path}: {Detail}")]
    private static partial void LogSkipped(ILogger logger, string path, string detail);
}
