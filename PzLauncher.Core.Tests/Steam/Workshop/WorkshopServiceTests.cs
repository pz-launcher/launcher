using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Net;
using PzLauncher.Core.Platform;
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
    private readonly FakeModCollections _collections = new();
    private readonly FakePlatformEnvironment _platform;
    private readonly WorkshopService _service;
    private readonly ModBuilder _mods;
    private readonly string _steamRoot;
    private readonly string _library;
    private readonly string _root;

    public WorkshopServiceTests()
    {
        var steam = new SteamLayoutBuilder(_temp.Path);
        _library = steam.CreateLibrary("Library");
        _steamRoot = steam.CreateSteamRoot(_library);
        _platform = FakePlatformEnvironment.WithRegistrySteam(_steamRoot);
        _platform.HomeDirectory = _temp.CreateDirectory("home");
        SteamLayoutBuilder.WriteRaw(SteamClientFiles.LoginUsersPath(_steamRoot), Fixtures.Read("Steam", "loginusers.vdf"));
        _http = new LauncherHttpClient(_api.Handler, V("0.2.0"));
        _service = new WorkshopService(
            new SteamLocator(_platform),
            ZombieBuddyServiceTests.CreateWorkshopLibraries(_platform),
            new WorkshopWebApi(_http, NullLogger<WorkshopWebApi>.Instance),
            _collections,
            _time,
            NullLogger<WorkshopService>.Instance);
        _root = new GameInstallBuilder(Path.Combine(_library, "steamapps", "common"), PlatformKind.Windows)
            .CreateFlat("ProjectZomboid", GameInstallBuilder.WindowsGameJson, withJre: false);
        _mods = new ModBuilder(Path.Combine(_platform.HomeDirectory, "Zomboid"), _library);
    }

    public void Dispose()
    {
        _http.Dispose();
        _temp.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string SubscriptionsPath
    {
        get
        {
            var account = ResultAssert.Ok(SteamLoginUsers.ParseCurrent(Fixtures.Read("Steam", "loginusers.vdf"), "p"));
            return WorkshopFiles.SubscriptionsPath(_steamRoot, account.AccountId);
        }
    }

    private void Subscribe(params (ulong Id, bool Disabled)[] items)
    {
        var entries = string.Concat(items.Select((item, i) =>
            $$"""
                "{{i}}" { "publishedfileid" "{{item.Id}}" "time_subscribed" "{{1000 + i}}" "disabled_locally" "{{(item.Disabled ? 1 : 0)}}" }

            """));
        SteamLayoutBuilder.WriteRaw(SubscriptionsPath, $"\"subscribedfiles\"\n{{\n\t\"appid\"\t\t\"108600\"\n{entries}}}\n");
    }

    private void Install(params (ulong Id, string Manifest, string Latest)[] items)
    {
        var installed = string.Concat(items.Select(item => $$"""    "{{item.Id}}" { "size" "42" "timeupdated" "500" "manifest" "{{item.Manifest}}" }""" + "\n"));
        var details = string.Concat(items.Select(item => $$"""    "{{item.Id}}" { "manifest" "{{item.Manifest}}" "latest_manifest" "{{item.Latest}}" }""" + "\n"));
        SteamLayoutBuilder.WriteRaw(
            WorkshopFiles.ManifestPath(_library),
            $"\"AppWorkshop\"\n{{\n\"WorkshopItemsInstalled\"\n{{\n{installed}}}\n\"WorkshopItemDetails\"\n{{\n{details}}}\n}}\n");
        foreach (var item in items)
        {
            Directory.CreateDirectory(WorkshopLibraries.ItemFolder(_library, item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    private async Task<WorkshopSubscriptions> GetAsync() => ResultAssert.Ok(await _service.GetSubscriptionsAsync(_root, "", Ct));

    [Fact]
    public async Task GetSubscriptionsAsync_JoinsSubscriptionsInstallsModsAndCollections()
    {
        Subscribe((100, false), (200, false), (300, true));
        Install((100, "11", "11"), (300, "33", "33"));
        _mods.Steam("100", "ModA");
        _mods.Steam("100", "ModB", ModBuilder.ModInfo("ModB", name: "Mod B", incompatible: "ModA"));
        _collections.Collections = [new ModCollection("pack", ["ModB", "Other"], false, false), new ModCollection("other", ["Other"], false, false)];

        var subscriptions = await GetAsync();

        Assert.Empty(subscriptions.Warnings);
        Assert.Equal([100UL, 200UL, 300UL], subscriptions.Items.Select(item => item.Id));
        var first = subscriptions.Items[0];
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1000), first.Subscribed);
        Assert.Equal(
            new WorkshopInstall(WorkshopLibraries.ItemFolder(_library, "100"), "11", DateTimeOffset.FromUnixTimeSeconds(500), 42),
            first.Installed);
        Assert.Null(first.Latest);
        Assert.Equal(["ModA", "ModB"], first.Mods.Select(mod => mod.Id));
        Assert.Equal(("Mod B", "100", InstalledModSource.SteamWorkshop), (first.Mods[1].Name, first.Mods[1].WorkshopId, first.Mods[1].Source));
        Assert.Equal(["ModA"], first.Mods[1].Incompatible);
        Assert.Equal(["pack"], first.Collections);
        Assert.Equal(
            [WorkshopItemState.UpToDate, WorkshopItemState.NotInstalled, WorkshopItemState.DisabledLocally],
            subscriptions.Items.Select(item => item.State));
        Assert.Empty(subscriptions.Items[1].Mods);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ModIdSharedWithAnotherItem_IsListedForBoth()
    {
        Subscribe((100, false), (200, false));
        Install((100, "1", "1"), (200, "2", "2"));
        _mods.Steam("100", "Shared");
        _mods.Steam("200", "Shared");

        var subscriptions = await GetAsync();

        Assert.All(subscriptions.Items, item => Assert.Equal(["Shared"], item.Mods.Select(mod => mod.Id)));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_InstallListedButFolderGone_IsNotInstalled()
    {
        Subscribe((100, false));
        Install((100, "1", "1"));
        Directory.Delete(WorkshopLibraries.ItemFolder(_library, "100"));

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
    public async Task GetSubscriptionsAsync_CollectionsFail_WarnsAndListsItems()
    {
        Subscribe((100, false));
        _collections.Error = ModCollectionErrors.CacheFolderUnresolved("p");

        var subscriptions = await GetAsync();

        Assert.Single(subscriptions.Items);
        Assert.Equal(ErrorCode.ModCollectionCacheFolderUnresolved, Assert.Single(subscriptions.Warnings).Code);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_UnreadableInstallList_Warns()
    {
        Subscribe((100, false));
        SteamLayoutBuilder.WriteRaw(WorkshopFiles.ManifestPath(_library), "\"AppWorkshop\"\n{\n");

        var subscriptions = await GetAsync();

        Assert.Equal(WorkshopItemState.NotInstalled, Assert.Single(subscriptions.Items).State);
        Assert.Equal(ErrorCode.WorkshopManifestUnreadable, Assert.Single(subscriptions.Warnings).Code);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_MalformedSubscriptionFile_Fails()
    {
        SteamLayoutBuilder.WriteRaw(SubscriptionsPath, "\"subscribedfiles\"\n{\n");

        ResultAssert.Failed(await _service.GetSubscriptionsAsync(_root, "", Ct), ErrorCode.WorkshopSubscriptionsUnreadable);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_NoSteam_IsSteamNotInstalled()
    {
        _platform.SteamRegistryPath = null;

        ResultAssert.Failed(await _service.GetSubscriptionsAsync(_root, "", Ct), ErrorCode.SteamNotInstalled);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_NoLoginUsers_IsNoCurrentAccount()
    {
        File.Delete(SteamClientFiles.LoginUsersPath(_steamRoot));

        ResultAssert.Failed(await _service.GetSubscriptionsAsync(_root, "", Ct), ErrorCode.SteamNoCurrentAccount);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_RelativeInstallRoot_IsInvalidPath() =>
        ResultAssert.Failed(await _service.GetSubscriptionsAsync("relative", "", Ct), ErrorCode.InstallInvalidPath);

    [Fact]
    public async Task GetCurrentAccountAsync_ReadsTheSignedInAccount()
    {
        var expected = ResultAssert.Ok(SteamLoginUsers.ParseCurrent(Fixtures.Read("Steam", "loginusers.vdf"), "p"));

        Assert.Equal(expected, ResultAssert.Ok(await _service.GetCurrentAccountAsync(Ct)));
    }

    [Fact]
    public async Task GetCurrentAccountAsync_NoSteam_IsSteamNotInstalled()
    {
        _platform.SteamRegistryPath = null;

        ResultAssert.Failed(await _service.GetCurrentAccountAsync(Ct), ErrorCode.SteamNotInstalled);
    }

    [Fact]
    public async Task GetCurrentAccountAsync_NoLoginUsers_IsNoCurrentAccount()
    {
        File.Delete(SteamClientFiles.LoginUsersPath(_steamRoot));

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

    private sealed class FakeModCollections : IModCollectionService
    {
        public IReadOnlyList<ModCollection> Collections { get; set; } = [];

        public Error? Error { get; set; }

        public Task<Result<ModCollectionState>> GetAsync(string installRoot, string extraGameArgs, CancellationToken ct = default) =>
            Task.FromResult(Error is { } error
                ? Result<ModCollectionState>.Fail(error)
                : Result<ModCollectionState>.Ok(new ModCollectionState("cache", Collections, [], [], true)));

        public Task<Result> SaveAsync(SaveModCollectionRequest request, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<Result> DeleteAsync(string installRoot, string extraGameArgs, string name, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<Result<ApplyModCollectionResult>> ApplyAsync(string installRoot, string extraGameArgs, string name, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
