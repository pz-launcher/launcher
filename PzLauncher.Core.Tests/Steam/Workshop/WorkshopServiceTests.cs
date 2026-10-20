using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Net;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Sources;
using PzLauncher.Core.Tests.Mod.ZombieBuddy;
using PzLauncher.Core.Tests.TestSupport;
using static PzLauncher.Core.Tests.TestSupport.UpdateTestKit;

namespace PzLauncher.Core.Tests.Steam.Workshop;

public sealed class WorkshopServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeWorkshopApi _api = new();
    private readonly LauncherHttpClient _http;
    private readonly ManualTimeProvider _time = new();
    private readonly WorkshopTestLayout _layout;
    private readonly WorkshopService _service;

    public WorkshopServiceTests()
    {
        _layout = new WorkshopTestLayout(_temp);
        _http = new LauncherHttpClient(_api.Handler, V("0.2.0"));
        _service = Create(_layout, _http, _time);
    }

    public void Dispose()
    {
        _http.Dispose();
        _temp.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Root => _layout.Root;

    internal static WorkshopService Create(WorkshopTestLayout layout, LauncherHttpClient http, TimeProvider time) =>
        new(
            new SteamLocator(layout.Platform),
            ZombieBuddyServiceTests.CreateWorkshopLibraries(layout.Platform),
            new WorkshopWebApi(http, NullLogger<WorkshopWebApi>.Instance),
            time,
            NullLogger<WorkshopService>.Instance);

    private void Subscribe(params (ulong Id, bool Disabled)[] items) => _layout.Subscribe(items);

    private void Install(params (ulong Id, string Manifest, string Latest)[] items) => _layout.Install(items);

    private async Task<WorkshopSubscriptions> GetAsync() => ResultAssert.Ok(await _service.GetSubscriptionsAsync(Root, Ct));

    [Fact]
    public async Task GetSubscriptionsAsync_JoinsSubscriptionsAndInstalls()
    {
        Subscribe((100, false), (200, false), (300, true));
        Install((100, "11", "11"), (300, "33", "33"));

        var subscriptions = await GetAsync();

        Assert.Empty(subscriptions.Warnings);
        Assert.Equal([100UL, 200UL, 300UL], subscriptions.Items.Select(item => item.Id));
        var first = subscriptions.Items[0];
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1000), first.Subscribed);
        Assert.Equal(new WorkshopInstall(_layout.ItemFolder(100), "11", DateTimeOffset.FromUnixTimeSeconds(500), 42), first.Installed);
        Assert.Null(first.Latest);
        Assert.Equal(
            [WorkshopItemState.UpToDate, WorkshopItemState.NotInstalled, WorkshopItemState.DisabledLocally],
            subscriptions.Items.Select(item => item.State));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_InstallListedButFolderGone_IsNotInstalled()
    {
        Subscribe((100, false));
        Install((100, "1", "1"));
        Directory.Delete(_layout.ItemFolder(100));

        var item = Assert.Single((await GetAsync()).Items);

        Assert.Equal(WorkshopItemState.NotInstalled, item.State);
        Assert.Null(item.Installed);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_BeforeACheck_UsesSteamsLatestManifest()
    {
        Subscribe((100, false));
        Install((100, "1", "2"));

        Assert.Equal(WorkshopItemState.NeedsUpdate, Assert.Single((await GetAsync()).Items).State);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_AfterACheck_ComparesWithTheWebApi()
    {
        Subscribe((100, false), (200, false));
        Install((100, "1", "1"), (200, "2", "3"));
        _api.AddItem(100, "Hundred", manifest: "9");
        _api.AddItem(200, "Two hundred", manifest: "2");

        ResultAssert.Ok(await _service.CheckUpdatesAsync([100, 200], Ct));
        var items = (await GetAsync()).Items;

        Assert.Equal([WorkshopItemState.NeedsUpdate, WorkshopItemState.UpToDate], items.Select(item => item.State));
        Assert.Equal(["Hundred", "Two hundred"], items.Select(item => item.Latest?.Title));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_NoSubscriptionFile_IsEmpty() =>
        Assert.Empty((await GetAsync()).Items);

    [Fact]
    public async Task GetSubscriptionsAsync_UnreadableInstallList_Warns()
    {
        Subscribe((100, false));
        SteamLayoutBuilder.WriteRaw(WorkshopFiles.ManifestPath(_layout.Library), "\"AppWorkshop\"\n{\n");

        var subscriptions = await GetAsync();

        Assert.Equal(WorkshopItemState.NotInstalled, Assert.Single(subscriptions.Items).State);
        Assert.Equal(ErrorCode.WorkshopManifestUnreadable, Assert.Single(subscriptions.Warnings).Code);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_MalformedSubscriptionFile_Fails()
    {
        SteamLayoutBuilder.WriteRaw(_layout.SubscriptionsPath, "\"subscribedfiles\"\n{\n");

        ResultAssert.Failed(await _service.GetSubscriptionsAsync(Root, Ct), ErrorCode.WorkshopSubscriptionsUnreadable);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_NoSteam_IsSteamNotInstalled()
    {
        _layout.Platform.SteamRegistryPath = null;

        ResultAssert.Failed(await _service.GetSubscriptionsAsync(Root, Ct), ErrorCode.SteamNotInstalled);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_NoLoginUsers_IsNoCurrentAccount()
    {
        File.Delete(SteamClientFiles.LoginUsersPath(_layout.SteamRoot));

        ResultAssert.Failed(await _service.GetSubscriptionsAsync(Root, Ct), ErrorCode.SteamNoCurrentAccount);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_RelativeInstallRoot_IsInvalidPath() =>
        ResultAssert.Failed(await _service.GetSubscriptionsAsync("relative", Ct), ErrorCode.InstallInvalidPath);

    [Fact]
    public async Task GetCurrentAccountAsync_ReadsTheSignedInAccount()
    {
        var expected = ResultAssert.Ok(SteamLoginUsers.ParseCurrent(Fixtures.Read("Steam", "loginusers.vdf"), "p"));

        Assert.Equal(expected, ResultAssert.Ok(await _service.GetCurrentAccountAsync(Ct)));
    }

    [Fact]
    public async Task GetCurrentAccountAsync_NoSteam_IsSteamNotInstalled()
    {
        _layout.Platform.SteamRegistryPath = null;

        ResultAssert.Failed(await _service.GetCurrentAccountAsync(Ct), ErrorCode.SteamNotInstalled);
    }

    [Fact]
    public async Task GetCurrentAccountAsync_NoLoginUsers_IsNoCurrentAccount()
    {
        File.Delete(SteamClientFiles.LoginUsersPath(_layout.SteamRoot));

        ResultAssert.Failed(await _service.GetCurrentAccountAsync(Ct), ErrorCode.SteamNoCurrentAccount);
    }

    [Fact]
    public async Task CheckUpdatesAsync_ReturnsShownItemsInRequestOrder()
    {
        _api.AddItem(2, "Two", manifest: "22", updated: 50, size: 7);
        _api.AddItem(1, "One");

        var details = ResultAssert.Ok(await _service.CheckUpdatesAsync([2, 3, 1, 2], Ct));

        Assert.Equal([2UL, 1UL], details.Select(item => item.Id));
        Assert.Equal(new WorkshopItemDetails(2, "Two", "22", DateTimeOffset.FromUnixTimeSeconds(50), 7), details[0]);
    }

    [Fact]
    public async Task CheckUpdatesAsync_WithinTheCacheLifetime_ReusesAnswers()
    {
        _api.AddItem(1, "One");
        _api.AddItem(2, "Two");

        ResultAssert.Ok(await _service.CheckUpdatesAsync([1], Ct));
        _time.Advance(WorkshopService.CacheLifetime - TimeSpan.FromSeconds(1));
        ResultAssert.Ok(await _service.CheckUpdatesAsync([1, 2], Ct));
        _time.Advance(TimeSpan.FromSeconds(1));
        ResultAssert.Ok(await _service.CheckUpdatesAsync([1, 2], Ct));

        Assert.Equal<IReadOnlyList<ulong>>([[1], [2], [1]], _api.DetailRequests);
    }

    [Fact]
    public async Task CheckUpdatesAsync_NetworkFails_IsNetworkFailed()
    {
        _api.Handler.Throw(WorkshopFiles.PublishedFileDetailsUrl);

        ResultAssert.Failed(await _service.CheckUpdatesAsync([1], Ct), ErrorCode.WorkshopNetworkFailed);
    }

    [Fact]
    public async Task GetCollectionAsync_NestedCollectionsWithACycle_ExpandDepthFirstWithEachItemOnce()
    {
        _api.AddCollection(10, "Pack", (1, 0), (20, 2), (2, 0), (30, 2));
        _api.AddCollection(20, "Nested", (3, 0), (10, 2), (1, 0), (40, 2), (4, 0));
        _api.AddCollection(40, "Deep", (5, 0));
        _api.AddCollection(30, "Second", (6, 0), (20, 2));
        foreach (var id in new ulong[] { 1, 2, 3, 4, 5 })
        {
            _api.AddItem(id, $"Item {id}");
        }

        var collection = ResultAssert.Ok(await _service.GetCollectionAsync(10, Ct));

        Assert.Equal((10UL, "Pack"), (collection.Id, collection.Title));
        Assert.Equal([1UL, 3UL, 5UL, 4UL, 2UL, 6UL], collection.Items.Select(item => item.Id));
        Assert.Equal("Item 5", collection.Items[2].Details?.Title);
        Assert.Null(collection.Items[5].Details);
        Assert.Equal<IReadOnlyList<ulong>>([[10], [20, 30], [40]], _api.CollectionRequests);
    }

    [Fact]
    public async Task GetCollectionAsync_HiddenNestedCollection_IsSkipped()
    {
        _api.AddCollection(10, "Pack", (1, 0), (99, 2), (2, 0));

        var collection = ResultAssert.Ok(await _service.GetCollectionAsync(10, Ct));

        Assert.Equal([1UL, 2UL], collection.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task GetCollectionAsync_FetchesDetailsIntoTheSessionCache()
    {
        Subscribe((1, false));
        Install((1, "5", "5"));
        _api.AddCollection(10, "Pack", (1, 0));
        _api.AddItem(1, "One", manifest: "6");

        ResultAssert.Ok(await _service.GetCollectionAsync(10, Ct));
        var item = Assert.Single((await GetAsync()).Items);

        Assert.Equal(("One", WorkshopItemState.NeedsUpdate), (item.Latest?.Title, item.State));
    }

    [Fact]
    public async Task GetCollectionAsync_NotACollection_IsCollectionNotFound()
    {
        _api.AddItem(5, "Plain item");

        var error = ResultAssert.Failed(await _service.GetCollectionAsync(5, Ct), ErrorCode.WorkshopCollectionNotFound);

        Assert.Equal("5", error.Details["id"]);
    }

    [Fact]
    public async Task GetCollectionAsync_NetworkFails_IsNetworkFailed()
    {
        _api.Handler.Throw(WorkshopFiles.CollectionDetailsUrl);

        ResultAssert.Failed(await _service.GetCollectionAsync(10, Ct), ErrorCode.WorkshopNetworkFailed);
    }

    [Fact]
    public async Task ClassifyAsync_AsksOnceForAllIds_AndTitlesTheCollections()
    {
        _api.AddCollection(10, "Pack", (1, 0));
        _api.AddItem(1, "One");

        var kinds = ResultAssert.Ok(await _service.ClassifyAsync([10, 1, 99, 10], Ct));

        Assert.Equal([new WorkshopIdKind(10, true, "Pack"), new WorkshopIdKind(1, false, null), new WorkshopIdKind(99, false, null)], kinds);
        Assert.Equal<IReadOnlyList<ulong>>([[10, 1, 99]], _api.CollectionRequests);
        Assert.Equal<IReadOnlyList<ulong>>([[10]], _api.DetailRequests);
    }

    [Fact]
    public async Task ClassifyAsync_NoCollection_FetchesNoDetails()
    {
        _api.AddItem(1, "One");

        ResultAssert.Ok(await _service.ClassifyAsync([1], Ct));

        Assert.Empty(_api.DetailRequests);
    }

    [Fact]
    public async Task ClassifyAsync_NetworkFails_IsNetworkFailed()
    {
        _api.Handler.Throw(WorkshopFiles.CollectionDetailsUrl);

        ResultAssert.Failed(await _service.ClassifyAsync([1], Ct), ErrorCode.WorkshopNetworkFailed);
    }
}
