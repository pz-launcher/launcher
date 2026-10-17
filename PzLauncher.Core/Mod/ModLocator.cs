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
        foreach (var root in Roots(preferredItemId, cache, libraries, logger))
        {
            ct.ThrowIfCancellationRequested();
            if (await ReadAsync(Path.Combine(root.ModsFolder, modId), logger, ct).ConfigureAwait(false) is { } info
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

    /// <summary>The <c>mods</c> folders the game searches, in its lookup order.</summary>
    public static IEnumerable<ModRoot> Roots(string? preferredItemId, string? cache, IReadOnlyList<string> libraries, ILogger logger)
    {
        if (cache is not null)
        {
            foreach (var item in Subfolders(ModFolders.StagedWorkshopFolder(cache), logger))
            {
                yield return new ModRoot(ModFolders.StagedModsFolder(item), InstalledModSource.StagedWorkshop, null);
            }
        }

        foreach (var library in libraries)
        {
            var items = Subfolders(WorkshopLibraries.ContentFolder(library), logger);
            foreach (var item in items.OrderBy(item => !string.Equals(Path.GetFileName(item), preferredItemId, StringComparison.Ordinal)))
            {
                yield return new ModRoot(ModFolders.WorkshopModsFolder(item), InstalledModSource.SteamWorkshop, Path.GetFileName(item));
            }
        }

        if (cache is not null)
        {
            yield return new ModRoot(ModFolders.LocalModsFolder(cache), InstalledModSource.Local, null);
        }
    }

    // The highest version folder that holds a mod.info is the one read, whatever its id.
    public static async Task<ModInfo?> ReadAsync(string modFolder, ILogger logger, CancellationToken ct)
    {
        foreach (var folder in VersionFolders(modFolder, logger))
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

    // As the game loads a B42 mod: only the highest version folder is used, so its mod.info or else common's;
    // a lower version folder's or a legacy root mod.info doesn't count.
    public static async Task<ModInfo?> ReadGameModInfoAsync(string modFolder, ILogger logger, CancellationToken ct)
    {
        var version = VersionFolders(modFolder, logger).FirstOrDefault();
        var common = Path.Combine(modFolder, ModFolders.CommonFolder);
        try
        {
            return (version is null ? null : await ModInfo.ReadFileAsync(version, version, ct).ConfigureAwait(false))
                ?? await ModInfo.ReadFileAsync(common, version ?? common, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSkipped(logger, modFolder, ex.Message);
            return null;
        }
    }

    // Highest version first.
    private static List<string> VersionFolders(string modFolder, ILogger logger)
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
        return versions.ConvertAll(entry => entry.Folder);
    }

    // Ordinal order; empty when the folder is missing or unreadable.
    public static List<string> Subfolders(string folder, ILogger logger)
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

    internal readonly record struct ModRoot(string ModsFolder, InstalledModSource Source, string? WorkshopId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Mod lookup skipped {Path}: {Detail}")]
    private static partial void LogSkipped(ILogger logger, string path, string detail);
}
