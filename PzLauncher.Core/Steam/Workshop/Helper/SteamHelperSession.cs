using PzLauncher.Core.Steam.Workshop.Actions;

namespace PzLauncher.Core.Steam.Workshop.Helper;

/// <summary>Runs one helper action against an initialised <see cref="SteamApi"/>, writing its events.</summary>
// Synchronous on purpose: Steam's manual dispatch and shutdown stay on the thread that initialised it.
internal sealed class SteamHelperSession(SteamApi api, Action<SteamHelperEvent> write, TimeProvider time)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    // Bounds a run if Steam keeps answering full pages.
    internal const uint MaxPages = 100;

    private readonly Dictionary<ulong, SteamCallback> _callResults = [];
    private readonly Dictionary<ulong, int> _downloadResults = [];

    public TimeSpan CallResultTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long an update may go without a change in progress or state before it fails.</summary>
    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public void ChangeSubscriptions(IReadOnlyList<ulong> ids, bool subscribe)
    {
        foreach (var id in ids)
        {
            write(ChangeSubscription(id, subscribe) is { } failed ? failed : new SteamHelperItemDone(id));
        }
    }

    public void Update(IReadOnlyList<ulong> ids)
    {
        foreach (var id in ids)
        {
            write(UpdateItem(id));
        }
    }

    /// <returns><see cref="SteamHelperCollections"/>, or <see cref="SteamHelperFailed"/> for the first query Steam didn't answer.</returns>
    public SteamHelperEvent ListCollections()
    {
        var account = api.GetAccountId();
        List<WorkshopCollectionSummary> favorited = [], published = [];
        var failed = ReadAllPages(SteamUserUgcList.Favorited, page => QueryPage(account, SteamUserUgcList.Favorited, page), favorited)
            ?? ReadAllPages(SteamUserUgcList.Published, page => QueryPage(account, SteamUserUgcList.Published, page), published);
        return failed ?? (SteamHelperEvent)new SteamHelperCollections(new WorkshopUserCollections(account, favorited, published));
    }

    /// <summary>Reads pages from 1 until a short page or Steam's total, adding each collection once.</summary>
    /// <returns>The failure of the first page Steam didn't answer with OK, <c>null</c> once all were read.</returns>
    internal static SteamHelperFailed? ReadAllPages(SteamUserUgcList list, Func<uint, SteamUgcPage> queryPage, List<WorkshopCollectionSummary> collections)
    {
        var seen = new HashSet<ulong>();
        ulong read = 0;
        for (uint page = 1; page <= MaxPages; page++)
        {
            var answer = queryPage(page);
            if (answer.Result != WorkshopFiles.ResultOk)
            {
                return new SteamHelperFailed(
                    SteamHelperFailure.QueryFailed,
                    answer.Result == 0 ? $"{list} page {page}: no answer from Steam" : $"{list} page {page}: EResult {answer.Result}");
            }

            foreach (var item in answer.Items)
            {
                if (item.Result == WorkshopFiles.ResultOk && item.FileType == WorkshopFiles.FileTypeCollection && seen.Add(item.Id))
                {
                    collections.Add(new WorkshopCollectionSummary(item.Id, item.Title.Length > 0 ? item.Title : null, item.ChildCount));
                }
            }

            read += answer.Returned;
            if (answer.Returned < SteamUgcQuery.ResultsPerPage || read >= answer.TotalMatching)
            {
                break;
            }
        }

        return null;
    }

    private SteamUgcPage QueryPage(uint account, SteamUserUgcList list, uint page)
    {
        var handle = api.CreateUserCollectionsQuery(account, list, page);
        if (handle == SteamUgcQuery.InvalidHandle)
        {
            return SteamUgcPage.NoAnswer;
        }

        try
        {
            var call = api.SendQuery(handle);
            if (call == 0
                || !TryWaitForCallResult(call, out var callResult)
                || callResult.Failed
                || callResult.Id != SteamCallbackIds.UgcQueryCompleted
                || SteamCallbackPayloads.ReadUgcQueryCompleted(callResult.Payload) is not { } completed)
            {
                return SteamUgcPage.NoAnswer;
            }

            if (completed.Result != WorkshopFiles.ResultOk)
            {
                return new SteamUgcPage(completed.Result, 0, 0, []);
            }

            var items = new List<UgcDetails>();
            for (uint index = 0; index < completed.Returned; index++)
            {
                if (api.GetQueryResult(handle, index) is { } details)
                {
                    items.Add(details);
                }
            }

            return new SteamUgcPage(completed.Result, completed.Returned, completed.TotalMatching, items);
        }
        finally
        {
            api.ReleaseQuery(handle);
        }
    }

    /// <returns>The item's failure, <c>null</c> once Steam confirmed the change.</returns>
    private SteamHelperItemFailed? ChangeSubscription(ulong id, bool subscribe)
    {
        var (failure, expectedCallback) = subscribe
            ? (WorkshopItemFailure.SubscribeFailed, SteamCallbackIds.SubscribeItemResult)
            : (WorkshopItemFailure.UnsubscribeFailed, SteamCallbackIds.UnsubscribeItemResult);
        var call = subscribe ? api.SubscribeItem(id) : api.UnsubscribeItem(id);
        if (call == 0)
        {
            return new SteamHelperItemFailed(id, failure, 0);
        }

        if (!TryWaitForCallResult(call, out var callResult))
        {
            return new SteamHelperItemFailed(id, WorkshopItemFailure.TimedOut, 0);
        }

        var result = callResult.Failed || callResult.Id != expectedCallback
            ? null
            : SteamCallbackPayloads.ReadPublishedFileResult(callResult.Payload, api.LargeCallbackPack);
        return result is { Result: WorkshopFiles.ResultOk }
            ? null
            : new SteamHelperItemFailed(id, failure, result?.Result ?? 0);
    }

    // Subscribing to an already-subscribed item makes Steam fetch its latest info, so the download
    // that follows gets the newest revision even when Steam hadn't noticed it yet.
    private SteamHelperEvent UpdateItem(ulong id)
    {
        if (ChangeSubscription(id, subscribe: true) is { } failed)
        {
            return failed;
        }

        _downloadResults.Remove(id);
        if (!api.DownloadItem(id, highPriority: true))
        {
            return new SteamHelperItemFailed(id, WorkshopItemFailure.DownloadNotStarted, 0);
        }

        var lastChange = time.GetTimestamp();
        (ulong Bytes, ulong Total, SteamItemState State) last = default;
        while (true)
        {
            Pump();
            var state = api.GetItemState(id);
            if (_downloadResults.TryGetValue(id, out var result))
            {
                if (result != WorkshopFiles.ResultOk)
                {
                    return new SteamHelperItemFailed(id, WorkshopItemFailure.DownloadFailed, result);
                }

                if (IsUpToDate(state))
                {
                    return new SteamHelperItemDone(id);
                }
            }

            var (bytes, total) = api.GetItemDownloadInfo(id);
            var current = (bytes, total, state);
            if (current != last)
            {
                if ((bytes, total) != (last.Bytes, last.Total))
                {
                    write(new SteamHelperProgress(id, bytes, total));
                }

                last = current;
                lastChange = time.GetTimestamp();
            }
            else if (time.GetElapsedTime(lastChange) > StallTimeout)
            {
                return new SteamHelperItemFailed(id, WorkshopItemFailure.TimedOut, 0);
            }

            Thread.Sleep(PollInterval);
        }
    }

    private static bool IsUpToDate(SteamItemState state)
        => state.HasFlag(SteamItemState.Installed)
            && (state & (SteamItemState.NeedsUpdate | SteamItemState.Downloading | SteamItemState.DownloadPending)) == 0;

    private bool TryWaitForCallResult(ulong call, out SteamCallback result)
    {
        var start = time.GetTimestamp();
        while (true)
        {
            Pump();
            if (_callResults.Remove(call, out result))
            {
                return true;
            }

            if (time.GetElapsedTime(start) > CallResultTimeout)
            {
                return false;
            }

            Thread.Sleep(PollInterval);
        }
    }

    private void Pump()
    {
        foreach (var callback in api.Pump())
        {
            if (callback.ApiCall != 0)
            {
                _callResults[callback.ApiCall] = callback;
            }
            else if (callback.Id == SteamCallbackIds.DownloadItemResult
                     && SteamCallbackPayloads.ReadDownloadItemResult(callback.Payload, api.LargeCallbackPack) is { } download
                     && download.AppId == PzSteamApps.Game)
            {
                // Fires for every app's downloads; the latest result per item wins.
                _downloadResults[download.Id] = download.Result;
            }
        }
    }
}

/// <param name="Result">Steam's EResult for the query; 0 (k_EResultNone) when Steam refused it or no answer came in time.</param>
/// <param name="Returned">Results on this page, including any Steam then had no details for.</param>
internal sealed record SteamUgcPage(int Result, uint Returned, uint TotalMatching, IReadOnlyList<UgcDetails> Items)
{
    public static SteamUgcPage NoAnswer { get; } = new(0, 0, 0, []);
}
