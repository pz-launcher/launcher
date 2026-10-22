using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.ViewModels.Mods;
using PzLauncher.Desktop.ViewModels.Mods.Workshop;
using static PzLauncher.Desktop.Tests.TestSupport.TestInstalls;
using static PzLauncher.Desktop.Tests.TestSupport.TestWorkshop;

namespace PzLauncher.Desktop.Tests.ViewModels.Mods.Workshop;

[Collection(UiTextCultureCollection.Name)]
public class WorkshopSubscriptionsViewModelTests : UiTextCultureTest
{
    private readonly FakeInstallDetectionService _detection = new();
    private readonly FakeModCollectionService _collections = new();
    private readonly FakeInstallOverridesService _overrides = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeClipboardService _clipboard = new();
    private readonly FakeUrlLauncher _urls = new();
    private readonly FakeWorkshopService _service = new();
    private readonly FakeWorkshopActionRunner _runner = new();
    private readonly GameRunState _runState = new();
    private readonly FakePreferenceStore _preferences = new();
    private readonly WorkshopFavorites _favorites;

    public WorkshopSubscriptionsViewModelTests()
    {
        Installs = new(_detection, new FakeFolderPicker(), new FakePlatformEnvironment(), Localization);
        _favorites = Favorites(_preferences);
        _service
            .WithItem(Item(1001, mods: ["Core"], collections: ["Survival"], latest: Details(1001, "Core Library", "100")))
            .WithItem(Item(1002, mods: ["Maps", "MapsExtra"], collections: ["Survival", "Driving"], latest: Details(1002, "Map Pack", "100")))
            .WithItem(Item(1003, mods: ["Cars"], latest: Details(1003, "Vehicles", "300"), state: WorkshopItemState.NeedsUpdate))
            .WithItem(Item(1004, manifest: null));
    }

    private InstallsViewModel Installs { get; }

    private async Task<ModsViewModel> CreatePageAsync(ModsSection section = ModsSection.WorkshopSubscriptions, params PzInstall[] installs)
    {
        _detection.ScanResults.Enqueue(FakeInstallDetectionService.Found(installs.Length == 0 ? [SteamGame] : installs));
        await Installs.ActivateCommand.ExecuteAsync(null);
        var orderWriter = new ModCollectionOrderWriter(_collections, _runState, new FakePlatformEnvironment(), new ManualTimeProvider());
        var history = TestWorkshop.History();
        var userCollections = TestWorkshop.UserCollections();
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

    private async Task<WorkshopSubscriptionsViewModel> CreateAsync() => (await CreatePageAsync()).WorkshopSubscriptions;

    private static WorkshopItemRow Row(WorkshopSubscriptionsViewModel vm, ulong id) => vm.VisibleRows.Single(row => row.Id == id);

    private static void SelectIds(WorkshopSubscriptionsViewModel vm, params ulong[] ids)
    {
        foreach (var id in ids)
        {
            Row(vm, id).IsSelected = true;
        }
    }

    // Loading

    [Fact]
    public async Task SectionShown_ReadsTheInstallInUseWithItsGameArgs()
    {
        _overrides.Set(SteamGame.RootPath, GameLaunchOverrides.Empty with { ExtraGameArgs = @"-cachedir=D:\pz" });

        var vm = await CreateAsync();

        Assert.Equal((SteamGame.RootPath, @"-cachedir=D:\pz"), _service.GetRequests[0]);
        Assert.Equal(4, vm.VisibleRows.Count);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task OtherSectionShown_ReadsNoSubscriptionsUntilTheSectionOpens()
    {
        var page = await CreatePageAsync(ModsSection.Mods);
        Assert.Empty(_service.GetRequests);

        page.Section = ModsSection.WorkshopSubscriptions;

        Assert.NotEmpty(_service.GetRequests);
        Assert.Equal(4, page.WorkshopSubscriptions.VisibleRows.Count);
    }

    [Fact]
    public async Task DedicatedServerInUse_ReadsNothingAndAllowsNoActions()
    {
        var page = await CreatePageAsync(ModsSection.WorkshopSubscriptions, SteamServer);

        Assert.Empty(_service.GetRequests);
        Assert.Empty(page.WorkshopSubscriptions.VisibleRows);
        Assert.False(page.WorkshopSubscriptions.SubscribeCommand.CanExecute(null));
        Assert.False(page.WorkshopSubscriptions.CheckUpdatesCommand.CanExecute(null));
    }

    [Fact]
    public async Task InstallInUseChanges_ReadsTheNewInstall()
    {
        await CreatePageAsync(ModsSection.WorkshopSubscriptions, SteamGame, ManualGame);

        Installs.Use(ManualGame.RootPath);

        Assert.Equal(ManualGame.RootPath, _service.GetRequests[^1].Root);
    }

    [Fact]
    public async Task Refresh_ReadsTheFilesAgain()
    {
        var page = await CreatePageAsync();
        var reads = _service.GetRequests.Count;

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.True(_service.GetRequests.Count > reads);
    }

    [Fact]
    public async Task ReadFails_ShowsTheErrorAndNoRows()
    {
        var error = TestErrors.Create(ErrorCode.WorkshopSubscriptionsUnreadable, @"C:\Steam\userdata\1\ugc\108600_subscriptions.vdf");
        _service.GetError = () => error;

        var vm = await CreateAsync();

        Assert.Equal(ErrorMessages.ToUserText(error), vm.ErrorText);
        Assert.Empty(vm.VisibleRows);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task Load_ShowsTheReadWarnings()
    {
        var warning = TestErrors.Create(ErrorCode.WorkshopManifestUnreadable, @"D:\SteamLibrary\steamapps\workshop\appworkshop_108600.acf");
        _service.Warnings.Add(warning);

        var vm = await CreateAsync();

        Assert.Equal([ErrorMessages.ToUserText(warning)], vm.Warnings);
    }

    [Fact]
    public async Task Load_FetchesTitlesOnceForUntitledItems()
    {
        _service.WithPublished(1004, "Weather", "500");

        var vm = await CreateAsync();

        Assert.Equal([1004UL], Assert.Single(_service.CheckRequests));
        Assert.Equal("Weather", Row(vm, 1004).Title);
    }

    [Fact]
    public async Task Load_TitleLookupFails_KeepsTheListAndShowsAWarning()
    {
        var error = TestErrors.Create(ErrorCode.WorkshopNetworkFailed);
        _service.CheckError = () => error;

        var vm = await CreateAsync();

        Assert.Equal(4, vm.VisibleRows.Count);
        Assert.Equal("1004", Row(vm, 1004).Title);
        Assert.Equal([ErrorMessages.ToUserText(error)], vm.Warnings);
    }

    [Fact]
    public async Task Rows_ShowModIdsSizeAndTheCollectionsUsingThem()
    {
        var vm = await CreateAsync();

        var row = Row(vm, 1002);
        Assert.Equal("Map Pack", row.Title);
        Assert.Equal("Maps, MapsExtra", row.ModIds);
        Assert.Equal("Survival, Driving", row.CollectionNames);
        Assert.Equal(3.0, row.SizeMb);
        Assert.Null(Row(vm, 1004).SizeMb);
        Assert.False(Row(vm, 1004).HasTitle);
    }

    // Search, filter, sort

    [Theory]
    [InlineData("map pack", 1002UL)]
    [InlineData("1003", 1003UL)]
    [InlineData("mapsextra", 1002UL)]
    public async Task Search_MatchesTitleItemIdOrModId(string search, ulong expected)
    {
        var vm = await CreateAsync();

        vm.SearchText = search;

        Assert.Equal([expected], vm.VisibleRows.Select(row => row.Id));
    }

    [Theory]
    [InlineData(WorkshopSubscriptionsFilter.All, new ulong[] { 1001, 1002, 1003, 1004 })]
    [InlineData(WorkshopSubscriptionsFilter.NeedsUpdate, new ulong[] { 1003 })]
    [InlineData(WorkshopSubscriptionsFilter.Unused, new ulong[] { 1003, 1004 })]
    public async Task Filter_NarrowsTheList(WorkshopSubscriptionsFilter filter, ulong[] expected)
    {
        var vm = await CreateAsync();

        vm.Filter = filter;

        Assert.Equal(expected, vm.VisibleRows.Select(row => row.Id).Order());
    }

    [Fact]
    public async Task SearchMatchesNothing_IsFilterEmpty()
    {
        var vm = await CreateAsync();

        vm.SearchText = "no such item";

        Assert.True(vm.IsFilterEmpty);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task NoSubscriptions_IsEmpty()
    {
        _service.Items.Clear();

        var vm = await CreateAsync();

        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public async Task SortBy_TitleFirst_ThenReversesOnTheSecondClick()
    {
        var vm = await CreateAsync();

        Assert.Equal([1001UL, 1002, 1003, 1004], vm.VisibleRows.Select(row => row.Id));

        vm.SortByCommand.Execute(WorkshopSubscriptionsSortColumn.Title);

        Assert.True(vm.SortDescending);
        Assert.Equal([1003UL, 1002, 1001, 1004], vm.VisibleRows.Select(row => row.Id));
    }

    [Fact]
    public async Task SortBy_State_PutsItemsNeedingAttentionFirst()
    {
        var vm = await CreateAsync();

        vm.SortByCommand.Execute(WorkshopSubscriptionsSortColumn.State);

        Assert.Equal([1003UL, 1004, 1001, 1002], vm.VisibleRows.Select(row => row.Id));
    }

    [Fact]
    public async Task Selection_SurvivesAReload()
    {
        var page = await CreatePageAsync();
        SelectIds(page.WorkshopSubscriptions, 1002);

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.True(Row(page.WorkshopSubscriptions, 1002).IsSelected);
        Assert.Equal(1, page.WorkshopSubscriptions.SelectedCount);
    }

    // Check for updates

    [Fact]
    public async Task CheckUpdates_AsksForEveryInstalledItemAndMarksTheOutdatedOnes()
    {
        var vm = await CreateAsync();
        _service.WithPublished(1001, "Core Library", "101");

        await vm.CheckUpdatesCommand.ExecuteAsync(null);

        Assert.Equal(new ulong[] { 1001, 1002, 1003 }, _service.CheckRequests[1].Order());
        Assert.Equal(WorkshopItemState.NeedsUpdate, Row(vm, 1001).Item.State);
        Assert.Equal(2, vm.OutdatedCount);
        Assert.Equal(string.Format(UiText.Workshop_CheckedOutdated, 2), vm.StatusText);
    }

    [Fact]
    public async Task CheckUpdates_Fails_ShowsTheError()
    {
        var vm = await CreateAsync();
        var error = TestErrors.Create(ErrorCode.WorkshopNetworkFailed);
        _service.CheckError = () => error;

        await vm.CheckUpdatesCommand.ExecuteAsync(null);

        Assert.Equal([ErrorMessages.ToUserText(error)], _dialogs.Errors);
        Assert.Null(vm.StatusText);
    }

    // Steam actions

    [Fact]
    public async Task UpdateAll_UpdatesTheOutdatedItemsThenReads()
    {
        var vm = await CreateAsync();
        _runner.Succeeded = (_, id) => _service.Install(id, "300");
        var reads = _service.GetRequests.Count;

        await vm.UpdateOutdatedCommand.ExecuteAsync(null);

        var run = Assert.Single(_runner.Runs);
        Assert.Equal(SteamGame.RootPath, run.Root);
        Assert.Equal(WorkshopAction.Update, run.Request.Action);
        Assert.Equal([1003UL], run.Request.Ids);
        var dialog = Assert.Single(_dialogs.WorkshopActions);
        Assert.True(dialog.IsFinished);
        Assert.Equal(WorkshopActionItemState.Done, Assert.Single(dialog.Items).State);
        Assert.True(_service.GetRequests.Count > reads);
        Assert.Equal(0, vm.OutdatedCount);
        Assert.False(vm.IsActionRunning);
    }

    [Fact]
    public async Task UpdateAll_StillOutdatedAfterSteamFinished_ReportsTheItem()
    {
        var vm = await CreateAsync();

        await vm.UpdateOutdatedCommand.ExecuteAsync(null);

        var row = Assert.Single(Assert.Single(_dialogs.WorkshopActions).Items);
        Assert.True(row.IsStillOutdated);
        Assert.Equal(WorkshopActionItemState.Failed, row.State);
    }

    [Fact]
    public async Task UpdateSelected_ForceUpdatesTheSelectedItemsWhateverTheirState()
    {
        var vm = await CreateAsync();
        SelectIds(vm, 1001, 1002);

        await vm.UpdateSelectedCommand.ExecuteAsync(null);

        var run = Assert.Single(_runner.Runs);
        Assert.Equal(WorkshopAction.Update, run.Request.Action);
        Assert.Equal(new ulong[] { 1001, 1002 }, run.Request.Ids.Order());
        Assert.All(Assert.Single(_dialogs.WorkshopActions).Items, row => Assert.Equal(WorkshopActionItemState.Done, row.State));
    }

    [Fact]
    public async Task Update_WhileSteamDownloads_ShowsTheRowDownloading()
    {
        var vm = await CreateAsync();
        _runner.Gate = new TaskCompletionSource();

        var running = vm.UpdateOutdatedCommand.ExecuteAsync(null);

        Assert.True(vm.IsActionRunning);
        Assert.True(Row(vm, 1003).IsDownloading);
        Assert.Equal(50, Row(vm, 1003).DownloadPercent);
        Assert.False(vm.SubscribeCommand.CanExecute(null));
        _runner.Gate.SetResult();
        await running;
        Assert.False(Row(vm, 1003).IsDownloading);
    }

    [Fact]
    public async Task Action_ItemFails_ShowsItsFailure()
    {
        var vm = await CreateAsync();
        SelectIds(vm, 1001);
        _runner.Failure = _ => WorkshopItemFailure.DownloadFailed;

        await vm.UpdateSelectedCommand.ExecuteAsync(null);

        var row = Assert.Single(Assert.Single(_dialogs.WorkshopActions).Items);
        Assert.Equal(WorkshopActionItemState.Failed, row.State);
        Assert.Equal(WorkshopItemFailure.DownloadFailed, row.Failure);
        Assert.True(row.HasSteamResult);
    }

    [Fact]
    public async Task Action_SteamNotRunning_ShowsTheErrorInTheDialog()
    {
        var vm = await CreateAsync();
        var error = TestErrors.Create(ErrorCode.WorkshopSteamNotRunning);
        _runner.RunError = _ => error;

        await vm.UpdateOutdatedCommand.ExecuteAsync(null);

        var dialog = Assert.Single(_dialogs.WorkshopActions);
        Assert.Equal(ErrorMessages.ToUserText(error), dialog.ErrorText);
        Assert.True(dialog.IsFinished);
        Assert.Equal(4, vm.VisibleRows.Count);
    }

    [Fact]
    public async Task DialogClosedWhileRunning_CancelsTheRun()
    {
        var vm = await CreateAsync();
        _runner.Gate = new TaskCompletionSource();
        _dialogs.WorkshopActionHandler = action =>
        {
            var started = action.StartAsync();
            action.CloseCommand.Execute(null);
            return started;
        };

        await vm.UpdateOutdatedCommand.ExecuteAsync(null);

        var dialog = Assert.Single(_dialogs.WorkshopActions);
        Assert.False(dialog.IsFinished);
        Assert.False(vm.IsActionRunning);
    }

    [Fact]
    public async Task Unsubscribe_ConfirmNamesTheCollectionsUsingTheItems()
    {
        var vm = await CreateAsync();
        SelectIds(vm, 1001, 1002);
        _dialogs.ConfirmAnswers.Enqueue(true);

        await vm.UnsubscribeCommand.ExecuteAsync(null);

        var confirm = Assert.Single(_dialogs.Confirms);
        Assert.Equal(string.Format(UiText.Workshop_UnsubscribeUsedMessage, 2, "Driving, Survival"), confirm.Message);
        Assert.Contains(string.Format(UiText.Mods_NameWithId, "Map Pack", "1002"), confirm.Items);
        var run = Assert.Single(_runner.Runs);
        Assert.Equal(WorkshopAction.Unsubscribe, run.Request.Action);
        Assert.Equal(new ulong[] { 1001, 1002 }, run.Request.Ids.Order());
    }

    [Fact]
    public async Task Unsubscribe_ItemsUsedByNoCollection_SaysSo()
    {
        var vm = await CreateAsync();
        SelectIds(vm, 1003);

        await vm.UnsubscribeCommand.ExecuteAsync(null);

        Assert.Equal(string.Format(UiText.Workshop_UnsubscribeMessage, 1), Assert.Single(_dialogs.Confirms).Message);
    }

    [Fact]
    public async Task Unsubscribe_Declined_RunsNothing()
    {
        var vm = await CreateAsync();
        SelectIds(vm, 1001);

        await vm.UnsubscribeCommand.ExecuteAsync(null);

        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public async Task Subscribe_PastedLink_SubscribesItsId()
    {
        var vm = await CreateAsync();
        _dialogs.Accept("https://steamcommunity.com/sharedfiles/filedetails/?id=2409333430");

        await vm.SubscribeCommand.ExecuteAsync(null);

        var run = Assert.Single(_runner.Runs);
        Assert.Equal(WorkshopAction.Subscribe, run.Request.Action);
        Assert.Equal([2409333430UL], run.Request.Ids);
    }

    [Fact]
    public async Task Subscribe_Prompt_OffersAClipboardLinkAndRejectsOtherText()
    {
        var vm = await CreateAsync();
        _clipboard.Text = " 2409333430 ";

        await vm.SubscribeCommand.ExecuteAsync(null);

        var prompt = Assert.Single(_dialogs.Prompts);
        Assert.Equal("2409333430", prompt.InitialText);
        Assert.Null(prompt.Validate("steam://url/CommunityFilePage/2409333430"));
        Assert.Equal(UiText.Workshop_LinkInvalid, prompt.Validate("not a link"));
        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public async Task Subscribe_ClipboardHoldsOtherText_StartsEmpty()
    {
        var vm = await CreateAsync();
        _clipboard.Text = "hello";

        await vm.SubscribeCommand.ExecuteAsync(null);

        Assert.Equal("", Assert.Single(_dialogs.Prompts).InitialText);
    }

    [Fact]
    public async Task GameRunning_DisablesSteamActionsButNotTheUpdateCheck()
    {
        var vm = await CreateAsync();
        SelectIds(vm, 1001);

        _runState.IsGameRunning = true;

        Assert.False(vm.CanRunActions);
        Assert.False(vm.SubscribeCommand.CanExecute(null));
        Assert.False(vm.UnsubscribeCommand.CanExecute(null));
        Assert.False(vm.UpdateOutdatedCommand.CanExecute(null));
        Assert.False(vm.UpdateSelectedCommand.CanExecute(null));
        Assert.True(vm.CheckUpdatesCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelectionCommands_NeedASelection()
    {
        var vm = await CreateAsync();

        Assert.False(vm.UnsubscribeCommand.CanExecute(null));
        Assert.False(vm.UpdateSelectedCommand.CanExecute(null));

        SelectIds(vm, 1002);

        Assert.True(vm.UnsubscribeCommand.CanExecute(null));
        Assert.True(vm.UpdateSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task OpenInSteam_OpensTheItemPageInTheSteamClient()
    {
        var vm = await CreateAsync();

        await vm.OpenInSteamCommand.ExecuteAsync(Row(vm, 1002));

        Assert.Equal("steam://url/CommunityFilePage/1002", Assert.Single(_urls.Opened).OriginalString);
    }

    // Local favorites

    [Fact]
    public async Task ToggleFavorite_StarsTheRowAndSavesItWithItsTitle()
    {
        var vm = await CreateAsync();

        vm.ToggleFavoriteCommand.Execute(Row(vm, 1002));

        Assert.True(Row(vm, 1002).IsFavorite);
        Assert.Equal([new SavedWorkshopEntry(1002, "Map Pack")], _preferences.SavedWorkshopFavorites[^1].Items);
    }

    [Fact]
    public async Task ToggleFavorite_WhileAGameRuns_StarsWithoutSteam()
    {
        var vm = await CreateAsync();
        _runState.IsGameRunning = true;

        Assert.True(vm.ToggleFavoriteCommand.CanExecute(Row(vm, 1001)));
        vm.ToggleFavoriteCommand.Execute(Row(vm, 1001));
        vm.ToggleFavoriteCommand.Execute(Row(vm, 1002));
        vm.ToggleFavoriteCommand.Execute(Row(vm, 1002));

        Assert.Equal([1001UL], _favorites.Items.Select(entry => entry.Id));
        Assert.Empty(_runner.Runs);
        Assert.Empty(_runner.ListRuns);
    }

    [Fact]
    public async Task Load_StarredEarlier_ShowsTheStarAndKeepsTheNewTitle()
    {
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1001, null);

        var vm = await CreateAsync();

        Assert.True(Row(vm, 1001).IsFavorite);
        Assert.False(Row(vm, 1002).IsFavorite);
        Assert.Equal([new WorkshopEntry(1001, "Core Library")], _favorites.Items);
    }

    [Fact]
    public async Task FavoritesFilter_ShowsExactlyTheStarredSubscriptionsAndFollowsTheStars()
    {
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1003, null);
        _favorites.Toggle(WorkshopFavoriteKind.Item, 5005, null);
        var vm = await CreateAsync();

        vm.Filter = WorkshopSubscriptionsFilter.Favorites;

        Assert.Equal([1003UL], vm.VisibleRows.Select(row => row.Id));

        vm.ToggleFavoriteCommand.Execute(Row(vm, 1003));

        Assert.Empty(vm.VisibleRows);
        Assert.True(vm.IsFilterEmpty);
    }
}
