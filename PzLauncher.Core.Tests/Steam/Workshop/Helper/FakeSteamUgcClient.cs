using System.Buffers.Binary;
using PzLauncher.Core.Steam.Workshop.Helper;

namespace PzLauncher.Core.Tests.Steam.Workshop.Helper;

/// <summary>
/// Steam as a <see cref="SteamHelperSession"/> sees it, with the pack 8 (Windows) callback layouts. Each call is answered on the pump after
/// <see cref="AnswerAfterPumps"/> more pumps; download progress and results come from <see cref="OnPumpAfterDownload"/>.
/// </summary>
internal sealed class FakeSteamUgcClient : ISteamUgcClient
{
    public const uint AccountId = 12345678;

    private readonly List<(int Due, SteamCallback Callback)> _pending = [];
    private readonly Dictionary<ulong, (SteamUserUgcList List, uint Page)> _queries = [];
    private ulong _nextCall = 100;
    private int _pumps;

    /// <summary>EResult in each subscribe or unsubscribe answer.</summary>
    public int CallResult { get; set; } = 1;

    /// <summary>The callback id each subscribe answer comes with; <c>null</c> for the right one.</summary>
    public int? AnswerCallbackId { get; set; }

    public bool AnswerCalls { get; set; } = true;

    /// <summary>Calls return 0, as when Steam refuses them.</summary>
    public bool RefuseCalls { get; set; }

    public int AnswerAfterPumps { get; set; }

    /// <summary>A call result for an unrelated call comes before each answer.</summary>
    public bool StrayCallResults { get; set; }

    public bool DownloadStarts { get; set; } = true;

    public Dictionary<ulong, SteamItemState> States { get; } = [];

    public Dictionary<ulong, (ulong Downloaded, ulong Total)> DownloadInfo { get; } = [];

    /// <summary>Called on each pump after the last <see cref="DownloadItem"/> with the pump's number, from 1; returns callbacks to deliver.</summary>
    public Func<int, IEnumerable<SteamCallback>>? OnPumpAfterDownload { get; set; }

    /// <summary>The results of each list's pages, from page 1; Steam's total is all of them.</summary>
    public Dictionary<SteamUserUgcList, UgcDetails[][]> Pages { get; } = [];

    public List<ulong> Subscribed { get; } = [];

    public List<ulong> Unsubscribed { get; } = [];

    public List<ulong> Downloads { get; } = [];

    public List<ulong> Released { get; } = [];

    public int PumpsSinceDownload { get; private set; }

    public bool LargeCallbackPack => true;

    public static SteamCallback DownloadResult(uint appId, ulong id, int result)
    {
        var payload = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, appId);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(8), id);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16), result);
        return new SteamCallback(SteamCallbackIds.DownloadItemResult, 0, false, payload);
    }

    public static UgcDetails Collection(ulong id) => new(id, 1, 2, $"Collection {id}", 3);

    public SteamItemState GetItemState(ulong id) => States.GetValueOrDefault(id);

    public (ulong Downloaded, ulong Total) GetItemDownloadInfo(ulong id) => DownloadInfo.GetValueOrDefault(id);

    public ulong SubscribeItem(ulong id)
    {
        Subscribed.Add(id);
        return Answer(AnswerCallbackId ?? SteamCallbackIds.SubscribeItemResult, PublishedFileResult(id));
    }

    public ulong UnsubscribeItem(ulong id)
    {
        Unsubscribed.Add(id);
        return Answer(SteamCallbackIds.UnsubscribeItemResult, PublishedFileResult(id));
    }

    public bool DownloadItem(ulong id, bool highPriority)
    {
        Downloads.Add(id);
        PumpsSinceDownload = 0;
        return DownloadStarts;
    }

    public uint GetAccountId() => AccountId;

    public ulong CreateUserCollectionsQuery(uint accountId, SteamUserUgcList list, uint page)
    {
        var handle = _nextCall++;
        _queries[handle] = (list, page);
        return handle;
    }

    public ulong SendQuery(ulong handle)
    {
        var (list, page) = _queries[handle];
        var pages = Pages.GetValueOrDefault(list) ?? [];
        var returned = page <= pages.Length ? pages[page - 1].Length : 0;
        var payload = new byte[20];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, handle);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12), (uint)returned);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16), (uint)pages.Sum(items => items.Length));
        return Answer(SteamCallbackIds.UgcQueryCompleted, payload);
    }

    public UgcDetails? GetQueryResult(ulong handle, uint index)
    {
        var (list, page) = _queries[handle];
        var items = Pages[list][page - 1];
        return index < items.Length ? items[index] : null;
    }

    public void ReleaseQuery(ulong handle) => Released.Add(handle);

    public List<SteamCallback> Pump()
    {
        _pumps++;
        var due = _pending.Where(entry => entry.Due <= _pumps).ToList();
        _pending.RemoveAll(due.Contains);
        List<SteamCallback> callbacks = [.. due.Select(entry => entry.Callback)];
        if (Downloads.Count > 0)
        {
            PumpsSinceDownload++;
            callbacks.AddRange(OnPumpAfterDownload?.Invoke(PumpsSinceDownload) ?? []);
        }

        return callbacks;
    }

    private byte[] PublishedFileResult(ulong id)
    {
        var payload = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(payload, CallResult);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(8), id);
        return payload;
    }

    private ulong Answer(int callbackId, byte[] payload)
    {
        if (RefuseCalls)
        {
            return 0;
        }

        var call = _nextCall++;
        var due = _pumps + 1 + AnswerAfterPumps;
        if (StrayCallResults)
        {
            _pending.Add((_pumps + 1, new SteamCallback(callbackId, call + 1000, true, [])));
        }

        if (AnswerCalls)
        {
            _pending.Add((due, new SteamCallback(callbackId, call, false, payload)));
        }

        return call;
    }
}
