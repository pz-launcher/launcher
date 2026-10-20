using System.Globalization;
using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;

namespace PzLauncher.Core.Steam.Workshop.Sources;

internal sealed record WorkshopSubscription(ulong Id, DateTimeOffset Subscribed, bool DisabledLocally);

/// <summary>
/// <c>userdata/&lt;account&gt;/ugc/108600_subscriptions.vdf</c>: Steam's cached copy of the account's subscriptions,
/// refreshed from the server, so a change made elsewhere can show up late.
/// </summary>
internal static partial class WorkshopSubscriptionsFile
{
    // A missing file is an account that never subscribed to anything.
    public static async Task<Result<IReadOnlyList<WorkshopSubscription>>> ReadAsync(string path, ILogger logger, CancellationToken ct)
    {
        var text = await ReadFile.TextIfExistsAsync(path, reason => WorkshopErrors.SubscriptionsUnreadable(path, reason), ct)
            .ConfigureAwait(false);
        if (text is not { } read)
        {
            return Array.Empty<WorkshopSubscription>();
        }

        return read.IsSuccess ? Parse(read.Value, path, logger) : read.Error;
    }

    public static Result<IReadOnlyList<WorkshopSubscription>> Parse(string text, string path, ILogger logger)
    {
        var root = SteamKeyValues.ParseBlock(text, WorkshopFiles.SubscriptionsRoot, reason => WorkshopErrors.SubscriptionsUnreadable(path, reason));
        if (!root.IsSuccess)
        {
            return root.Error;
        }

        var items = new List<WorkshopSubscription>();
        var seen = new HashSet<ulong>();
        foreach (var (key, entry) in root.Value.GetChildren())
        {
            // Numbered entries are items; "appid" and "time_last_updated" are metadata.
            if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                continue;
            }

            if (!entry.IsCollection
                || !WorkshopIds.TryParse(entry.GetString(WorkshopFiles.PublishedFileIdKey), out var id)
                || !WorkshopIds.TryParseUnixTime(entry.GetString(WorkshopFiles.TimeSubscribedKey), out var subscribed))
            {
                LogSkipped(logger, path, key);
                continue;
            }

            if (seen.Add(id))
            {
                items.Add(new WorkshopSubscription(id, subscribed, entry.GetString(WorkshopFiles.DisabledLocallyKey) == "1"));
            }
        }

        return items;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workshop subscription list {Path}: malformed entry \"{Key}\" skipped")]
    private static partial void LogSkipped(ILogger logger, string path, string key);
}
