using PzLauncher.Core.Common;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Services.Mods;

public class WorkshopLaunchCheckTests
{
    private const string GameRoot = @"E:\SteamLibrary\steamapps\common\ProjectZomboid";
    private const string GameArgs = @"-cachedir=D:\pz";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly FakeModCollectionService _collections = new();
    private readonly FakeWorkshopService _workshop = new();
    private readonly ManualTimeProvider _time = new();

    private WorkshopLaunchCheck CreateCheck() => TestWorkshop.LaunchCheck(_workshop, _collections, _time);

    private Task<IReadOnlyList<WorkshopItem>> FindAsync(string? collection) => CreateCheck().FindOutdatedAsync(GameRoot, GameArgs, collection);

    private static IEnumerable<ulong> Ids(IEnumerable<WorkshopItem> items) => items.Select(item => item.Id);

    // Items 1 and 2 hold the collection's mods, item 3 the enabled list's; 1 and 3 have a newer version.
    private void ArrangeCollectionAndEnabledList()
    {
        _collections
            .WithMods(TestMods.Mod("ModA", workshopId: "1"), TestMods.Mod("ModB", workshopId: "2"), TestMods.Mod("ModC", workshopId: "3"))
            .WithCollection("Pack", "ModA", "ModB");
        _collections.ActiveIds = ["ModC"];
        _workshop
            .WithItem(TestWorkshop.Item(1, mods: ["ModA"]))
            .WithItem(TestWorkshop.Item(2, mods: ["ModB"]))
            .WithItem(TestWorkshop.Item(3, mods: ["ModC"]))
            .WithPublished(1, "Alpha", "200")
            .WithPublished(2, "Bravo", "100")
            .WithPublished(3, "Charlie", "200");
    }

    [Fact]
    public async Task CurrentCollection_ChecksTheItemsBehindItsMods()
    {
        ArrangeCollectionAndEnabledList();

        var outdated = await FindAsync("Pack");

        Assert.Equal([1ul], Ids(outdated));
        Assert.Equal([[1ul, 2ul]], _workshop.CheckRequests);
        Assert.Equal([(GameRoot, GameArgs)], _collections.GetRequests);
        Assert.Equal([(GameRoot, GameArgs)], _workshop.GetRequests);
    }

    [Fact]
    public async Task OutdatedItem_CarriesTheLatestVersion()
    {
        ArrangeCollectionAndEnabledList();

        var item = Assert.Single(await FindAsync("Pack"));

        Assert.Equal(("Alpha", "200"), (item.Latest?.Title, item.Latest?.Manifest));
        Assert.Equal(WorkshopItemState.NeedsUpdate, item.State);
    }

    [Fact]
    public async Task NoCurrentCollection_ChecksTheEnabledList()
    {
        ArrangeCollectionAndEnabledList();

        var outdated = await FindAsync(null);

        Assert.Equal([3ul], Ids(outdated));
        Assert.Equal([[3ul]], _workshop.CheckRequests);
    }

    [Fact]
    public async Task CurrentCollectionGone_ChecksTheEnabledList()
    {
        ArrangeCollectionAndEnabledList();

        var outdated = await FindAsync("Deleted in-game");

        Assert.Equal([3ul], Ids(outdated));
    }

    [Fact]
    public async Task CurrentCollection_IncludesTheRequirementsItEnables()
    {
        _collections
            .WithMods(TestMods.Mod("ModA", workshopId: "1", require: ["Library"]), TestMods.Mod("Library", workshopId: "4"))
            .WithCollection("Pack", "ModA");
        _workshop
            .WithItem(TestWorkshop.Item(1, mods: ["ModA"]))
            .WithItem(TestWorkshop.Item(4, mods: ["Library"]))
            .WithPublished(1, "Alpha", "100")
            .WithPublished(4, "Library", "200");

        var outdated = await FindAsync("Pack");

        Assert.Equal([4ul], Ids(outdated));
    }

    [Fact]
    public async Task ItemWithSeveralMods_CountsWhenOneOfThemIsEnabled()
    {
        _collections.WithMods(TestMods.Mod("Cars", workshopId: "5"), TestMods.Mod("CarsLite", workshopId: "5")).ActiveIds = ["CarsLite"];
        _workshop.WithItem(TestWorkshop.Item(5, mods: ["Cars", "CarsLite"])).WithPublished(5, "Vehicles", "200");

        var outdated = await FindAsync(null);

        Assert.Equal([5ul], Ids(outdated));
    }

    [Fact]
    public async Task ItemsNotInstalledOrDisabledLocally_AreNotChecked()
    {
        _collections.WithMods(TestMods.Mod("ModA", workshopId: "1"), TestMods.Mod("ModB", workshopId: "2")).ActiveIds = ["ModA", "ModB"];
        _workshop
            .WithItem(TestWorkshop.Item(1, manifest: null, mods: ["ModA"]))
            .WithItem(TestWorkshop.Item(2, mods: ["ModB"], state: WorkshopItemState.DisabledLocally))
            .WithPublished(1, "Alpha", "200")
            .WithPublished(2, "Bravo", "200");

        var outdated = await FindAsync(null);

        Assert.Empty(outdated);
        Assert.Empty(_workshop.CheckRequests);
    }

    [Fact]
    public async Task NoEnabledModFromTheWorkshop_DoesNotGoOnline()
    {
        _collections.WithMods(TestMods.Mod("Local", source: InstalledModSource.Local)).ActiveIds = ["Local"];
        _workshop.WithItem(TestWorkshop.Item(1, mods: ["ModA"])).WithPublished(1, "Alpha", "200");

        var outdated = await FindAsync(null);

        Assert.Empty(outdated);
        Assert.Empty(_workshop.CheckRequests);
    }

    [Fact]
    public async Task NothingEnabled_ReadsNoSubscriptions()
    {
        ArrangeCollectionAndEnabledList();
        _collections.ActiveIds = [];

        var outdated = await FindAsync(null);

        Assert.Empty(outdated);
        Assert.Empty(_workshop.GetRequests);
    }

    [Fact]
    public async Task EverythingUpToDate_ReturnsNothing()
    {
        ArrangeCollectionAndEnabledList();
        _workshop.WithPublished(1, "Alpha", "100");

        var outdated = await FindAsync("Pack");

        Assert.Empty(outdated);
        Assert.Single(_workshop.CheckRequests);
    }

    [Theory]
    [InlineData(WorkshopItemState.NeedsUpdate, true)]
    [InlineData(WorkshopItemState.UpToDate, false)]
    public async Task ItemTheWebApiDoesNotShow_KeepsWhatSteamKnows(WorkshopItemState state, bool outdated)
    {
        _collections.WithMods(TestMods.Mod("ModA", workshopId: "1")).ActiveIds = ["ModA"];
        _workshop.WithItem(TestWorkshop.Item(1, mods: ["ModA"], state: state));

        var found = await FindAsync(null);

        ulong[] expected = outdated ? [1] : [];
        Assert.Equal(expected, Ids(found));
    }

    [Fact]
    public async Task CollectionsUnreadable_ReturnsNothing()
    {
        ArrangeCollectionAndEnabledList();
        _collections.GetError = () => TestErrors.Create(ErrorCode.ModCollectionCacheFolderUnresolved, GameRoot);

        Assert.Empty(await FindAsync("Pack"));
        Assert.Empty(_workshop.GetRequests);
    }

    [Fact]
    public async Task SubscriptionsUnreadable_ReturnsNothing()
    {
        ArrangeCollectionAndEnabledList();
        _workshop.GetError = () => TestErrors.Create(ErrorCode.WorkshopSubscriptionsUnreadable, @"C:\Steam\userdata\1\ugc\108600_subscriptions.vdf");

        Assert.Empty(await FindAsync("Pack"));
        Assert.Empty(_workshop.CheckRequests);
    }

    [Fact]
    public async Task WebApiFails_ReturnsNothing()
    {
        ArrangeCollectionAndEnabledList();
        _workshop.CheckError = () => TestErrors.Create(ErrorCode.WorkshopNetworkFailed);

        Assert.Empty(await FindAsync("Pack"));
    }

    [Fact]
    public async Task WebApiTooSlow_ReturnsNothingAfterTheTimeout()
    {
        ArrangeCollectionAndEnabledList();
        _workshop.CheckGate = new TaskCompletionSource();

        var find = FindAsync("Pack");
        _time.Advance(Timeout - TimeSpan.FromSeconds(1));

        Assert.False(find.IsCompleted);

        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Empty(await find);
    }
}
