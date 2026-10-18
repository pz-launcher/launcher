using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Net;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Tests.TestSupport;
using static PzLauncher.Core.Tests.TestSupport.UpdateTestKit;

namespace PzLauncher.Core.Tests.Steam.Workshop;

public sealed class WorkshopWebApiTests : IDisposable
{
    private readonly FakeWorkshopApi _fake = new();
    private readonly FakeHttpHandler _handler = new();
    private readonly LauncherHttpClient _fakeHttp;
    private readonly LauncherHttpClient _http;

    public WorkshopWebApiTests()
    {
        _fakeHttp = new LauncherHttpClient(_fake.Handler, V("0.2.0"));
        _http = new LauncherHttpClient(_handler, V("0.2.0"));
    }

    public void Dispose()
    {
        _fakeHttp.Dispose();
        _http.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WorkshopWebApi Fake => new(_fakeHttp, NullLogger<WorkshopWebApi>.Instance);

    private WorkshopWebApi Api => new(_http, NullLogger<WorkshopWebApi>.Instance);

    [Fact]
    public async Task GetDetailsAsync_RealResponse_ReadsTheItemAndNullForResult9()
    {
        _handler.RespondJson(WorkshopFiles.PublishedFileDetailsUrl, Fixtures.Read("Steam", "published-file-details.json"));

        var details = ResultAssert.Ok(await Api.GetDetailsAsync([2409333430, 1], Ct));

        Assert.Equal(
            new WorkshopItemDetails(2409333430, "'91 RANGE ROVER Classic", "2026051810389825471", DateTimeOffset.FromUnixTimeSeconds(1791483143), 18928379),
            details[2409333430]);
        Assert.Null(details[1]);
    }

    [Fact]
    public async Task GetDetailsAsync_MoreThanOneBatch_SplitsAtTheLimitAndAnswersEveryId()
    {
        var ids = Enumerable.Range(1, WorkshopFiles.MaxIdsPerRequest + 1).Select(i => (ulong)i).ToList();
        _fake.AddItem(1001, "Last");

        var details = ResultAssert.Ok(await Fake.GetDetailsAsync(ids, Ct));

        Assert.Equal([WorkshopFiles.MaxIdsPerRequest, 1], _fake.DetailRequests.Select(request => request.Count));
        Assert.Equal(ids, _fake.DetailRequests.SelectMany(request => request));
        Assert.Equal(ids.Count, details.Count);
        Assert.Equal("Last", details[1001]?.Title);
    }

    [Fact]
    public async Task GetDetailsAsync_RepeatedId_IsRequestedOnce()
    {
        _fake.AddItem(5, "Five");

        ResultAssert.Ok(await Fake.GetDetailsAsync([5, 5], Ct));

        Assert.Equal([5UL], Assert.Single(_fake.DetailRequests));
    }

    [Fact]
    public async Task GetDetailsAsync_OkItemMissingAField_IsNull()
    {
        _handler.RespondJson(
            WorkshopFiles.PublishedFileDetailsUrl,
            """{"response":{"publishedfiledetails":[{"publishedfileid":"5","result":1,"title":"No manifest","time_updated":1,"file_size":"1"}]}}""");

        var details = ResultAssert.Ok(await Api.GetDetailsAsync([5], Ct));

        Assert.Null(details[5]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"response":{"publishedfiledetails":[{"publishedfileid":"abc","result":1}]}}""")]
    [InlineData("""{"response":{}}""")]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task GetDetailsAsync_UnreadableBody_IsResponseInvalid(string body)
    {
        _handler.RespondJson(WorkshopFiles.PublishedFileDetailsUrl, body);

        ResultAssert.Failed(await Api.GetDetailsAsync([5], Ct), ErrorCode.WorkshopResponseInvalid);
    }

    [Fact]
    public async Task GetDetailsAsync_HttpError_IsNetworkFailed()
    {
        _handler.RespondStatus(WorkshopFiles.PublishedFileDetailsUrl, HttpStatusCode.ServiceUnavailable);

        var error = ResultAssert.Failed(await Api.GetDetailsAsync([5], Ct), ErrorCode.WorkshopNetworkFailed);

        Assert.Equal("HTTP 503", error.Details["reason"]);
    }

    [Fact]
    public async Task GetDetailsAsync_ConnectionFails_IsNetworkFailed()
    {
        _handler.Throw(WorkshopFiles.PublishedFileDetailsUrl);

        ResultAssert.Failed(await Api.GetDetailsAsync([5], Ct), ErrorCode.WorkshopNetworkFailed);
    }

    [Fact]
    public async Task GetDetailsAsync_NoAnswerWithinTheTimeout_IsNetworkFailed()
    {
        _handler.Hang(WorkshopFiles.PublishedFileDetailsUrl);
        var api = new WorkshopWebApi(_http, NullLogger<WorkshopWebApi>.Instance) { RequestTimeout = TimeSpan.FromMilliseconds(50) };

        var error = ResultAssert.Failed(await api.GetDetailsAsync([5], Ct), ErrorCode.WorkshopNetworkFailed);

        Assert.Equal("timed out", error.Details["reason"]);
    }

    [Fact]
    public async Task GetDetailsAsync_Cancelled_Throws()
    {
        _handler.Hang(WorkshopFiles.PublishedFileDetailsUrl);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = Api.GetDetailsAsync([5], cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task GetCollectionChildrenAsync_ChildrenInArrayOrderWithNestedFlag_AndNullForResult9()
    {
        _handler.RespondJson(
            WorkshopFiles.CollectionDetailsUrl,
            """
            {"response":{"result":1,"resultcount":0,"collectiondetails":[
              {"publishedfileid":"10","result":1,"children":[
                {"publishedfileid":"3","sortorder":0,"filetype":0},
                {"publishedfileid":"20","sortorder":0,"filetype":2},
                {"publishedfileid":"1","sortorder":1,"filetype":0}]},
              {"publishedfileid":"11","result":9}]}}
            """);

        var collections = ResultAssert.Ok(await Api.GetCollectionChildrenAsync([10, 11], Ct));

        Assert.Equal(
            [new WorkshopCollectionChild(3, false), new WorkshopCollectionChild(20, true), new WorkshopCollectionChild(1, false)],
            collections[10]);
        Assert.Null(collections[11]);
    }

    [Fact]
    public async Task GetCollectionChildrenAsync_SendsCollectionCountForm()
    {
        _fake.AddCollection(10, "Pack", (1, 0));

        ResultAssert.Ok(await Fake.GetCollectionChildrenAsync([10, 11], Ct));

        Assert.Equal([10UL, 11UL], Assert.Single(_fake.CollectionRequests));
    }
}
