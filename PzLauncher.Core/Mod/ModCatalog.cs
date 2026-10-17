using Microsoft.Extensions.Logging;

namespace PzLauncher.Core.Mod;

/// <summary>Every installed mod in the game's lookup order; the first copy of an id wins.</summary>
internal static class ModCatalog
{
    public static async Task<IReadOnlyList<InstalledMod>> ReadAsync(
        string? cache, IReadOnlyList<string> libraries, ILogger logger, CancellationToken ct)
    {
        var mods = new List<InstalledMod>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in ModLocator.Roots(null, cache, libraries, logger))
        {
            foreach (var modFolder in ModLocator.Subfolders(root.ModsFolder, logger))
            {
                ct.ThrowIfCancellationRequested();
                if (await ModLocator.ReadGameModInfoAsync(modFolder, logger, ct).ConfigureAwait(false) is not { } info
                    || info.Value(ModFolders.IdKey) is not { } id
                    || !seen.Add(id))
                {
                    continue;
                }

                mods.Add(new InstalledMod(
                    id,
                    info.Value(ModFolders.NameKey) ?? id,
                    root.WorkshopId,
                    root.Source,
                    IdList(info, ModFolders.RequireKey),
                    IdList(info, ModFolders.LoadAfterKey),
                    IdList(info, ModFolders.LoadBeforeKey),
                    IdList(info, ModFolders.IncompatibleKey)));
            }
        }

        return mods;
    }

    // B42 writes ids as "\id"; the part after the last backslash is the mod id.
    internal static IReadOnlyList<string> ParseIdList(string? value)
    {
        if (value is null)
        {
            return [];
        }

        var ids = new List<string>();
        foreach (var part in value.Split(','))
        {
            var id = part.Trim();
            id = id[(id.LastIndexOf('\\') + 1)..].Trim();
            if (id.Length > 0)
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static IReadOnlyList<string> IdList(ModInfo info, string key) => ParseIdList(info.Value(key));
}
