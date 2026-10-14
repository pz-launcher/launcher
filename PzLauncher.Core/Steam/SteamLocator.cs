using PzLauncher.Core.Common;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Steam;

internal sealed class SteamLocator(IPlatformEnvironment platform)
{
    // Priority order; the directories may not exist.
    public IReadOnlyList<string> GetCandidateRoots()
    {
        var raw = new List<string>();
        switch (platform.Os)
        {
            case PlatformKind.Windows:
                if (platform.ReadSteamPathFromRegistry() is { } registryPath)
                {
                    raw.Add(registryPath);
                }

                var programFilesX86 = platform.GetEnvironmentVariable("ProgramFiles(x86)");
                if (!string.IsNullOrWhiteSpace(programFilesX86))
                {
                    raw.Add(Path.Combine(programFilesX86, "Steam"));
                }

                break;
            case PlatformKind.MacOS:
                raw.Add(Path.Combine(platform.HomeDirectory, "Library", "Application Support", "Steam"));
                break;
            case PlatformKind.Linux:
                raw.Add(Path.Combine(platform.HomeDirectory, ".steam", "steam"));
                raw.Add(Path.Combine(platform.HomeDirectory, ".local", "share", "Steam"));
                raw.Add(Path.Combine(platform.HomeDirectory, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"));
                break;
        }

        var seen = new HashSet<string>(StringComparer.FromComparison(platform.PathComparison));
        var result = new List<string>();
        foreach (var candidate in raw)
        {
            if (!PathNormalizer.TryNormalizeAbsolute(candidate, out var normalized))
            {
                continue;
            }

            if (seen.Add(normalized))
            {
                result.Add(normalized);
            }
        }

        return result;
    }

    // Linux ~/.steam/steam is usually a symlink to another candidate; resolve so it isn't reported twice.
    public IReadOnlyList<string> FindSteamRoots()
    {
        var seen = new HashSet<string>(StringComparer.FromComparison(platform.PathComparison));
        var result = new List<string>();
        foreach (var candidate in GetCandidateRoots())
        {
            if (!Directory.Exists(Path.Combine(candidate, "steamapps")))
            {
                continue;
            }

            var resolved = ResolveLink(candidate);
            if (seen.Add(resolved))
            {
                result.Add(resolved);
            }
        }

        return result;
    }

    private static string ResolveLink(string path)
    {
        try
        {
            var target = new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true);
            return target is null ? path : PathNormalizer.Normalize(target.FullName);
        }
        catch (IOException)
        {
            return path;
        }
        catch (UnauthorizedAccessException)
        {
            return path;
        }
    }
}
