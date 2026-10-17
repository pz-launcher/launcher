using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;

namespace PzLauncher.Core.Mod.Collections;

/// <summary>
/// The launcher's order per collection, next to the game's collection file: the game rewrites each collection's ids in
/// hash order on every save.
/// </summary>
internal static partial class ModCollectionOrderFile
{
    /// <summary>Empty when missing; an unreadable or invalid file is ignored.</summary>
    public static async Task<Dictionary<string, IReadOnlyList<string>>> ReadAsync(string cache, ILogger logger, CancellationToken ct)
    {
        var path = ModCollectionFiles.OrderFile(cache);
        try
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize(json, ModCollectionOrderJsonContext.Default.ModCollectionOrderDocument);
            // The serializer leaves null dictionary values and list elements to the reader.
            return document is null
                ? new(StringComparer.Ordinal)
                : document.Collections.Where(entry => entry.Value is not null).ToDictionary(
                    entry => entry.Key,
                    entry => (IReadOnlyList<string>)[.. entry.Value.Where(id => id is not null)],
                    StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LogIgnored(logger, path, ex.Message);
            return new(StringComparer.Ordinal);
        }
    }

    public static async Task<Result> WriteAsync(string cache, IReadOnlyDictionary<string, IReadOnlyList<string>> orders, CancellationToken ct)
    {
        var path = ModCollectionFiles.OrderFile(cache);
        var document = new ModCollectionOrderDocument(orders);
        var node = JsonSerializer.SerializeToNode(document, ModCollectionOrderJsonContext.Default.ModCollectionOrderDocument) ?? new JsonObject();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? cache);
            await AtomicJsonFile.WriteAsync(path, node, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ModCollectionErrors.FileWriteFailed(path, ex.Message);
        }

        return Result.Ok();
    }

    /// <summary>The remembered ids still in the collection, in remembered order, then its other ids in their own order.</summary>
    public static IReadOnlyList<string> Reconcile(IReadOnlyList<string> modIds, IReadOnlyList<string>? remembered)
    {
        if (remembered is null)
        {
            return modIds;
        }

        var current = new HashSet<string>(modIds, StringComparer.Ordinal);
        var ordered = new List<string>();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in remembered)
        {
            if (current.Contains(id) && placed.Add(id))
            {
                ordered.Add(id);
            }
        }

        ordered.AddRange(modIds.Where(placed.Add));
        return ordered;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mod collection order file ignored: {Path}: {Detail}")]
    private static partial void LogIgnored(ILogger logger, string path, string detail);
}
