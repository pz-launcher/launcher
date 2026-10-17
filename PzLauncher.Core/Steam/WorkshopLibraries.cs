using System.Globalization;
using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Steam;

internal sealed partial class WorkshopLibraries(
    SteamLocator locator,
    SteamLibraryReader reader,
    IPlatformEnvironment platform,
    ILogger<WorkshopLibraries> logger)
{
    public static string ContentFolder(string library) =>
        Path.Combine(library, "steamapps", "workshop", "content", PzSteamApps.Game.ToString(CultureInfo.InvariantCulture));

    // ZombieBuddy's installer order: the game's own library, then each Steam root and the libraries it lists.
    public async Task<IReadOnlyList<string>> CandidatesAsync(string installRoot, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.FromComparison(platform.PathComparison));
        var libraries = new List<string>();

        void Add(string? path)
        {
            if (PathNormalizer.TryNormalizeAbsolute(path, out var normalized) && seen.Add(normalized))
            {
                libraries.Add(normalized);
            }
        }

        Add(OwnLibrary(installRoot));
        foreach (var root in locator.FindSteamRoots())
        {
            ct.ThrowIfCancellationRequested();
            Add(root);
            var listed = await reader.ReadLibraryPathsAsync(root, ct).ConfigureAwait(false);
            if (!listed.IsSuccess)
            {
                LogIgnored(logger, listed.Error.Code.ToWireName(), listed.Error.Message);
                continue;
            }

            foreach (var library in listed.Value)
            {
                Add(library);
            }
        }

        return libraries;
    }

    // A Steam install root is <library>/steamapps/common/<installdir>; a manual folder has no own library.
    private string? OwnLibrary(string installRoot)
    {
        var common = Path.GetDirectoryName(installRoot);
        var steamapps = Path.GetDirectoryName(common);
        return common is not null
            && steamapps is not null
            && string.Equals(Path.GetFileName(common), "common", platform.PathComparison)
            && string.Equals(Path.GetFileName(steamapps), "steamapps", platform.PathComparison)
                ? Path.GetDirectoryName(steamapps)
                : null;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Steam library list ignored: {Code}: {Detail}")]
    private static partial void LogIgnored(ILogger logger, string code, string detail);
}
