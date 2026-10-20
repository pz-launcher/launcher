using PzLauncher.Core.Steam.Workshop.Helper;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Steam.Workshop.Helper;

public sealed class SteamHelperSessionTests
{
    private const uint Game = 108600;

    private static readonly SteamHelperTimeouts Timeouts = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2));

    private readonly FakeSteamUgcClient _steam = new();
    private readonly List<SteamHelperEvent> _events = [];
    private readonly SteppingTimeProvider _time = new();

    private SteamHelperSession Session() => new(_steam, _events.Add, Timeouts, _time);

    private TimeSpan Elapsed => _time.GetElapsedTime(0);

    [Fact]
    public void Subscribe_SteamConfirms_ReportsEachItemDone()
    {
        Session().ChangeSubscriptions([1, 2], subscribe: true);

        Assert.Equal([new SteamHelperItemDone(1), new SteamHelperItemDone(2)], _events);
        Assert.Equal([1UL, 2UL], _steam.Subscribed);
    }

    [Fact]
    public void Unsubscribe_SteamAnswersAFailure_ReportsItsResult()
    {
        _steam.CallResult = 2;

        Session().ChangeSubscriptions([1], subscribe: false);

        Assert.Equal([new SteamHelperItemFailed(1, SteamHelperItemFailure.UnsubscribeFailed, 2)], _events);
        Assert.Equal([1UL], _steam.Unsubscribed);
    }

    [Fact]
    public void Subscribe_CallRefused_FailsWithoutWaiting()
    {
        _steam.RefuseCalls = true;

        Session().ChangeSubscriptions([1], subscribe: true);

        Assert.Equal([new SteamHelperItemFailed(1, SteamHelperItemFailure.SubscribeFailed, 0)], _events);
        Assert.Equal(TimeSpan.Zero, Elapsed);
    }

    [Fact]
    public void Subscribe_NoAnswer_TimesOutAfterTheCallResultTimeout()
    {
        _steam.AnswerCalls = false;

        Session().ChangeSubscriptions([1], subscribe: true);

        Assert.Equal([new SteamHelperItemFailed(1, SteamHelperItemFailure.TimedOut, 0)], _events);
        Assert.InRange(Elapsed, Timeouts.CallResult, Timeouts.CallResult + TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Subscribe_ResultsOfOtherCallsFirst_WaitsForItsOwn()
    {
        _steam.StrayCallResults = true;
        _steam.AnswerAfterPumps = 3;

        Session().ChangeSubscriptions([1], subscribe: true);

        Assert.Equal([new SteamHelperItemDone(1)], _events);
    }

    [Fact]
    public void Subscribe_AnswerOfAnotherKind_Fails()
    {
        _steam.AnswerCallbackId = SteamCallbackIds.UnsubscribeItemResult;

        Session().ChangeSubscriptions([1], subscribe: true);

        Assert.Equal([new SteamHelperItemFailed(1, SteamHelperItemFailure.SubscribeFailed, 0)], _events);
    }

    [Fact]
    public void Update_DownloadFinishes_ReportsProgressThenDone()
    {
        _steam.OnPumpAfterDownload = pump =>
        {
            switch (pump)
            {
                case 1:
                    _steam.DownloadInfo[1] = (10, 100);
                    _steam.States[1] = SteamItemState.Installed | SteamItemState.Downloading;
                    return [];
                case 2:
                    _steam.DownloadInfo[1] = (100, 100);
                    return [];
                default:
                    _steam.States[1] = SteamItemState.Installed;
                    return [FakeSteamUgcClient.DownloadResult(Game, 1, 1)];
            }
        };

        Session().Update([1]);

        Assert.Equal([new SteamHelperProgress(1, 10, 100), new SteamHelperProgress(1, 100, 100), new SteamHelperItemDone(1)], _events);
        Assert.Equal([1UL], _steam.Subscribed);
        Assert.Equal([1UL], _steam.Downloads);
    }

    [Fact]
    public void Update_AnotherAppsDownloadResult_IsIgnored()
    {
        _steam.States[1] = SteamItemState.Installed;
        _steam.OnPumpAfterDownload = pump => pump switch
        {
            1 => [FakeSteamUgcClient.DownloadResult(Game + 1, 1, 2)],
            5 => [FakeSteamUgcClient.DownloadResult(Game, 1, 1)],
            _ => [],
        };

        Session().Update([1]);

        Assert.Equal([new SteamHelperItemDone(1)], _events);
        Assert.Equal(5, _steam.PumpsSinceDownload);
    }

    [Fact]
    public void Update_DownloadFails_ReportsSteamsResult()
    {
        _steam.OnPumpAfterDownload = pump => pump == 1 ? [FakeSteamUgcClient.DownloadResult(Game, 1, 15)] : [];

        Session().Update([1]);

        Assert.Equal([new SteamHelperItemFailed(1, SteamHelperItemFailure.DownloadFailed, 15)], _events);
    }

    [Fact]
    public void Update_ResultBeforeTheInstallSettles_WaitsUntilItIsUpToDate()
    {
        _steam.States[1] = SteamItemState.Installed | SteamItemState.NeedsUpdate;
        _steam.OnPumpAfterDownload = pump =>
        {
            if (pump == 4)
            {
                _steam.States[1] = SteamItemState.Installed;
            }

            return pump == 1 ? [FakeSteamUgcClient.DownloadResult(Game, 1, 1)] : [];
        };

        Session().Update([1]);

        Assert.Equal([new SteamHelperItemDone(1)], _events);
        Assert.Equal(4, _steam.PumpsSinceDownload);
    }

    [Fact]
    public void Update_NothingChanges_TimesOutAfterTheStallTimeout()
    {
        _steam.States[1] = SteamItemState.Installed | SteamItemState.DownloadPending;

        Session().Update([1]);

        Assert.Equal([new SteamHelperItemFailed(1, SteamHelperItemFailure.TimedOut, 0)], _events);
        Assert.InRange(Elapsed, Timeouts.Stall, Timeouts.Stall + TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Update_ProgressKeepsItAlive_PastTheStallTimeout()
    {
        _steam.OnPumpAfterDownload = pump =>
        {
            if (pump < 3000)
            {
                _steam.DownloadInfo[1] = ((ulong)pump, 3000);
                return [];
            }

            _steam.States[1] = SteamItemState.Installed;
            return [FakeSteamUgcClient.DownloadResult(Game, 1, 1)];
        };

        Session().Update([1]);

        Assert.Equal(new SteamHelperItemDone(1), _events[^1]);
        Assert.True(Elapsed > Timeouts.Stall);
    }

    [Fact]
    public void Update_DownloadNotStarted_Fails()
    {
        _steam.DownloadStarts = false;

        Session().Update([1]);

        Assert.Equal([new SteamHelperItemFailed(1, SteamHelperItemFailure.DownloadNotStarted, 0)], _events);
    }

    [Fact]
    public void Update_SubscribeFails_DoesNotDownload()
    {
        _steam.CallResult = 2;

        Session().Update([1]);

        Assert.Equal([new SteamHelperItemFailed(1, SteamHelperItemFailure.SubscribeFailed, 2)], _events);
        Assert.Empty(_steam.Downloads);
    }

    [Fact]
    public void ListCollections_ReadsBothListsPageByPage_ReportingEachPage()
    {
        _steam.Pages[SteamUserUgcList.Favorited] =
        [
            [.. Enumerable.Range(1, 50).Select(id => FakeSteamUgcClient.Collection((ulong)id))],
            [FakeSteamUgcClient.Collection(51)],
        ];
        _steam.Pages[SteamUserUgcList.Published] = [[FakeSteamUgcClient.Collection(900)]];

        var listed = Assert.IsType<SteamHelperCollections>(Session().ListCollections());

        Assert.Equal(FakeSteamUgcClient.AccountId, listed.AccountId);
        Assert.Equal(51, listed.Favorited.Count);
        Assert.Equal([new SteamHelperCollection(900, "Collection 900", 3)], listed.Published);
        Assert.Equal(
            [
                new SteamHelperPageRead(SteamUserUgcList.Favorited, 1),
                new SteamHelperPageRead(SteamUserUgcList.Favorited, 2),
                new SteamHelperPageRead(SteamUserUgcList.Published, 1),
            ],
            _events);
        Assert.Equal(3, _steam.Released.Count);
    }

    [Fact]
    public void ListCollections_QueryNotAnswered_FailsNamingThePage()
    {
        _steam.AnswerCalls = false;

        var failed = Assert.IsType<SteamHelperFailed>(Session().ListCollections());

        Assert.Equal(SteamHelperFailure.QueryFailed, failed.Reason);
        Assert.Contains("Favorited page 1: no answer", failed.Message, StringComparison.Ordinal);
        Assert.Equal([new SteamHelperPageRead(SteamUserUgcList.Favorited, 1)], _events);
        Assert.Single(_steam.Released);
    }
}
