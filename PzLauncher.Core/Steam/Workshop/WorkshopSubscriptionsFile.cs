using System.Globalization;
using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;

namespace PzLauncher.Core.Steam.Workshop;

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
        string text;
        try
        {
            text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Array.Empty<WorkshopSubscription>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return WorkshopErrors.SubscriptionsUnreadable(path, ex.Message);
        }

        return Parse(text, path, logger);
    }

    public static Result<IReadOnlyList<WorkshopSubscription>> Parse(string text, string path, ILogger logger)
    {
        var parsed = SteamKeyValues.Parse(text);
        if (!parsed.IsSuccess)
        {
            return WorkshopErrors.SubscriptionsUnreadable(path, parsed.Error.Message);
        }

        var root = parsed.Value.GetChild(WorkshopFiles.SubscriptionsRoot);
        if (root is null || !root.IsCollection)
        {
            return WorkshopErrors.SubscriptionsUnreadable(path, $"missing \"{WorkshopFiles.SubscriptionsRoot}\" block");
        }

        var items = new List<WorkshopSubscription>();
        var seen = new HashSet<ulong>();
        foreach (var (key, entry) in root.GetChildren())
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
