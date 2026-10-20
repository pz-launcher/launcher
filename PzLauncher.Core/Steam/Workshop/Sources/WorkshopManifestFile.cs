using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;

namespace PzLauncher.Core.Steam.Workshop.Sources;

/// <param name="LatestManifest">Steam's own idea of the latest version; it lags the Web API.</param>
internal sealed record WorkshopManifestEntry(WorkshopInstall Install, string? LatestManifest);

internal sealed record WorkshopManifest(IReadOnlyDictionary<ulong, WorkshopManifestEntry> Items, IReadOnlyList<Error> Warnings);

/// <summary>Each Steam library's <c>steamapps/workshop/appworkshop_108600.acf</c>: the Workshop items Steam installed there.</summary>
internal static partial class WorkshopManifestFile
{
    // The first library that lists an item wins; a library without the file has no items.
    public static async Task<WorkshopManifest> ReadAsync(IReadOnlyList<string> libraries, ILogger logger, CancellationToken ct)
    {
        var items = new Dictionary<ulong, WorkshopManifestEntry>();
        var warnings = new List<Error>();
        foreach (var library in libraries)
        {
            var path = WorkshopFiles.ManifestPath(library);
            var text = await ReadFile.TextIfExistsAsync(path, reason => WorkshopErrors.ManifestUnreadable(path, reason), ct)
                .ConfigureAwait(false);
            if (text is not { } read)
            {
                continue;
            }

            var parsed = read.IsSuccess ? Parse(read.Value, library, path, logger) : read.Error;
            if (!parsed.IsSuccess)
            {
                warnings.Add(Logged(logger, parsed.Error));
                continue;
            }

            foreach (var (id, entry) in parsed.Value)
            {
                items.TryAdd(id, entry);
            }
        }

        return new WorkshopManifest(items, warnings);
    }

    public static Result<IReadOnlyList<KeyValuePair<ulong, WorkshopManifestEntry>>> Parse(string text, string library, string path, ILogger logger)
    {
        var root = SteamKeyValues.ParseBlock(text, WorkshopFiles.ManifestRoot, reason => WorkshopErrors.ManifestUnreadable(path, reason));
        if (!root.IsSuccess)
        {
            return root.Error;
        }

        var details = root.Value.GetChild(WorkshopFiles.ItemDetailsKey);
        var items = new List<KeyValuePair<ulong, WorkshopManifestEntry>>();
        foreach (var (key, entry) in root.Value.GetChild(WorkshopFiles.InstalledItemsKey)?.GetChildren() ?? [])
        {
            if (!WorkshopIds.TryParse(key, out var id)
                || !entry.IsCollection
                || !WorkshopIds.TryParseManifest(entry.GetString(WorkshopFiles.ManifestKey), out var manifest)
                || !WorkshopIds.TryParseUnixTime(entry.GetString(WorkshopFiles.TimeUpdatedKey), out var updated)
                || !WorkshopIds.TryParseSize(entry.GetString(WorkshopFiles.SizeKey), out var size))
            {
                LogSkipped(logger, path, key);
                continue;
            }

            var latest = WorkshopIds.TryParseManifest(details?.GetChild(key)?.GetString(WorkshopFiles.LatestManifestKey), out var value)
                ? value
                : null;
            var install = new WorkshopInstall(WorkshopFiles.ItemFolder(library, key), manifest, updated, size);
            items.Add(new KeyValuePair<ulong, WorkshopManifestEntry>(id, new WorkshopManifestEntry(install, latest)));
        }

        return items;
    }

    private static Error Logged(ILogger logger, Error error)
    {
        LogUnreadable(logger, error.Code.ToWireName(), error.Message);
        return error;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workshop install list {Path}: malformed entry \"{Key}\" skipped")]
    private static partial void LogSkipped(ILogger logger, string path, string key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workshop install list ignored: {Code}: {Detail}")]
    private static partial void LogUnreadable(ILogger logger, string code, string detail);
}
