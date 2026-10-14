using PzLauncher.Core.Common;

namespace PzLauncher.Core.Steam;

internal sealed class SteamLibraryReader
{
    public static string LibraryFoldersPath(string steamRoot) =>
        Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");

    public static string ManifestPath(string libraryPath, int appId) =>
        Path.Combine(libraryPath, "steamapps", $"appmanifest_{appId}.acf");

    // Paths are returned as written in the file; callers normalize them.
    public async Task<Result<IReadOnlyList<string>>> ReadLibraryPathsAsync(string steamRoot, CancellationToken ct = default)
    {
        var file = LibraryFoldersPath(steamRoot);
        Error Unreadable(string reason) => SteamErrors.LibraryUnreadable(file, reason);

        var text = await ReadAsync(file, Unreadable, ct).ConfigureAwait(false);
        if (!text.IsSuccess)
        {
            return text.Error;
        }

        var parsed = SteamKeyValues.Parse(text.Value);
        if (!parsed.IsSuccess)
        {
            return Unreadable(ReasonOf(parsed.Error));
        }

        var folders = parsed.Value.GetChild("libraryfolders");
        if (folders is null || !folders.IsCollection)
        {
            return Unreadable("missing \"libraryfolders\" block");
        }

        var paths = new List<string>();
        foreach (var (key, entry) in folders.GetChildren())
        {
            // Current format: "0" { "path" "..." }. Old format: "1" "D:\SteamLibrary".
            // Only numeric keys are libraries; others ("contentstatsid", ...) are metadata.
            if (!int.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                continue;
            }

            var path = entry.IsCollection ? entry.GetString("path") : entry.AsText();
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    public async Task<Result<SteamAppManifest>> ReadManifestAsync(string manifestPath, CancellationToken ct = default)
    {
        Error Unreadable(string reason) => SteamErrors.ManifestUnreadable(manifestPath, reason);

        var text = await ReadAsync(manifestPath, Unreadable, ct).ConfigureAwait(false);
        if (!text.IsSuccess)
        {
            return text.Error;
        }

        var parsed = SteamKeyValues.Parse(text.Value);
        if (!parsed.IsSuccess)
        {
            return Unreadable(ReasonOf(parsed.Error));
        }

        var state = parsed.Value.GetChild("AppState");
        if (state is null || !state.IsCollection)
        {
            return Unreadable("missing \"AppState\" block");
        }

        var installDir = state.GetString("installdir");
        if (string.IsNullOrWhiteSpace(installDir)
            || PathNormalizer.HasInvalidPathChars(installDir)
            || Path.IsPathRooted(installDir))
        {
            return Unreadable("missing or invalid \"installdir\"");
        }

        var buildId = state.GetString("buildid");
        if (string.IsNullOrWhiteSpace(buildId))
        {
            return Unreadable("missing \"buildid\"");
        }

        var betaKey = state.GetChild("UserConfig")?.GetString("BetaKey") ?? state.GetChild("MountedConfig")?.GetString("BetaKey");
        var branch = string.IsNullOrWhiteSpace(betaKey) || betaKey.Equals("public", StringComparison.OrdinalIgnoreCase)
            ? null
            : betaKey;

        return new SteamAppManifest(installDir, buildId, branch);
    }

    private static async Task<Result<string>> ReadAsync(string path, Func<string, Error> onFailure, CancellationToken ct)
    {
        try
        {
            return await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return onFailure(ex.Message);
        }
    }

    private static string ReasonOf(Error error) =>
        error.Details.TryGetValue("reason", out var reason) ? reason : error.Message;
}
