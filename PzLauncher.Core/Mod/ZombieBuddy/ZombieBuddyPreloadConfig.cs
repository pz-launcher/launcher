using System.Text.Json;
using System.Text.Json.Nodes;
using PzLauncher.Core.Common;

namespace PzLauncher.Core.Mod.ZombieBuddy;

/// <summary>Edits one <c>preload_mods</c> entry of ZombieBuddy's <c>config.json</c>.</summary>
internal static class ZombieBuddyPreloadConfig
{
    // A duplicate key would otherwise lose one of its values on write.
    private static readonly JsonDocumentOptions ReadOptions = new() { AllowDuplicateProperties = false };

    /// <summary>
    /// Writes nothing when the stored entry is current and, with <paramref name="first"/>, first. ZombieBuddy preloads in
    /// map order; without <paramref name="first"/> a replaced entry keeps its place and a new one goes last.
    /// </summary>
    public static async Task<Result<ZombieBuddyPreload>> EnsureAsync(
        string configFolder, string modId, ZombieBuddyPreloadEntry entry, bool first, StringComparison pathComparison, CancellationToken ct)
    {
        var path = Path.Combine(configFolder, ZombieBuddyFiles.ConfigFile);
        var config = await ReadAsync(path, modId, ct).ConfigureAwait(false);
        if (!config.IsSuccess)
        {
            return config.Error;
        }

        var (root, preload) = config.Value;
        if (preload is null)
        {
            preload = [];
            root[ZombieBuddyFiles.PreloadModsKey] = preload;
        }

        var stored = preload[modId];
        var current = stored is not null && await IsCurrentAsync(stored, entry, pathComparison, ct).ConfigureAwait(false);
        if (current && (!first || preload.IndexOf(modId) == 0))
        {
            return ZombieBuddyPreload.Current;
        }

        var value = current ? stored : new JsonObject { [ZombieBuddyFiles.InfPathKey] = entry.InfPath, [ZombieBuddyFiles.JarPathKey] = entry.JarPath };
        if (first)
        {
            preload.Remove(modId);
            preload.Insert(0, modId, value);
        }
        else
        {
            preload[modId] = value;
        }

        var written = await WriteAsync(configFolder, path, root, modId, ct).ConfigureAwait(false);
        return written.IsSuccess ? ZombieBuddyPreload.Written : written.Error;
    }

    /// <summary>The other entries keep their order; writes nothing when the file or the entry is missing.</summary>
    public static async Task<Result<ZombieBuddyPreload>> RemoveAsync(string configFolder, string modId, CancellationToken ct)
    {
        var path = Path.Combine(configFolder, ZombieBuddyFiles.ConfigFile);
        var config = await ReadAsync(path, modId, ct).ConfigureAwait(false);
        if (!config.IsSuccess)
        {
            return config.Error;
        }

        var (root, preload) = config.Value;
        if (preload is null || !preload.Remove(modId))
        {
            return ZombieBuddyPreload.NotApplicable;
        }

        var written = await WriteAsync(configFolder, path, root, modId, ct).ConfigureAwait(false);
        return written.IsSuccess ? ZombieBuddyPreload.Removed : written.Error;
    }

    // A missing file reads as an empty root; Preload is null when the file has no preload_mods object.
    private static async Task<Result<Config>> ReadAsync(string path, string modId, CancellationToken ct)
    {
        string json;
        try
        {
            json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            json = "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ZombieBuddyErrors.PreloadConfigIoFailed(modId, path, ex.Message);
        }

        var root = Parse(json, modId, path);
        if (!root.IsSuccess)
        {
            return root.Error;
        }

        if (root.Value.TryGetPropertyValue(ZombieBuddyFiles.PreloadModsKey, out var mods) && mods is not (null or JsonObject))
        {
            return ZombieBuddyErrors.PreloadConfigInvalid(modId, path, "preload_mods is not a JSON object.");
        }

        return new Config(root.Value, mods as JsonObject);
    }

    private static async Task<Result> WriteAsync(string configFolder, string path, JsonObject root, string modId, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(configFolder);
            await AtomicJsonFile.WriteAsync(path, root, ct).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ZombieBuddyErrors.PreloadConfigIoFailed(modId, path, ex.Message);
        }
    }

    // ZombieBuddy reads a blank file as the default config.
    private static Result<JsonObject> Parse(string json, string modId, string path)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonObject();
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json, documentOptions: ReadOptions);
        }
        catch (JsonException ex)
        {
            return ZombieBuddyErrors.PreloadConfigInvalid(modId, path, ex.Message);
        }

        return node is JsonObject root ? root : ZombieBuddyErrors.PreloadConfigInvalid(modId, path, "The file holds no JSON object.");
    }

    // The same files, or files ZombieBuddy would load the same jar bytes from: a client and a dedicated server on one
    // machine share config.json and would otherwise overwrite each other's entry at every start.
    private static async Task<bool> IsCurrentAsync(JsonNode stored, ZombieBuddyPreloadEntry entry, StringComparison pathComparison, CancellationToken ct)
    {
        if (stored is not JsonObject value
            || !PathNormalizer.TryNormalizeAbsolute(Text(value[ZombieBuddyFiles.InfPathKey]), out var inf)
            || !PathNormalizer.TryNormalizeAbsolute(Text(value[ZombieBuddyFiles.JarPathKey]), out var jar))
        {
            return false;
        }

        if (string.Equals(inf, entry.InfPath, pathComparison) && string.Equals(jar, entry.JarPath, pathComparison))
        {
            return true;
        }

        return File.Exists(inf) && await FileContent.TryEqualAsync(jar, entry.JarPath, ct).ConfigureAwait(false);
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private sealed record Config(JsonObject Root, JsonObject? Preload);
}
