using System.Globalization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Net;

namespace PzLauncher.Core.Steam.Workshop.Sources;

internal readonly record struct WorkshopCollectionChild(ulong Id, bool IsCollection);

/// <summary>The keyless Steam Web API methods for Workshop items and collections; answers are cached server-side for about 30 minutes.</summary>
internal sealed partial class WorkshopWebApi(LauncherHttpClient http, ILogger<WorkshopWebApi> logger)
{
    internal TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>An entry for every requested id; <c>null</c> for one the API doesn't show (removed, hidden, malformed).</summary>
    public Task<Result<IReadOnlyDictionary<ulong, WorkshopItemDetails?>>> GetDetailsAsync(IEnumerable<ulong> ids, CancellationToken ct) =>
        FetchAsync(
            ids,
            WorkshopFiles.PublishedFileDetailsUrl,
            "itemcount",
            WorkshopWebApiJsonContext.Default.PublishedFileDetailsJson,
            json => json.Response?.Files,
            "publishedfiledetails",
            file => file.Id,
            ToDetails,
            ct);

    /// <summary>An entry for every requested id, children in collection order; <c>null</c> for one that isn't a collection the API shows.</summary>
    public Task<Result<IReadOnlyDictionary<ulong, IReadOnlyList<WorkshopCollectionChild>?>>> GetCollectionChildrenAsync(
        IEnumerable<ulong> ids, CancellationToken ct) =>
        FetchAsync(
            ids,
            WorkshopFiles.CollectionDetailsUrl,
            "collectioncount",
            WorkshopWebApiJsonContext.Default.CollectionDetailsJson,
            json => json.Response?.Collections,
            "collectiondetails",
            collection => collection.Result == WorkshopFiles.ResultOk ? collection.Id : null,
            ToChildren,
            ct);

    private async Task<Result<IReadOnlyDictionary<ulong, TValue?>>> FetchAsync<TJson, TEntry, TValue>(
        IEnumerable<ulong> ids,
        string url,
        string countKey,
        JsonTypeInfo<TJson> type,
        Func<TJson, IEnumerable<TEntry>?> entriesOf,
        string entriesKey,
        Func<TEntry, ulong?> idOf,
        Func<ulong, TEntry, TValue?> convert,
        CancellationToken ct)
        where TJson : notnull
        where TValue : class
    {
        var values = new Dictionary<ulong, TValue?>();
        foreach (var batch in ids.Distinct().Chunk(WorkshopFiles.MaxIdsPerRequest))
        {
            var response = await PostAsync(url, countKey, batch, type, ct).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                return response.Error;
            }

            if (entriesOf(response.Value) is not { } entries)
            {
                return Logged(WorkshopErrors.ResponseInvalid($"missing \"{entriesKey}\""));
            }

            foreach (var id in batch)
            {
                values[id] = null;
            }

            foreach (var entry in entries)
            {
                if (idOf(entry) is { } id && values.ContainsKey(id))
                {
                    values[id] = convert(id, entry);
                }
            }
        }

        return values;
    }

    private WorkshopItemDetails? ToDetails(ulong id, PublishedFileJson file)
    {
        if (file.Result != WorkshopFiles.ResultOk)
        {
            return null;
        }

        if (file.Title is not { } title
            || !WorkshopIds.TryParseManifest(file.ContentManifest, out var manifest)
            || file.TimeUpdated is not { } seconds
            || !WorkshopIds.TryParseUnixTime(seconds, out var updated)
            || file.FileSize is not { } size)
        {
            LogMalformedItem(logger, id);
            return null;
        }

        return new WorkshopItemDetails(id, title, manifest, updated, size);
    }

    // Children with the same sortorder occur; the API's array order is the order Steam shows.
    private static IReadOnlyList<WorkshopCollectionChild> ToChildren(ulong id, CollectionJson collection) =>
        (collection.Children ?? [])
            .Where(child => child.Id is not null and not 0)
            .Select(child => new WorkshopCollectionChild(child.Id ?? 0, child.FileType == WorkshopFiles.FileTypeCollection))
            .ToList();

    private async Task<Result<T>> PostAsync<T>(string url, string countKey, ulong[] ids, JsonTypeInfo<T> type, CancellationToken ct)
        where T : notnull
    {
        var form = new List<KeyValuePair<string, string>>(ids.Length + 1)
        {
            new(countKey, ids.Length.ToString(CultureInfo.InvariantCulture)),
        };
        for (var i = 0; i < ids.Length; i++)
        {
            form.Add(new(string.Create(CultureInfo.InvariantCulture, $"publishedfileids[{i}]"), ids[i].ToString(CultureInfo.InvariantCulture)));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        return await HttpJson.SendAsync(
            http.Client,
            request,
            type,
            RequestTimeout,
            reason => Logged(WorkshopErrors.NetworkFailed(reason)),
            reason => Logged(WorkshopErrors.ResponseInvalid(reason)),
            ct).ConfigureAwait(false);
    }

    private Error Logged(Error error)
    {
        LogFailed(logger, error.Code.ToWireName(), error.Message);
        return error;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam Web API: {Code}: {Detail}")]
    private static partial void LogFailed(ILogger logger, string code, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam Web API: item {Id} has incomplete details; treated as not shown")]
    private static partial void LogMalformedItem(ILogger logger, ulong id);
}
