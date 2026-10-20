using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Steam.Workshop.Sources;

namespace PzLauncher.Core.Steam.Workshop;

internal sealed partial class WorkshopService(
    SteamLocator locator,
    WorkshopLibraries workshop,
    WorkshopWebApi api,
    TimeProvider time,
    ILogger<WorkshopService> logger) : IWorkshopService
{
    // The Web API's server cache holds an answer for about 30 minutes, so asking again sooner gains little.
    internal static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    // Bounds the requests a huge or cyclic tree of nested collections can cause.
    internal const int MaxCollections = 500;

    private readonly ConcurrentDictionary<ulong, CachedDetails> _cache = new();

    public async Task<Result<WorkshopSubscriptions>> GetSubscriptionsAsync(string installRoot, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!PathNormalizer.TryNormalizeAbsolute(installRoot, out var root))
        {
            return Logged(InstallErrors.InvalidPath(installRoot));
        }

        var login = await SteamLoginUsers.LocateCurrentAsync(locator, ct).ConfigureAwait(false);
        if (!login.IsSuccess)
        {
            return Logged(login.Error);
        }

        var subscriptions = await WorkshopSubscriptionsFile.ReadAsync(
            WorkshopFiles.SubscriptionsPath(login.Value.SteamRoot, login.Value.Account.AccountId), logger, ct).ConfigureAwait(false);
        if (!subscriptions.IsSuccess)
        {
            return Logged(subscriptions.Error);
        }

        var libraries = await workshop.CandidatesAsync(root, ct).ConfigureAwait(false);
        var manifest = await WorkshopManifestFile.ReadAsync(libraries, logger, ct).ConfigureAwait(false);
        var items = new List<WorkshopItem>();
        foreach (var subscription in subscriptions.Value)
        {
            var installed = manifest.Items.GetValueOrDefault(subscription.Id) is { } entry && Directory.Exists(entry.Install.Folder) ? entry : null;
            var latest = _cache.GetValueOrDefault(subscription.Id)?.Details;
            items.Add(new WorkshopItem(
                subscription.Id,
                subscription.Subscribed,
                subscription.DisabledLocally,
                installed?.Install,
                latest,
                // Before a check, Steam's own latest manifest is the best known; it lags the Web API.
                WorkshopItem.StateOf(subscription.DisabledLocally, installed?.Install, latest?.Manifest ?? installed?.LatestManifest)));
        }

        return new WorkshopSubscriptions(items, manifest.Warnings);
    }

    public async Task<Result<IReadOnlyList<WorkshopItemDetails>>> CheckUpdatesAsync(IReadOnlyList<ulong> ids, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var details = await DetailsAsync(ids, ct).ConfigureAwait(false);
        if (!details.IsSuccess)
        {
            return details.Error;
        }

        return ids.Distinct()
            .Select(id => details.Value.GetValueOrDefault(id))
            .OfType<WorkshopItemDetails>()
            .ToList();
    }

    public async Task<Result<WorkshopCollection>> GetCollectionAsync(ulong id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var tree = await CollectionTreeAsync(id, ct).ConfigureAwait(false);
        if (!tree.IsSuccess)
        {
            return tree.Error;
        }

        var itemIds = Flatten(id, tree.Value);
        var details = await DetailsAsync([id, .. itemIds], ct).ConfigureAwait(false);
        if (!details.IsSuccess)
        {
            return details.Error;
        }

        return new WorkshopCollection(
            id,
            details.Value.GetValueOrDefault(id)?.Title,
            itemIds.ConvertAll(itemId => new WorkshopCollectionItem(itemId, details.Value.GetValueOrDefault(itemId))));
    }

    public async Task<Result<IReadOnlyList<WorkshopIdKind>>> ClassifyAsync(IReadOnlyList<ulong> ids, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var distinct = ids.Distinct().ToList();
        var children = await api.GetCollectionChildrenAsync(distinct, ct).ConfigureAwait(false);
        if (!children.IsSuccess)
        {
            return children.Error;
        }

        var collections = distinct.Where(id => children.Value.GetValueOrDefault(id) is not null).ToHashSet();
        var details = await DetailsAsync([.. collections], ct).ConfigureAwait(false);
        if (!details.IsSuccess)
        {
            return details.Error;
        }

        return distinct.ConvertAll(id => collections.Contains(id)
            ? new WorkshopIdKind(id, true, details.Value.GetValueOrDefault(id)?.Title)
            : new WorkshopIdKind(id, false, null));
    }

    public async Task<Result<SteamAccount>> GetCurrentAccountAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var login = await SteamLoginUsers.LocateCurrentAsync(locator, ct).ConfigureAwait(false);
        return login.IsSuccess ? login.Value.Account : Logged(login.Error);
    }

    private async Task<Result<IReadOnlyDictionary<ulong, WorkshopItemDetails?>>> DetailsAsync(IReadOnlyList<ulong> ids, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var stale = ids.Distinct().Where(id => !_cache.TryGetValue(id, out var cached) || now - cached.FetchedAt >= CacheLifetime).ToList();
        var details = new Dictionary<ulong, WorkshopItemDetails?>();
        if (stale.Count > 0)
        {
            var fetched = await api.GetDetailsAsync(stale, ct).ConfigureAwait(false);
            if (!fetched.IsSuccess)
            {
                return fetched.Error;
            }

            foreach (var (id, item) in fetched.Value)
            {
                _cache[id] = new CachedDetails(item, now);
                details[id] = item;
            }
        }

        foreach (var id in ids)
        {
            if (!details.ContainsKey(id) && _cache.TryGetValue(id, out var cached))
            {
                details[id] = cached.Details;
            }
        }

        return details;
    }

    // Breadth-first, one request per level of nesting.
    private async Task<Result<Dictionary<ulong, IReadOnlyList<WorkshopCollectionChild>>>> CollectionTreeAsync(ulong id, CancellationToken ct)
    {
        var tree = new Dictionary<ulong, IReadOnlyList<WorkshopCollectionChild>>();
        var requested = new HashSet<ulong> { id };
        List<ulong> pending = [id];
        while (pending.Count > 0)
        {
            var fetched = await api.GetCollectionChildrenAsync(pending, ct).ConfigureAwait(false);
            if (!fetched.IsSuccess)
            {
                return fetched.Error;
            }

            pending = [];
            foreach (var (collectionId, children) in fetched.Value)
            {
                if (children is null)
                {
                    if (collectionId == id)
                    {
                        return Logged(WorkshopErrors.CollectionNotFound(id));
                    }

                    LogNestedSkipped(logger, collectionId, id);
                    continue;
                }

                tree[collectionId] = children;
                foreach (var child in children)
                {
                    if (child.IsCollection && requested.Count < MaxCollections && requested.Add(child.Id))
                    {
                        pending.Add(child.Id);
                    }
                }
            }
        }

        return tree;
    }

    // Depth-first in collection order, without recursion: a nested collection's items take its place, each item once.
    private static List<ulong> Flatten(ulong id, Dictionary<ulong, IReadOnlyList<WorkshopCollectionChild>> tree)
    {
        var items = new List<ulong>();
        var seenItems = new HashSet<ulong>();
        var expanded = new HashSet<ulong> { id };
        var stack = new Stack<(IReadOnlyList<WorkshopCollectionChild> Children, int Next)>();
        stack.Push((tree[id], 0));
        while (stack.TryPop(out var frame))
        {
            if (frame.Next >= frame.Children.Count)
            {
                continue;
            }

            var child = frame.Children[frame.Next];
            stack.Push((frame.Children, frame.Next + 1));
            if (!child.IsCollection)
            {
                if (seenItems.Add(child.Id))
                {
                    items.Add(child.Id);
                }
            }
            else if (expanded.Add(child.Id) && tree.TryGetValue(child.Id, out var nested))
            {
                stack.Push((nested, 0));
            }
        }

        return items;
    }

    private Error Logged(Error error)
    {
        LogFailed(logger, error.Code.ToWireName(), error.Message);
        return error;
    }

    private sealed record CachedDetails(WorkshopItemDetails? Details, DateTimeOffset FetchedAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam Workshop: {Code}: {Detail}")]
    private static partial void LogFailed(ILogger logger, string code, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam Workshop: nested collection {Nested} of {Collection} is not shown; skipped")]
    private static partial void LogNestedSkipped(ILogger logger, ulong nested, ulong collection);
}
