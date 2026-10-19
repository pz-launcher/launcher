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

        var text = await SteamKeyValues.ReadTextAsync(file, Unreadable, ct).ConfigureAwait(false);
        if (!text.IsSuccess)
        {
            return text.Error;
        }

        var folders = SteamKeyValues.ParseBlock(text.Value, "libraryfolders", Unreadable);
        if (!folders.IsSuccess)
        {
            return folders.Error;
        }

        var paths = new List<string>();
        foreach (var (key, entry) in folders.Value.GetChildren())
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

        var text = await SteamKeyValues.ReadTextAsync(manifestPath, Unreadable, ct).ConfigureAwait(false);
        if (!text.IsSuccess)
        {
            return text.Error;
        }

        var block = SteamKeyValues.ParseBlock(text.Value, "AppState", Unreadable);
        if (!block.IsSuccess)
        {
            return block.Error;
        }

        var state = block.Value;
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
}
