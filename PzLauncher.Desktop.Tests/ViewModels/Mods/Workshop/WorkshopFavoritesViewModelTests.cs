using System.Globalization;
using PzLauncher.Core.Common;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.ViewModels.Mods;
using PzLauncher.Desktop.ViewModels.Mods.Workshop;
using static PzLauncher.Desktop.Tests.TestSupport.TestInstalls;
using static PzLauncher.Desktop.Tests.TestSupport.TestWorkshop;

namespace PzLauncher.Desktop.Tests.ViewModels.Mods.Workshop;

[Collection(UiTextCultureCollection.Name)]
public class WorkshopFavoritesViewModelTests : UiTextCultureTest
{
    private const ulong Pack = 9000;

    private const string PackLink = "https://steamcommunity.com/sharedfiles/filedetails/?id=9000";

    private const string SharedText = "#collections\n9000\n#items\n1001\n1005\n";

    private readonly FakeInstallDetectionService _detection = new();
    private readonly FakeModCollectionService _collections = new();
    private readonly FakeInstallOverridesService _overrides = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeUrlLauncher _urls = new();
    private readonly FakeWorkshopService _service = new();
    private readonly FakeWorkshopActionRunner _runner = new();
    private readonly FakePreferenceStore _preferences = new();
    private readonly GameRunState _runState = new();
    private readonly FakeClipboardService _clipboard = new();
    private WorkshopFavorites _favorites;

    public WorkshopFavoritesViewModelTests()
    {
        Installs = new(_detection, new FakeFolderPicker(), new FakePlatformEnvironment(), Localization);
        _favorites = Favorites(_preferences);
        _service
            .WithItem(Item(1001, mods: ["Core"], collections: ["Survival"]))
            .WithItem(Item(1002, mods: ["Maps", "MapsExtra"], collections: ["Survival", "Driving"]))
            .WithItem(Item(1003, mods: ["Cars"]))
            .WithItem(Item(1004, manifest: null))
            .WithPublished(1001, "Core Library", "100")
            .WithPublished(1002, "Map Pack", "100")
            .WithPublished(1003, "Vehicles", "300")
            .WithPublished(1005, "Weather", "500")
            .WithPublished(Pack, "Survival Pack", "1");
        _service.Collections[Pack] = [1003, 1001, 1005, 1002, 1004, 1006];
    }

    private InstallsViewModel Installs { get; }

    private async Task<ModsViewModel> CreatePageAsync(ModsSection section = ModsSection.WorkshopFavorites)
    {
        _detection.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamGame));
        await Installs.ActivateCommand.ExecuteAsync(null);
        var orderWriter = new ModCollectionOrderWriter(_collections, _runState, new FakePlatformEnvironment(), new ManualTimeProvider());
        var history = History(_preferences);
        var userCollections = UserCollections(_preferences);
        var localCollections = TestWorkshop.LocalCollectionStore();
        var page = new ModsViewModel(
            Installs, _collections, _overrides, new FakeModCollectionDefaultsService(), new ModCollectionApplier(_collections, _dialogs), _dialogs,
            _clipboard, _runState, orderWriter, _service, _service, _runner, _urls, history, userCollections, _favorites,
            new WorkshopTitles(history, userCollections, _favorites, localCollections), localCollections, TestWorkshop.ModpackStoreOf(localCollections), Localization)
        {
            Section = section,
        };
        await page.ActivateCommand.ExecuteAsync(null);
        return page;
    }

    private async Task<WorkshopFavoritesViewModel> CreateAsync() => (await CreatePageAsync()).WorkshopFavorites;

    private static WorkshopItemRow Row(WorkshopFavoritesViewModel vm, ulong id) => vm.Rows.Single(row => row.Id == id);

    private static string Imported(int collections, int items, int skipped) =>
        string.Format(CultureInfo.CurrentCulture, UiText.WorkshopFavorites_Imported, collections, items, skipped);

    // Starred collections

    [Fact]
    public async Task StarredInWorkshopCollections_IsListedWithItsTitle()
    {
        var page = await CreatePageAsync(ModsSection.WorkshopCollections);
        page.WorkshopCollections.LinkText = PackLink;
        await page.WorkshopCollections.OpenCommand.ExecuteAsync(null);

        page.WorkshopCollections.ToggleOpenCollectionFavoriteCommand.Execute(null);

        Assert.Equal([new WorkshopCollectionRow(Pack, "Survival Pack", null, IsOpen: false, IsFavorite: true)], page.WorkshopFavorites.Collections);
        Assert.True(page.WorkshopFavorites.HasCollections);
    }

    [Fact]
    public async Task OpenCollection_SwitchesToWorkshopCollectionsAndOpensIt()
    {
        _favorites = Favorites(_preferences, new SavedWorkshopFavorites([new SavedWorkshopEntry(Pack, null)], []));
        var page = await CreatePageAsync();
        var vm = page.WorkshopFavorites;
        Assert.Equal("9000", vm.Collections[0].DisplayTitle);

        await vm.OpenCollectionCommand.ExecuteAsync(vm.Collections[0]);

        var collections = page.WorkshopCollections;
        Assert.Equal(ModsSection.WorkshopCollections, page.Section);
        Assert.Equal("Survival Pack", collections.Title);
        Assert.Equal([1003UL, 1001, 1005, 1002, 1004, 1006], collections.Rows.Select(row => row.Id));
        Assert.True(collections.IsOpenCollectionFavorite);
        Assert.Equal(Pack, Assert.Single(collections.Remembered).Id);
        Assert.False(collections.IsLoading);
        Assert.Equal("Survival Pack", vm.Collections[0].DisplayTitle);
    }

    [Fact]
    public async Task OpenCollection_AnotherWasOpen_ShowsTheStarredOne()
    {
        const ulong other = 9100;
        _service.WithPublished(other, "Vehicles Pack", "1");
        _service.Collections[other] = [1003];
        _favorites.Toggle(WorkshopFavoriteKind.Collection, Pack, "Survival Pack");
        var page = await CreatePageAsync(ModsSection.WorkshopCollections);
        page.WorkshopCollections.LinkText = "9100";
        await page.WorkshopCollections.OpenCommand.ExecuteAsync(null);
        page.Section = ModsSection.WorkshopFavorites;

        await page.WorkshopFavorites.OpenCollectionCommand.ExecuteAsync(page.WorkshopFavorites.Collections[0]);

        Assert.Equal("Survival Pack", page.WorkshopCollections.Title);
        Assert.Equal(6, page.WorkshopCollections.ItemCount);
    }

    [Fact]
    public async Task UnstarCollection_RemovesItHereAndFromTheSavedFavorites()
    {
        _favorites.Toggle(WorkshopFavoriteKind.Collection, Pack, "Survival Pack");
        var vm = await CreateAsync();

        vm.ToggleCollectionFavoriteCommand.Execute(vm.Collections[0]);

        Assert.Empty(vm.Collections);
        Assert.False(vm.HasCollections);
        Assert.Empty(_preferences.SavedWorkshopFavorites[^1].Collections);
    }

    // Starred items

    [Fact]
    public async Task Items_StarredInBothSections_ListsThemWithTitlesAndStates()
    {
        var page = await CreatePageAsync(ModsSection.WorkshopSubscriptions);
        var subscriptions = page.WorkshopSubscriptions;
        subscriptions.ToggleFavoriteCommand.Execute(subscriptions.VisibleRows.Single(row => row.Id == 1003));
        page.Section = ModsSection.WorkshopCollections;
        var collections = page.WorkshopCollections;
        collections.LinkText = PackLink;
        await collections.OpenCommand.ExecuteAsync(null);
        Assert.True(collections.Rows.Single(row => row.Id == 1003).IsFavorite);
        collections.ToggleFavoriteCommand.Execute(collections.Rows.Single(row => row.Id == 1005));

        page.Section = ModsSection.WorkshopFavorites;

        var vm = page.WorkshopFavorites;
        Assert.Equal([1005UL, 1003], vm.Rows.Select(row => row.Id));
        Assert.Equal(("Weather", WorkshopItemState.NotSubscribed), (Row(vm, 1005).Title, Row(vm, 1005).Item.State));
        Assert.Equal(("Vehicles", WorkshopItemState.NeedsUpdate), (Row(vm, 1003).Title, Row(vm, 1003).Item.State));
        Assert.All(vm.Rows, row => Assert.True(row.IsFavorite));
        Assert.Equal(2, vm.ItemCount);
    }

    [Fact]
    public async Task Refresh_AsksTheWebApiAgain()
    {
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1005, null);
        var page = await CreatePageAsync();
        var asked = _service.CheckRequests.Count;

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(asked + 1, _service.CheckRequests.Count);
        Assert.Equal([1005UL], page.WorkshopFavorites.Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task SubscribeMissing_SubscribesOnlyTheUnsubscribedOnes()
    {
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1003, null);
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1005, null);
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1006, null);
        _runner.Succeeded = (_, id) => _service.Items.Add(Item(id, manifest: null));
        var vm = await CreateAsync();

        await vm.SubscribeMissingCommand.ExecuteAsync(null);

        var run = Assert.Single(_runner.Runs);
        Assert.Equal(WorkshopAction.Subscribe, run.Request.Action);
        Assert.Equal([1006UL, 1005], run.Request.Ids);
        Assert.Equal(0, vm.MissingCount);
    }

    [Fact]
    public async Task Unstarred_RowLeavesWithoutAskingAgain()
    {
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1001, null);
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1005, null);
        var vm = await CreateAsync();
        var asked = (_service.GetRequests.Count, _service.CheckRequests.Count);

        vm.ToggleFavoriteCommand.Execute(Row(vm, 1005));

        Assert.Equal([1001UL], vm.Rows.Select(row => row.Id));
        Assert.Equal(1, vm.ItemCount);
        Assert.Equal(asked, (_service.GetRequests.Count, _service.CheckRequests.Count));
    }

    [Fact]
    public async Task NoneStarred_SaysHowToStarWithoutTheWebApi()
    {
        var vm = await CreateAsync();

        Assert.True(vm.IsListEmpty);
        Assert.False(vm.HasCollections);
        Assert.Empty(_service.CheckRequests);
    }

    [Fact]
    public async Task TitleLookupFails_ListsThemByIdWithAWarning()
    {
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1005, null);
        var error = TestErrors.Create(ErrorCode.WorkshopNetworkFailed);
        _service.CheckError = () => error;

        var vm = await CreateAsync();

        Assert.Equal("1005", Row(vm, 1005).Title);
        Assert.Equal([ErrorMessages.ToUserText(error)], vm.Warnings);
    }

    [Fact]
    public async Task Generate_SuggestsTheLocalFavoritesName()
    {
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1001, null);
        _dialogs.Accept("Mine");
        var vm = await CreateAsync();

        await vm.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(UiText.WorkshopFavorites_CollectionName, Assert.Single(_dialogs.Prompts).InitialText);
        Assert.Equal(["Core"], Assert.Single(_collections.Saves).ModIds);
    }

    // Share and import

    [Fact]
    public async Task ShareFavorites_CopiesTheIdsAndSaysHowMany()
    {
        _favorites.Toggle(WorkshopFavoriteKind.Collection, Pack, "Survival Pack");
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1001, null);
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1005, null);
        var vm = await CreateAsync();

        await vm.ShareFavoritesCommand.ExecuteAsync(null);

        Assert.Equal("#collections\n9000\n#items\n1005\n1001\n", _clipboard.Text);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, UiText.WorkshopFavorites_Shared, 1, 2), vm.FavoritesStatusText);
    }

    [Fact]
    public async Task ShareFavorites_NothingStarred_IsDisabled()
    {
        var vm = await CreateAsync();

        Assert.False(vm.ShareFavoritesCommand.CanExecute(null));

        _favorites.Toggle(WorkshopFavoriteKind.Item, 1001, null);

        Assert.True(vm.ShareFavoritesCommand.CanExecute(null));
    }

    [Fact]
    public async Task ImportFavorites_SharedText_AddsTheIdsWithTitlesAndSaysHowMany()
    {
        _clipboard.Text = SharedText;
        _dialogs.Accept(SharedText);
        var vm = await CreateAsync();

        await vm.ImportFavoritesCommand.ExecuteAsync(null);

        var prompt = Assert.Single(_dialogs.Prompts);
        Assert.Equal(SharedText.Trim(), prompt.InitialText);
        Assert.True(prompt.IsMultiline);
        Assert.Empty(_service.CollectionRequests);
        Assert.Equal([new WorkshopEntry(Pack, "Survival Pack")], _favorites.Collections);
        Assert.Equal([new WorkshopEntry(1001, "Core Library"), new WorkshopEntry(1005, "Weather")], _favorites.Items);
        Assert.Equal([new WorkshopCollectionRow(Pack, "Survival Pack", null, IsOpen: false, IsFavorite: true)], vm.Collections);
        Assert.Equal(Imported(1, 2, 0), vm.FavoritesStatusText);
    }

    [Fact]
    public async Task ImportFavorites_Twice_AddsNothingTheSecondTimeAndKeepsEarlierFavorites()
    {
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1003, "Vehicles");
        var vm = await CreateAsync();
        _dialogs.Accept(SharedText);
        await vm.ImportFavoritesCommand.ExecuteAsync(null);

        _dialogs.Accept(SharedText);
        await vm.ImportFavoritesCommand.ExecuteAsync(null);

        Assert.Equal([Pack], _favorites.Collections.Select(entry => entry.Id));
        Assert.Equal([1001UL, 1005, 1003], _favorites.Items.Select(entry => entry.Id));
        Assert.Equal(Imported(0, 0, 0), vm.FavoritesStatusText);
    }

    [Fact]
    public async Task ImportFavorites_UnmarkedIds_AreSortedByAskingTheWebApi()
    {
        _dialogs.Accept("https://steamcommunity.com/sharedfiles/filedetails/?id=9000\n1005\nnot an id\n");
        var vm = await CreateAsync();

        await vm.ImportFavoritesCommand.ExecuteAsync(null);

        Assert.Equal<IReadOnlyList<ulong>>([[Pack, 1005]], _service.ClassifyRequests);
        Assert.Equal([new WorkshopEntry(Pack, "Survival Pack")], _favorites.Collections);
        Assert.Equal([new WorkshopEntry(1005, "Weather")], _favorites.Items);
        Assert.Equal(Imported(1, 1, 1), vm.FavoritesStatusText);
    }

    [Fact]
    public async Task ImportFavorites_WebApiCannotSortAnId_SkipsItAndGuessesNothing()
    {
        _service.CollectionError = () => TestErrors.Create(ErrorCode.WorkshopNetworkFailed);
        _dialogs.Accept("9000\n#items\n1005");
        var vm = await CreateAsync();

        await vm.ImportFavoritesCommand.ExecuteAsync(null);

        Assert.Empty(_favorites.Collections);
        Assert.Equal([1005UL], _favorites.Items.Select(entry => entry.Id));
        Assert.Equal(Imported(0, 1, 1), vm.FavoritesStatusText);
    }

    [Fact]
    public async Task ImportFavorites_UnmarkedIdsAlreadyStarred_AreNotAskedAgain()
    {
        _favorites.Toggle(WorkshopFavoriteKind.Collection, Pack, "Survival Pack");
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1005, "Weather");
        _dialogs.Accept("9000\n1005\n");
        var vm = await CreateAsync();

        await vm.ImportFavoritesCommand.ExecuteAsync(null);

        Assert.Empty(_service.ClassifyRequests);
        Assert.Equal(Imported(0, 0, 0), vm.FavoritesStatusText);
    }

    [Fact]
    public async Task ImportFavorites_Prompt_RejectsTextWithoutAnIdAndIgnoresUnrelatedClipboardText()
    {
        _clipboard.Text = "survival pack:Core;Maps;";
        var vm = await CreateAsync();

        await vm.ImportFavoritesCommand.ExecuteAsync(null);

        var prompt = Assert.Single(_dialogs.Prompts);
        Assert.Equal("", prompt.InitialText);
        Assert.Equal(UiText.WorkshopFavorites_ImportNothing, prompt.Validate("#collections\n#items\nno ids here"));
        Assert.Null(prompt.Validate("1001"));
        Assert.Empty(_preferences.SavedWorkshopFavorites);
        Assert.Null(vm.FavoritesStatusText);
    }

    [Fact]
    public async Task ImportFavorites_ListsTheNewItems()
    {
        var vm = await CreateAsync();
        _dialogs.Accept("#items\n1005");

        await vm.ImportFavoritesCommand.ExecuteAsync(null);

        Assert.Equal([1005UL], vm.Rows.Select(row => row.Id));
        Assert.Equal(WorkshopItemState.NotSubscribed, Row(vm, 1005).Item.State);
    }
}
