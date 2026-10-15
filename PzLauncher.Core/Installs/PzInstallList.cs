namespace PzLauncher.Core.Installs;

/// <summary>Pass <see cref="Platform.IPlatformEnvironment.PathComparison"/> as <c>pathComparison</c>.</summary>
public static class PzInstallList
{
    public static bool ContainsPath(IEnumerable<PzInstall> installs, string rootPath, StringComparison pathComparison) =>
        installs.Any(i => string.Equals(i.RootPath, rootPath, pathComparison));

    /// <summary>On a duplicate root path the first occurrence wins, also within one list.</summary>
    public static IReadOnlyList<PzInstall> Merge(
        IEnumerable<PzInstall> primary,
        IEnumerable<PzInstall> secondary,
        StringComparison pathComparison)
    {
        var seen = new HashSet<string>(StringComparer.FromComparison(pathComparison));
        var merged = new List<PzInstall>();
        foreach (var install in primary.Concat(secondary))
        {
            if (seen.Add(install.RootPath))
            {
                merged.Add(install);
            }
        }

        return merged;
    }
}
