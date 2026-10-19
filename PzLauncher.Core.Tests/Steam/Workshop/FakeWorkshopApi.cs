using System.Globalization;
using System.Net;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Steam.Workshop;

/// <summary>Answers both Web API methods from the items and collections added, by the ids in each form body.</summary>
internal sealed class FakeWorkshopApi
{
    private readonly Dictionary<ulong, string> _items = [];
    private readonly Dictionary<ulong, (ulong Id, int FileType)[]> _collections = [];
    private readonly List<IReadOnlyList<ulong>> _detailRequests = [];
    private readonly List<IReadOnlyList<ulong>> _collectionRequests = [];

    public FakeWorkshopApi()
    {
        Handler.Respond(WorkshopFiles.PublishedFileDetailsUrl, async (request, ct) =>
        {
            var ids = await IdsAsync(request, "itemcount", ct);
            Record(_detailRequests, ids);
            var entries = ids.Select(id => _items.TryGetValue(id, out var item) ? item : NotFound(id));
            return FakeHttpHandler.JsonResponse($$$"""{"response":{"result":1,"resultcount":{{{ids.Count}}},"publishedfiledetails":[{{{string.Join(',', entries)}}}]}}""");
        });
        Handler.Respond(WorkshopFiles.CollectionDetailsUrl, async (request, ct) =>
        {
            var ids = await IdsAsync(request, "collectioncount", ct);
            Record(_collectionRequests, ids);
            var entries = ids.Select(id => _collections.TryGetValue(id, out var children) ? CollectionEntry(id, children) : NotFound(id));
            return FakeHttpHandler.JsonResponse($$$"""{"response":{"result":1,"resultcount":0,"collectiondetails":[{{{string.Join(',', entries)}}}]}}""");
        });
    }

    public FakeHttpHandler Handler { get; } = new();

    public IReadOnlyList<IReadOnlyList<ulong>> DetailRequests
    {
        get
        {
            lock (_detailRequests)
            {
                return [.. _detailRequests];
            }
        }
    }

    public IReadOnlyList<IReadOnlyList<ulong>> CollectionRequests
    {
        get
        {
            lock (_collectionRequests)
            {
                return [.. _collectionRequests];
            }
        }
    }

    public static string NotFound(ulong id) => $$"""{"publishedfileid":"{{id}}","result":9}""";

    public static string Item(ulong id, string title, string manifest, long updated = 1791483143, long size = 1000) =>
        $$"""{"publishedfileid":"{{id}}","result":1,"creator_app_id":108600,"consumer_app_id":108600,"file_size":"{{size}}","hcontent_file":"{{manifest}}","title":"{{title}}","time_updated":{{updated}},"visibility":0,"tags":[]}""";

    public void AddItem(ulong id, string title, string manifest = "100", long updated = 1791483143, long size = 1000) =>
        _items[id] = Item(id, title, manifest, updated, size);

    public void AddCollection(ulong id, string title, params (ulong Id, int FileType)[] children)
    {
        _items[id] = Item(id, title, "1");
        _collections[id] = children;
    }

    private static string CollectionEntry(ulong id, (ulong Id, int FileType)[] children) =>
        $$"""{"publishedfileid":"{{id}}","result":1,"children":[{{string.Join(',', children.Select((child, i) => $$"""{"publishedfileid":"{{child.Id}}","sortorder":{{i}},"filetype":{{child.FileType}}}"""))}}]}""";

    private static async Task<IReadOnlyList<ulong>> IdsAsync(HttpRequestMessage request, string countKey, CancellationToken ct)
    {
        Assert.Equal(HttpMethod.Post, request.Method);
        var form = (await request.Content!.ReadAsStringAsync(ct))
            .Split('&')
            .Select(pair => pair.Split('='))
            .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]), StringComparer.Ordinal);
        var count = int.Parse(form[countKey], CultureInfo.InvariantCulture);
        Assert.Equal(count + 1, form.Count);
        return [.. Enumerable.Range(0, count).Select(i => ulong.Parse(form[$"publishedfileids[{i}]"], CultureInfo.InvariantCulture))];
    }

    private static void Record(List<IReadOnlyList<ulong>> requests, IReadOnlyList<ulong> ids)
    {
        lock (requests)
        {
            requests.Add(ids);
        }
    }
}
