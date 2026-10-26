using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Mod.Workshop;
using PzLauncher.Core.Net;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Tests.Mod.ZombieBuddy;
using PzLauncher.Core.Tests.Steam.Workshop;
using PzLauncher.Core.Tests.TestSupport;
using static PzLauncher.Core.Tests.TestSupport.UpdateTestKit;

namespace PzLauncher.Core.Tests.Mod.Workshop;

public sealed class WorkshopModsServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeWorkshopApi _api = new();
    private readonly LauncherHttpClient _http;
    private readonly WorkshopTestLayout _layout;
    private readonly WorkshopModsService _service;

    public WorkshopModsServiceTests()
    {
        _layout = new WorkshopTestLayout(_temp);
        _http = new LauncherHttpClient(_api.Handler, V("0.2.0"));
        var collections = new ModCollectionService(
            ZombieBuddyServiceTests.CreateWorkshopLibraries(_layout.Platform), _layout.Platform, NullLogger<ModCollectionService>.Instance);
        _service = new WorkshopModsService(
            WorkshopServiceTests.Create(_layout, _http, new ManualTimeProvider()), collections, NullLogger<WorkshopModsService>.Instance);
    }

    public void Dispose()
    {
        _http.Dispose();
        _temp.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ModBuilder Mods => _layout.Mods;

    private async Task<WorkshopModSubscriptions> GetAsync() => ResultAssert.Ok(await _service.GetSubscriptionsAsync(_layout.Root, "", Ct));

    private async Task<IEnumerable<ulong>> FindOutdatedAsync(string? collection) =>
        ResultAssert.Ok(await _service.FindOutdatedForLaunchAsync(_layout.Root, "", collection, Ct)).Select(item => item.Id);

    // The cache folder no longer resolves, so the mod collections can't be read.
    private void BreakCollections() => _layout.Platform.HomeDirectory = "relative";

    [Fact]
    public async Task GetSubscriptionsAsync_JoinsEachItemWithItsModsAndTheCollectionsUsingThem()
    {
        _layout.Subscribe((100, false), (200, false), (300, true));
        _layout.Install((100, "11", "11"), (300, "33", "33"));
        Mods.Steam("100", "ModA");
        Mods.Steam("100", "ModB", ModBuilder.ModInfo("ModB", name: "Mod B", incompatible: "ModA"));
        _layout.WriteCollections("pack:ModB;Other;", "other:Other;");

        var subscriptions = await GetAsync();

        Assert.Empty(subscriptions.Warnings);
        Assert.Equal([100UL, 200UL, 300UL], subscriptions.Items.Select(item => item.Item.Id));
        var first = subscriptions.Items[0];
        Assert.Equal(WorkshopItemState.UpToDate, first.Item.State);
        Assert.Equal(["ModA", "ModB"], first.Mods.Select(mod => mod.Id));
        Assert.Equal(("Mod B", (ulong?)100, InstalledModSource.SteamWorkshop), (first.Mods[1].Name, first.Mods[1].WorkshopId, first.Mods[1].Source));
        Assert.Equal(["ModA"], first.Mods[1].Incompatible);
        Assert.Equal(["pack"], first.Collections);
        Assert.Empty(subscriptions.Items[1].Mods);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ItemWhoseOnlyModTheGameLoadsElsewhere_StillListsIt()
    {
        _layout.Subscribe((100, false), (200, false));
        _layout.Install((100, "1", "1"), (200, "2", "2"));
        Mods.Steam("100", "Shared");
        Mods.Steam("200", "Shared");

        var subscriptions = await GetAsync();

        Assert.All(subscriptions.Items, item => Assert.Equal(["Shared"], item.Mods.Select(mod => mod.Id)));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ItemWithOwnMods_AlsoListsTheOnesTheGameLoadsElsewhere()
    {
        _layout.Subscribe((100, false), (200, false));
        _layout.Install((100, "1", "1"), (200, "2", "2"));
        Mods.Steam("100", "Shared");
        Mods.Steam("200", "Shared");
        Mods.Steam("200", "Own");

        var subscriptions = await GetAsync();

        Assert.Equal(["Own", "Shared"], subscriptions.Items[1].Mods.Select(mod => mod.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_CollectionsUnreadable_WarnsAndReadsEachItemsMods()
    {
        _layout.Subscribe((100, false));
        _layout.Install((100, "1", "1"));
        Mods.Steam("100", "ModA");
        BreakCollections();

        var subscriptions = await GetAsync();

        var item = Assert.Single(subscriptions.Items);
        Assert.Equal(["ModA"], item.Mods.Select(mod => mod.Id));
        Assert.Empty(item.Collections);
        Assert.Equal(ErrorCode.ModCollectionCacheFolderUnresolved, Assert.Single(subscriptions.Warnings).Code);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_SubscriptionsUnreadable_Fails()
    {
        _layout.Platform.SteamRegistryPath = null;

        ResultAssert.Failed(await _service.GetSubscriptionsAsync(_layout.Root, "", Ct), ErrorCode.SteamNotInstalled);
    }

    // Items 1 and 2 hold the collection's mods, item 3 the enabled list's; 1 and 3 have a newer version.
    private void ArrangeCollectionAndEnabledList()
    {
        _layout.Subscribe((1, false), (2, false), (3, false));
        _layout.Install((1, "100", "100"), (2, "100", "100"), (3, "100", "100"));
        Mods.Steam("1", "ModA");
        Mods.Steam("2", "ModB");
        Mods.Steam("3", "ModC");
        _layout.WriteCollections("Pack:ModA;ModB;");
        _layout.WriteActive("ModC");
        _api.AddItem(1, "Alpha", manifest: "200");
        _api.AddItem(2, "Bravo", manifest: "100");
        _api.AddItem(3, "Charlie", manifest: "200");
    }

    [Fact]
    public async Task FindOutdatedForLaunchAsync_CurrentCollection_ChecksTheItemsBehindItsMods()
    {
        ArrangeCollectionAndEnabledList();

        Assert.Equal([1UL], await FindOutdatedAsync("Pack"));
        Assert.Equal<IReadOnlyList<ulong>>([[1, 2]], _api.DetailRequests);
    }

    [Fact]
    public async Task FindOutdatedForLaunchAsync_OutdatedItem_CarriesTheLatestVersion()
    {
        ArrangeCollectionAndEnabledList();

        var item = Assert.Single(ResultAssert.Ok(await _service.FindOutdatedForLaunchAsync(_layout.Root, "", "Pack", Ct)));

        Assert.Equal(("Alpha", "200", WorkshopItemState.NeedsUpdate), (item.Latest?.Title, item.Latest?.Manifest, item.State));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Deleted in-game")]
    public async Task FindOutdatedForLaunchAsync_NoCurrentCollection_ChecksTheEnabledList(string? collection)
    {
        ArrangeCollectionAndEnabledList();

        Assert.Equal([3UL], await FindOutdatedAsync(collection));
        Assert.Equal<IReadOnlyList<ulong>>([[3]], _api.DetailRequests);
    }

    [Fact]
    public async Task FindOutdatedForLaunchAsync_CurrentCollection_IncludesTheRequirementsItEnables()
    {
        _layout.Subscribe((1, false), (4, false));
        _layout.Install((1, "100", "100"), (4, "100", "100"));
        Mods.Steam("1", "ModA", ModBuilder.ModInfo("ModA", require: "Library"));
        Mods.Steam("4", "Library");
        _layout.WriteCollections("Pack:ModA;");
        _api.AddItem(1, "Alpha", manifest: "100");
        _api.AddItem(4, "Library", manifest: "200");

        Assert.Equal([4UL], await FindOutdatedAsync("Pack"));
    }

    [Fact]
    public async Task FindOutdatedForLaunchAsync_ItemWhoseModTheGameLoadsElsewhere_IsNotChecked()
    {
        _layout.Subscribe((100, false), (200, false));
        _layout.Install((100, "1", "1"), (200, "2", "2"));
        Mods.Steam("100", "Shared");
        Mods.Steam("200", "Shared");
        _layout.WriteActive("Shared");
        _api.AddItem(100, "First", manifest: "1");
        _api.AddItem(200, "Second", manifest: "3");

        Assert.Empty(await FindOutdatedAsync(null));
        Assert.Equal<IReadOnlyList<ulong>>([[100]], _api.DetailRequests);
    }

    [Fact]
    public async Task FindOutdatedForLaunchAsync_ItemsNotInstalledOrDisabledLocally_AreNotChecked()
    {
        _layout.Subscribe((1, false), (2, true));
        _layout.Install((2, "100", "100"));
        Mods.Steam("2", "ModB");
        _layout.WriteActive("ModA", "ModB");
        _api.AddItem(2, "Bravo", manifest: "200");

        Assert.Empty(await FindOutdatedAsync(null));
        Assert.Empty(_api.DetailRequests);
    }

    [Fact]
    public async Task FindOutdatedForLaunchAsync_NoEnabledModFromTheWorkshop_DoesNotGoOnline()
    {
        ArrangeCollectionAndEnabledList();
        Mods.Local("Local");
        _layout.WriteActive("Local");

        Assert.Empty(await FindOutdatedAsync(null));
        Assert.Empty(_api.DetailRequests);
    }

    [Fact]
    public async Task FindOutdatedForLaunchAsync_NothingEnabled_DoesNotGoOnline()
    {
        ArrangeCollectionAndEnabledList();
        _layout.WriteActive();

        Assert.Empty(await FindOutdatedAsync(null));
        Assert.Empty(_api.DetailRequests);
    }

    [Theory]
    [InlineData("2", true)]
    [InlineData("1", false)]
    public async Task FindOutdatedForLaunchAsync_ItemTheWebApiDoesNotShow_KeepsWhatSteamKnows(string steamsLatest, bool outdated)
    {
        _layout.Subscribe((1, false));
        _layout.Install((1, "1", steamsLatest));
        Mods.Steam("1", "ModA");
        _layout.WriteActive("ModA");

        ulong[] expected = outdated ? [1] : [];
        Assert.Equal(expected, await FindOutdatedAsync(null));
    }

    [Fact]
    public async Task FindOutdatedForLaunchAsync_CollectionsUnreadable_Fails()
    {
        ArrangeCollectionAndEnabledList();
        BreakCollections();

        ResultAssert.Failed(
            await _service.FindOutdatedForLaunchAsync(_layout.Root, "", "Pack", Ct), ErrorCode.ModCollectionCacheFolderUnresolved);
    }

    [Fact]
    public async Task FindOutdatedForLaunchAsync_WebApiFails_Fails()
    {
        ArrangeCollectionAndEnabledList();
        _api.Handler.Throw(WorkshopFiles.PublishedFileDetailsUrl);

        ResultAssert.Failed(await _service.FindOutdatedForLaunchAsync(_layout.Root, "", "Pack", Ct), ErrorCode.WorkshopNetworkFailed);
    }

    [Fact]
    public void InListOrder_KeepsSubscribedItemsAndFillsInMissingTitles()
    {
        var details = new WorkshopItemDetails(1, "One", "5", DateTimeOffset.FromUnixTimeSeconds(5), 7);
        var subscribed = new WorkshopModItem(new WorkshopItem(1, default, false, null, null, WorkshopItemState.NotInstalled), [], ["pack"]);

        var items = WorkshopModItem.InListOrder([new WorkshopCollectionItem(2, null), new WorkshopCollectionItem(1, details)], [subscribed]);

        Assert.Equal([2UL, 1UL], items.Select(item => item.Item.Id));
        Assert.Equal(WorkshopItem.NotSubscribed(2, null), items[0].Item);
        Assert.Equal((details, WorkshopItemState.NotInstalled), (items[1].Item.Latest, items[1].Item.State));
        Assert.Equal(["pack"], items[1].Collections);
    }

    [Fact]
    public void InListOrder_FilledInLatestVersion_DecidesTheState()
    {
        var details = new WorkshopItemDetails(1, "One", "5", DateTimeOffset.FromUnixTimeSeconds(5), 7);
        var installed = new WorkshopInstall("/steam/workshop/content/108600/1", "4", DateTimeOffset.FromUnixTimeSeconds(4), 7);
        var subscribed = new WorkshopModItem(new WorkshopItem(1, default, false, installed, null, WorkshopItemState.UpToDate), [], []);

        var items = WorkshopModItem.InListOrder([new WorkshopCollectionItem(1, details)], [subscribed]);

        Assert.Equal((details, WorkshopItemState.NeedsUpdate), (items[0].Item.Latest, items[0].Item.State));
    }

    [Fact]
    public void WithCollections_TakesTheCollectionsListingEachItemsMods()
    {
        var item = new WorkshopItem(1, default, false, null, null, WorkshopItemState.NotInstalled);
        var mod = new InstalledMod("ModA", "Mod A", 1, InstalledModSource.SteamWorkshop, [], [], [], []);
        var subscriptions = new WorkshopModSubscriptions([new WorkshopModItem(item, [mod], ["old"]), new WorkshopModItem(item, [], ["old"])], []);

        var joined = subscriptions.WithCollections(
            [new ModCollection("pack", ["ModA"], false, false), new ModCollection("other", ["ModB"], false, false), new ModCollection("all", ["ModB", "ModA"], false, false)]);

        Assert.Equal<IEnumerable<string>>([["pack", "all"], []], joined.Items.Select(joinedItem => joinedItem.Collections));
    }
}
