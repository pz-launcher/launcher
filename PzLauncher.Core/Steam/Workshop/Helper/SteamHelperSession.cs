using PzLauncher.Core.Steam.Workshop.Actions;

namespace PzLauncher.Core.Steam.Workshop.Helper;

/// <summary>Runs one helper action against an initialised <see cref="SteamApi"/>, writing its events.</summary>
// Synchronous on purpose: Steam's manual dispatch and shutdown stay on the thread that initialised it.
internal sealed class SteamHelperSession(SteamApi api, Action<SteamHelperEvent> write, TimeProvider time)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

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
        return result is { Result: SteamCallbackPayloads.ResultOk }
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
                if (result != SteamCallbackPayloads.ResultOk)
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
