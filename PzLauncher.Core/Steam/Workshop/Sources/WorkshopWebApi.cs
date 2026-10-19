using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
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
    public async Task<Result<IReadOnlyDictionary<ulong, WorkshopItemDetails?>>> GetDetailsAsync(IEnumerable<ulong> ids, CancellationToken ct)
    {
        var details = new Dictionary<ulong, WorkshopItemDetails?>();
        foreach (var batch in ids.Distinct().Chunk(WorkshopFiles.MaxIdsPerRequest))
        {
            var response = await PostAsync(
                WorkshopFiles.PublishedFileDetailsUrl, "itemcount", batch, WorkshopWebApiJsonContext.Default.PublishedFileDetailsJson, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                return response.Error;
            }

            if (response.Value.Response?.Files is not { } files)
            {
                return Logged(WorkshopErrors.ResponseInvalid("missing \"publishedfiledetails\""));
            }

            foreach (var id in batch)
            {
                details[id] = null;
            }

            foreach (var file in files)
            {
                if (file.Id is { } id && details.ContainsKey(id))
                {
                    details[id] = ToDetails(id, file);
                }
            }
        }

        return details;
    }

    /// <summary>An entry for every requested id, children in collection order; <c>null</c> for one that isn't a collection the API shows.</summary>
    public async Task<Result<IReadOnlyDictionary<ulong, IReadOnlyList<WorkshopCollectionChild>?>>> GetCollectionChildrenAsync(
        IEnumerable<ulong> ids, CancellationToken ct)
    {
        var collections = new Dictionary<ulong, IReadOnlyList<WorkshopCollectionChild>?>();
        foreach (var batch in ids.Distinct().Chunk(WorkshopFiles.MaxIdsPerRequest))
        {
            var response = await PostAsync(
                WorkshopFiles.CollectionDetailsUrl, "collectioncount", batch, WorkshopWebApiJsonContext.Default.CollectionDetailsJson, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                return response.Error;
            }

            if (response.Value.Response?.Collections is not { } found)
            {
                return Logged(WorkshopErrors.ResponseInvalid("missing \"collectiondetails\""));
            }

            foreach (var id in batch)
            {
                collections[id] = null;
            }

            foreach (var collection in found)
            {
                if (collection.Id is { } id && collections.ContainsKey(id) && collection.Result == WorkshopFiles.ResultOk)
                {
                    // Children with the same sortorder occur; the API's array order is the order Steam shows.
                    collections[id] = (collection.Children ?? [])
                        .Where(child => child.Id is not null and not 0)
                        .Select(child => new WorkshopCollectionChild(child.Id ?? 0, child.FileType == WorkshopFiles.FileTypeCollection))
                        .ToList();
                }
            }
        }

        return collections;
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

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Logged(WorkshopErrors.NetworkFailed(string.Create(CultureInfo.InvariantCulture, $"HTTP {(int)response.StatusCode}")));
            }

            var body = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
            await using var _ = body.ConfigureAwait(false);
            var json = await JsonSerializer.DeserializeAsync(body, type, timeoutCts.Token).ConfigureAwait(false);
            return json is null ? Logged(WorkshopErrors.ResponseInvalid("empty body")) : json;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Logged(WorkshopErrors.NetworkFailed("timed out"));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return Logged(WorkshopErrors.NetworkFailed(ex.Message));
        }
        catch (JsonException ex)
        {
            return Logged(WorkshopErrors.ResponseInvalid(ex.Message));
        }
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
