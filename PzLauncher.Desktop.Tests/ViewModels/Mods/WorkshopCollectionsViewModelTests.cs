using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.ViewModels.Mods;
using static PzLauncher.Desktop.Tests.TestSupport.TestInstalls;
using static PzLauncher.Desktop.Tests.TestSupport.TestWorkshop;

namespace PzLauncher.Desktop.Tests.ViewModels.Mods;

[Collection(UiTextCultureCollection.Name)]
public class WorkshopCollectionsViewModelTests : UiTextCultureTest
{
    private const ulong Pack = 9000;

    private const string PackLink = "https://steamcommunity.com/sharedfiles/filedetails/?id=9000";

    private readonly FakeInstallDetectionService _detection = new();
    private readonly FakeModCollectionService _collections = new();
    private readonly FakeInstallOverridesService _overrides = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeUrlLauncher _urls = new();
    private readonly FakeWorkshopService _service = new();
    private readonly FakeWorkshopActionRunner _runner = new();
    private readonly FakePreferenceStore _preferences = new();
    private readonly GameRunState _runState = new();

    public WorkshopCollectionsViewModelTests()
    {
        Installs = new(_detection, new FakeFolderPicker(), new FakePlatformEnvironment(), Localization);
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

    private async Task<ModsViewModel> CreatePageAsync(params SavedWorkshopCollection[] saved)
    {
        _detection.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamGame));
        await Installs.ActivateCommand.ExecuteAsync(null);
        var orderWriter = new ModCollectionOrderWriter(_collections, _runState, new FakePlatformEnvironment(), new ManualTimeProvider());
        var page = new ModsViewModel(
            Installs, _collections, _overrides, new FakeModCollectionDefaultsService(), new ModCollectionApplier(_collections, _dialogs), _dialogs,
            new FakeClipboardService(), _runState, orderWriter, _service, _runner, _urls, History(_preferences, saved), Localization)
        {
            Section = ModsSection.WorkshopCollections,
        };
        await page.ActivateCommand.ExecuteAsync(null);
        return page;
    }

    private async Task<WorkshopCollectionsViewModel> CreateAsync() => (await CreatePageAsync()).WorkshopCollections;

    private async Task<WorkshopCollectionsViewModel> OpenPackAsync()
    {
        var vm = await CreateAsync();
        vm.LinkText = PackLink;
        await vm.OpenCommand.ExecuteAsync(null);
        return vm;
    }

    private static WorkshopItemRow Row(WorkshopCollectionsViewModel vm, ulong id) => vm.Rows.Single(row => row.Id == id);

    // Opening

    [Fact]
    public async Task Open_CollectionLink_ListsItemsInCollectionOrderWithTheirStates()
    {
        var vm = await OpenPackAsync();

        Assert.Equal([Pack], _service.CollectionRequests);
        Assert.Equal("Survival Pack", vm.Title);
        Assert.Equal([1003UL, 1001, 1005, 1002, 1004, 1006], vm.Rows.Select(row => row.Id));
        Assert.Equal(WorkshopItemState.NeedsUpdate, Row(vm, 1003).Item.State);
        Assert.Equal(WorkshopItemState.UpToDate, Row(vm, 1001).Item.State);
        Assert.Equal(WorkshopItemState.NotSubscribed, Row(vm, 1005).Item.State);
        Assert.Equal(WorkshopItemState.NotInstalled, Row(vm, 1004).Item.State);
        Assert.Equal(WorkshopItemState.NotSubscribed, Row(vm, 1006).Item.State);
        Assert.Equal("Weather", Row(vm, 1005).Title);
        Assert.Equal("1006", Row(vm, 1006).Title);
        Assert.Equal((2, 4, 1), (vm.MissingCount, vm.SubscribedCount, vm.OutdatedCount));
        Assert.Equal("", vm.LinkText);
        Assert.False(vm.IsLoading);
    }

    [Theory]
    [InlineData("9000")]
    [InlineData("steamcommunity.com/workshop/filedetails/?id=9000")]
    [InlineData("steam://url/CommunityFilePage/9000")]
    public async Task Open_IdOrOtherLinkForms_OpensTheCollection(string text)
    {
        var vm = await CreateAsync();

        vm.LinkText = text;
        await vm.OpenCommand.ExecuteAsync(null);

        Assert.Equal("Survival Pack", vm.Title);
    }

    [Fact]
    public async Task LinkText_NeitherLinkNorId_CannotOpenAndSaysWhy()
    {
        var vm = await CreateAsync();

        vm.LinkText = "survival pack";

        Assert.False(vm.OpenCommand.CanExecute(null));
        Assert.Equal(UiText.Workshop_LinkInvalid, vm.LinkErrorText);
    }

    [Fact]
    public async Task Open_NotACollection_ShowsTheErrorAndRemembersNothing()
    {
        var vm = await CreateAsync();

        vm.LinkText = "1001";
        await vm.OpenCommand.ExecuteAsync(null);

        Assert.Equal(ErrorMessages.ToUserText(TestErrors.Create(ErrorCode.WorkshopCollectionNotFound)), vm.ErrorText);
        Assert.False(vm.HasCollection);
        Assert.Empty(vm.Rows);
        Assert.Empty(vm.Remembered);
        Assert.Empty(_preferences.SavedWorkshopCollections);
    }

    [Fact]
    public async Task SubscriptionsUnreadable_ShowsTheErrorAndNoRows()
    {
        var error = TestErrors.Create(ErrorCode.WorkshopSubscriptionsUnreadable, @"C:\Steam\userdata\1\ugc\108600_subscriptions.vdf");
        _service.GetError = () => error;

        var vm = await OpenPackAsync();

        Assert.Equal(ErrorMessages.ToUserText(error), vm.ErrorText);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task Refresh_OpensTheCollectionAgain()
    {
        var page = await CreatePageAsync();
        page.WorkshopCollections.LinkText = PackLink;
        await page.WorkshopCollections.OpenCommand.ExecuteAsync(null);

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.Equal([Pack, Pack], _service.CollectionRequests);
    }

    [Fact]
    public async Task SectionShownAgain_RereadsTheSubscriptionsWithoutTheWebApi()
    {
        var page = await CreatePageAsync();
        page.WorkshopCollections.LinkText = PackLink;
        await page.WorkshopCollections.OpenCommand.ExecuteAsync(null);
        var reads = _service.GetRequests.Count;
        _service.Items.RemoveAll(item => item.Id == 1001);

        page.Section = ModsSection.Mods;
        page.Section = ModsSection.WorkshopCollections;

        Assert.True(_service.GetRequests.Count > reads);
        Assert.Single(_service.CollectionRequests);
        Assert.Equal(WorkshopItemState.NotSubscribed, Row(page.WorkshopCollections, 1001).Item.State);
    }

    // Remembered collections

    [Fact]
    public async Task Open_RemembersTheCollectionMostRecentFirst()
    {
        var vm = await CreatePageAsync(new SavedWorkshopCollection(9100, "Vehicles Pack"), new SavedWorkshopCollection(Pack, "Old title"));

        vm.WorkshopCollections.LinkText = PackLink;
        await vm.WorkshopCollections.OpenCommand.ExecuteAsync(null);

        Assert.Equal([new SavedWorkshopCollection(Pack, "Survival Pack"), new SavedWorkshopCollection(9100, "Vehicles Pack")], _preferences.SavedWorkshopCollections[^1]);
        Assert.Equal(
            [new RememberedWorkshopCollectionRow(Pack, "Survival Pack", IsOpen: true), new RememberedWorkshopCollectionRow(9100, "Vehicles Pack", IsOpen: false)],
            vm.WorkshopCollections.Remembered);
    }

    [Fact]
    public async Task OpenRemembered_OpensThatCollection()
    {
        var vm = (await CreatePageAsync(new SavedWorkshopCollection(Pack, null))).WorkshopCollections;
        Assert.Equal("9000", vm.Remembered[0].Title);

        await vm.OpenRememberedCommand.ExecuteAsync(vm.Remembered[0]);

        Assert.Equal("Survival Pack", vm.Title);
        Assert.Equal("Survival Pack", vm.Remembered[0].Title);
    }

    [Fact]
    public async Task Forget_RemovesTheCollectionFromTheSavedList()
    {
        var vm = (await CreatePageAsync(new SavedWorkshopCollection(Pack, "Survival Pack"), new SavedWorkshopCollection(9100, "Vehicles Pack"))).WorkshopCollections;

        vm.ForgetCommand.Execute(vm.Remembered[0]);

        Assert.Equal([9100UL], vm.Remembered.Select(row => row.Id));
        Assert.Equal([new SavedWorkshopCollection(9100, "Vehicles Pack")], _preferences.SavedWorkshopCollections[^1]);
    }

    // Steam actions

    [Fact]
    public async Task SubscribeMissing_SubscribesTheUnsubscribedItemsAndReloads()
    {
        _runner.Succeeded = (_, id) => _service.Items.Add(Item(id, manifest: null));
        var vm = await OpenPackAsync();

        await vm.SubscribeMissingCommand.ExecuteAsync(null);

        var run = Assert.Single(_runner.Runs);
        Assert.Equal((SteamGame.RootPath, WorkshopAction.Subscribe), (run.Root, run.Request.Action));
        Assert.Equal([1005UL, 1006], run.Request.Ids);
        Assert.Equal(WorkshopItemState.NotInstalled, Row(vm, 1005).Item.State);
        Assert.Equal(0, vm.MissingCount);
        Assert.False(vm.SubscribeMissingCommand.CanExecute(null));
    }

    [Fact]
    public async Task UnsubscribeAll_ConfirmNamesTheCollectionsUsingTheItems()
    {
        _dialogs.ConfirmAnswers.Enqueue(true);
        var vm = await OpenPackAsync();

        await vm.UnsubscribeAllCommand.ExecuteAsync(null);

        var confirm = Assert.Single(_dialogs.Confirms);
        Assert.Equal(string.Format(UiText.Workshop_UnsubscribeUsedMessage, 4, "Driving, Survival"), confirm.Message);
        var run = Assert.Single(_runner.Runs);
        Assert.Equal(WorkshopAction.Unsubscribe, run.Request.Action);
        Assert.Equal([1003UL, 1001, 1002, 1004], run.Request.Ids);
    }

    [Fact]
    public async Task UnsubscribeAll_Declined_RunsNothing()
    {
        var vm = await OpenPackAsync();

        await vm.UnsubscribeAllCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.Confirms);
        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public async Task UpdateOutdated_UpdatesTheOutdatedItemsAndReportsOnesStillOutdated()
    {
        var vm = await OpenPackAsync();

        await vm.UpdateOutdatedCommand.ExecuteAsync(null);

        var run = Assert.Single(_runner.Runs);
        Assert.Equal(WorkshopAction.Update, run.Request.Action);
        Assert.Equal([1003UL], run.Request.Ids);
        Assert.True(Assert.Single(Assert.Single(_dialogs.WorkshopActions).Items).IsStillOutdated);
    }

    [Fact]
    public async Task UpdateOutdated_SteamInstallsTheLatest_ItemIsUpToDate()
    {
        _runner.Succeeded = (_, id) => _service.Install(id, "300");
        var vm = await OpenPackAsync();

        await vm.UpdateOutdatedCommand.ExecuteAsync(null);

        Assert.Equal(WorkshopItemState.UpToDate, Row(vm, 1003).Item.State);
        Assert.False(vm.UpdateOutdatedCommand.CanExecute(null));
    }

    [Fact]
    public async Task ActionRuns_WriteActionsAreDisabledUntilItEnds()
    {
        _runner.Gate = new TaskCompletionSource();
        var vm = await OpenPackAsync();

        var running = vm.UpdateOutdatedCommand.ExecuteAsync(null);

        Assert.True(vm.IsActionRunning);
        Assert.False(vm.SubscribeMissingCommand.CanExecute(null));
        Assert.False(vm.UnsubscribeAllCommand.CanExecute(null));
        Assert.False(vm.GenerateCommand.CanExecute(null));
        Assert.True(Row(vm, 1003).IsDownloading);
        _runner.Gate.SetResult();
        await running;
        Assert.False(vm.IsActionRunning);
        Assert.True(vm.SubscribeMissingCommand.CanExecute(null));
    }

    [Fact]
    public async Task GameRuns_WriteActionsAreDisabledButSteamPagesStillOpen()
    {
        var vm = await OpenPackAsync();

        _runState.IsGameRunning = true;

        Assert.False(vm.CanRunActions);
        Assert.False(vm.SubscribeMissingCommand.CanExecute(null));
        Assert.False(vm.UnsubscribeAllCommand.CanExecute(null));
        Assert.False(vm.UpdateOutdatedCommand.CanExecute(null));
        Assert.False(vm.GenerateCommand.CanExecute(null));
        Assert.True(vm.OpenCollectionInSteamCommand.CanExecute(null));
    }

    [Fact]
    public async Task NoCollectionOpen_AllowsNoActions()
    {
        var vm = await CreateAsync();

        Assert.False(vm.HasCollection);
        Assert.False(vm.SubscribeMissingCommand.CanExecute(null));
        Assert.False(vm.GenerateCommand.CanExecute(null));
        Assert.False(vm.OpenCollectionInSteamCommand.CanExecute(null));
    }

    [Fact]
    public async Task OpenInSteam_OpensTheCollectionAndItemPagesInTheClient()
    {
        var vm = await OpenPackAsync();

        await vm.OpenCollectionInSteamCommand.ExecuteAsync(null);
        await vm.OpenInSteamCommand.ExecuteAsync(Row(vm, 1005));

        Assert.Equal([WorkshopLinks.ClientPage(Pack), WorkshopLinks.ClientPage(1005)], _urls.Opened);
    }

    // Launcher mod collection

    [Fact]
    public async Task Generate_SingleModItemsOnly_SavesTheirModsInCollectionOrderWithoutAsking()
    {
        _service.Collections[Pack] = [1003, 1001];
        _dialogs.Accept("From Steam");
        var vm = await OpenPackAsync();

        await vm.GenerateCommand.ExecuteAsync(null);

        Assert.Empty(_dialogs.ModPicks);
        var prompt = Assert.Single(_dialogs.Prompts);
        Assert.Equal("Survival Pack", prompt.InitialText);
        var save = Assert.Single(_collections.Saves);
        Assert.Null(save.OriginalName);
        Assert.Equal("From Steam", save.Name);
        Assert.Equal(["Cars", "Core"], save.ModIds);
        Assert.Equal(string.Format(UiText.WorkshopCollections_Generated, "From Steam", 2), vm.StatusText);
    }

    [Fact]
    public async Task Generate_MultiModAndSkippedItems_AsksOnceAndLeavesOutUncheckedMods()
    {
        _dialogs.ModPickResult = pick =>
        {
            pick.Items[0].Mods[1].IsChecked = false;
            return true;
        };
        _dialogs.Accept("From Steam");
        var page = await CreatePageAsync();
        page.WorkshopCollections.LinkText = PackLink;
        await page.WorkshopCollections.OpenCommand.ExecuteAsync(null);

        await page.WorkshopCollections.GenerateCommand.ExecuteAsync(null);

        var pick = Assert.Single(_dialogs.ModPicks);
        var item = Assert.Single(pick.Items);
        Assert.Equal("Map Pack (1002)", item.Name);
        Assert.Equal(["Maps", "MapsExtra"], item.Mods.Select(choice => choice.Mod.Id));
        Assert.All(item.Mods, choice => Assert.False(choice.IsConflicting));
        Assert.Equal(
            [
                string.Format(UiText.WorkshopCollections_SkippedFormat, "Weather (1005)", UiText.WorkshopItemState_NotSubscribed),
                string.Format(UiText.WorkshopCollections_SkippedFormat, "1004", UiText.WorkshopItemState_NotInstalled),
                string.Format(UiText.WorkshopCollections_SkippedFormat, "1006", UiText.WorkshopItemState_NotSubscribed),
            ],
            pick.Skipped);
        Assert.Equal(["Cars", "Core", "Maps"], Assert.Single(_collections.Saves).ModIds);
        Assert.Equal("From Steam", page.Collections.Selected?.Name);
    }

    [Fact]
    public async Task Generate_IncompatibleMods_AreMarkedOnBothSides()
    {
        var index = _service.Items.FindIndex(item => item.Id == 1002);
        _service.Items[index] = _service.Items[index] with
        {
            Mods = [TestMods.Mod("Maps", workshopId: "1002"), TestMods.Mod("MapsLite", workshopId: "1002", incompatible: ["Maps"])],
        };
        _dialogs.ModPickResult = _ => false;
        var vm = await OpenPackAsync();

        await vm.GenerateCommand.ExecuteAsync(null);

        var mods = Assert.Single(Assert.Single(_dialogs.ModPicks).Items).Mods;
        Assert.All(mods, choice => Assert.True(choice.IsConflicting));
        Assert.Equal(string.Format(UiText.WorkshopCollections_PickConflict, "MapsLite"), mods[0].ConflictText);
    }

    [Fact]
    public async Task Generate_PickCancelled_AsksNoNameAndSavesNothing()
    {
        _dialogs.ModPickResult = _ => false;
        var vm = await OpenPackAsync();

        await vm.GenerateCommand.ExecuteAsync(null);

        Assert.Empty(_dialogs.Prompts);
        Assert.Empty(_collections.Saves);
    }

    [Fact]
    public async Task Generate_NameTaken_ConfirmsBeforeOverwriting()
    {
        _collections.WithCollection("Survival Pack", "Old");
        _service.Collections[Pack] = [1001];
        _dialogs.Accept("Survival Pack");
        _dialogs.ConfirmAnswers.Enqueue(true);
        var vm = await OpenPackAsync();

        await vm.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(UiText.ModCollections_OverwriteTitle, Assert.Single(_dialogs.Confirms).Title);
        Assert.Equal(["Core"], _collections.IdsOf("Survival Pack"));
    }

    [Fact]
    public async Task Generate_NothingInstalled_SaysSoAndAsksNothing()
    {
        _service.Collections[Pack] = [1004, 1005];
        var vm = await OpenPackAsync();

        await vm.GenerateCommand.ExecuteAsync(null);

        Assert.Equal([UiText.WorkshopCollections_GenerateNothing], _dialogs.Errors);
        Assert.Empty(_dialogs.ModPicks);
        Assert.Empty(_dialogs.Prompts);
    }

    [Fact]
    public async Task Generate_AllModsUnchecked_SaysSoAndSavesNothing()
    {
        _service.Collections[Pack] = [1002];
        _dialogs.ModPickResult = pick =>
        {
            foreach (var choice in pick.Items.SelectMany(item => item.Mods))
            {
                choice.IsChecked = false;
            }

            return true;
        };
        var vm = await OpenPackAsync();

        await vm.GenerateCommand.ExecuteAsync(null);

        Assert.Equal([UiText.WorkshopCollections_GenerateNothing], _dialogs.Errors);
        Assert.Empty(_collections.Saves);
    }
}
