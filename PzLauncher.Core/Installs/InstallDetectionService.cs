using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;

namespace PzLauncher.Core.Installs;

internal sealed partial class InstallDetectionService(
    SteamLocator locator,
    SteamLibraryReader reader,
    IPlatformEnvironment platform,
    ILogger<InstallDetectionService> logger) : IInstallDetectionService
{
    private static readonly (int AppId, PzInstallKind Kind)[] Apps =
    [
        (PzSteamApps.Game, PzInstallKind.Game),
        (PzSteamApps.DedicatedServer, PzInstallKind.DedicatedServer),
    ];

    public async Task<Result<InstallScanResult>> ScanAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var warnings = new List<Error>();
        var installs = new List<PzInstall>();

        var roots = locator.FindSteamRoots();
        if (roots.Count == 0)
        {
            AddWarning(warnings, SteamErrors.NotInstalled());
            return new InstallScanResult(installs, warnings);
        }

        var libraries = await CollectLibrariesAsync(roots, warnings, ct).ConfigureAwait(false);
        var comparer = StringComparer.FromComparison(platform.PathComparison);
        var seenInstalls = new HashSet<string>(comparer);

        foreach (var library in libraries)
        {
            foreach (var (appId, kind) in Apps)
            {
                ct.ThrowIfCancellationRequested();
                var manifestPath = SteamLibraryReader.ManifestPath(library, appId);
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                var manifest = await reader.ReadManifestAsync(manifestPath, ct).ConfigureAwait(false);
                if (!manifest.IsSuccess)
                {
                    AddWarning(warnings, manifest.Error);
                    continue;
                }

                var root = PathNormalizer.Normalize(Path.Combine(library, "steamapps", "common", manifest.Value.InstallDir));
                if (!Directory.Exists(root))
                {
                    AddWarning(warnings, InstallErrors.FilesMissing(root, appId));
                    continue;
                }

                if (seenInstalls.Add(root))
                {
                    installs.Add(new PzInstall(kind, root, new SteamAppInfo(manifest.Value.BuildId, manifest.Value.BetaBranch)));
                }
            }
        }

        var ordered = installs
            .OrderBy(i => i.Kind)
            .ThenBy(i => i.RootPath, comparer)
            .ToList();
        return new InstallScanResult(ordered, warnings);
    }

    public Task<Result<PzInstall>> InspectAsync(string path, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Inspect(path));
    }

    private Result<PzInstall> Inspect(string path)
    {
        if (!PathNormalizer.TryNormalizeAbsolute(path, out var root))
        {
            return InstallErrors.InvalidPath(path);
        }

        if (!Directory.Exists(root))
        {
            return InstallErrors.PathNotFound(root);
        }

        HashSet<string> entries;
        try
        {
            entries = new HashSet<string>(
                Directory.EnumerateFileSystemEntries(root).Select(entry => Path.GetFileName(entry)),
                StringComparer.FromComparison(platform.PathComparison));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var error = InstallErrors.PathUnreadable(root, ex.Message);
            LogWarning(logger, error.Code.ToWireName(), error.Message);
            return error;
        }

        if (PzInstallMarkers.DedicatedServer.Any(entries.Contains))
        {
            return new PzInstall(PzInstallKind.DedicatedServer, root, Steam: null);
        }

        if (PzInstallMarkers.Game.Any(entries.Contains))
        {
            return new PzInstall(PzInstallKind.Game, root, Steam: null);
        }

        return InstallErrors.NotAPzInstall(root);
    }

    // A root also listed in libraryfolders.vdf takes the file's spelling: the registry stores it lower-case with forward slashes.
    private async Task<List<string>> CollectLibrariesAsync(IReadOnlyList<string> roots, List<Error> warnings, CancellationToken ct)
    {
        var indexByPath = new Dictionary<string, int>(StringComparer.FromComparison(platform.PathComparison));
        var libraries = new List<string>();

        void Add(string path, bool preferThisSpelling)
        {
            if (!PathNormalizer.TryNormalizeAbsolute(path, out var normalized))
            {
                return;
            }

            if (indexByPath.TryAdd(normalized, libraries.Count))
            {
                libraries.Add(normalized);
            }
            else if (preferThisSpelling)
            {
                libraries[indexByPath[normalized]] = normalized;
            }
        }

        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            Add(root, preferThisSpelling: false);

            var listed = await reader.ReadLibraryPathsAsync(root, ct).ConfigureAwait(false);
            if (!listed.IsSuccess)
            {
                AddWarning(warnings, listed.Error);
                continue;
            }

            foreach (var library in listed.Value)
            {
                Add(library, preferThisSpelling: true);
            }
        }

        return libraries;
    }

    private void AddWarning(List<Error> warnings, Error warning)
    {
        LogWarning(logger, warning.Code.ToWireName(), warning.Message);
        warnings.Add(warning);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Install detection: {Code}: {Detail}")]
    private static partial void LogWarning(ILogger logger, string code, string detail);
}
