using System.Globalization;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
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
public class WorkshopCollectionsViewModelTests : UiTextCultureTest
{
    private const ulong Pack = 9000;

    private const string PackLink = "https://steamcommunity.com/sharedfiles/filedetails/?id=9000";

    private const uint Account = 12345678;

    private static readonly WorkshopUserCollections SteamCollections = new(
        Account,
        [new WorkshopCollectionSummary(3623026433, "Favorite Pack", 201), new WorkshopCollectionSummary(2690796635, null, 71)],
        [new WorkshopCollectionSummary(3815132834, "my", 214)]);

    private static readonly SavedWorkshopUserCollections SavedCollections = new(
        new DateTimeOffset(2026, 10, 1, 12, 30, 0, TimeSpan.Zero),
        new WorkshopUserCollections(Account, [new WorkshopCollectionSummary(5001, "Saved Favorite", 12)], []));

    private readonly FakeInstallDetectionService _detection = new();
    private readonly FakeModCollectionService _collections = new();
    private readonly FakeInstallOverridesService _overrides = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeUrlLauncher _urls = new();
    private readonly FakeWorkshopService _service = new();
    private readonly FakeWorkshopActionRunner _runner = new();
    private readonly FakePreferenceStore _preferences = new();
    private readonly GameRunState _runState = new();
    private readonly ManualTimeProvider _time = new();
    private readonly FakeClipboardService _clipboard = new();
    private WorkshopFavorites _favorites;
    private SavedWorkshopUserCollections? _savedUserCollections;

    public WorkshopCollectionsViewModelTests()
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

    private async Task<ModsViewModel> CreatePageAsync(params SavedWorkshopEntry[] saved)
    {
        var page = await CreateInactivePageAsync(saved);
        await page.ActivateCommand.ExecuteAsync(null);
        return page;
    }

    private async Task<ModsViewModel> CreateInactivePageAsync(params SavedWorkshopEntry[] saved)
    {
        _detection.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamGame));
        await Installs.ActivateCommand.ExecuteAsync(null);
        var orderWriter = new ModCollectionOrderWriter(_collections, _runState, new FakePlatformEnvironment(), new ManualTimeProvider());
        return new ModsViewModel(
            Installs, _collections, _overrides, new FakeModCollectionDefaultsService(), new ModCollectionApplier(_collections, _dialogs), _dialogs,
            _clipboard, _runState, orderWriter, _service, _runner, _urls, History(_preferences, saved),
            UserCollections(_preferences, _savedUserCollections, _time), _favorites, Localization)
        {
            Section = ModsSection.WorkshopCollections,
        };
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
        var vm = await CreatePageAsync(new SavedWorkshopEntry(9100, "Vehicles Pack"), new SavedWorkshopEntry(Pack, "Old title"));

        vm.WorkshopCollections.LinkText = PackLink;
        await vm.WorkshopCollections.OpenCommand.ExecuteAsync(null);

        Assert.Equal([new SavedWorkshopEntry(Pack, "Survival Pack"), new SavedWorkshopEntry(9100, "Vehicles Pack")], _preferences.SavedWorkshopCollections[^1]);
        Assert.Equal(
            [new RememberedWorkshopCollectionRow(Pack, "Survival Pack", IsOpen: true), new RememberedWorkshopCollectionRow(9100, "Vehicles Pack", IsOpen: false)],
            vm.WorkshopCollections.Remembered);
    }

    [Fact]
    public async Task OpenRemembered_OpensThatCollection()
    {
        var vm = (await CreatePageAsync(new SavedWorkshopEntry(Pack, null))).WorkshopCollections;
        Assert.Equal("9000", vm.Remembered[0].Title);

        await vm.OpenRememberedCommand.ExecuteAsync(vm.Remembered[0]);

        Assert.Equal("Survival Pack", vm.Title);
        Assert.Equal("Survival Pack", vm.Remembered[0].Title);
    }

    [Fact]
    public async Task Forget_RemovesTheCollectionFromTheSavedList()
    {
        var vm = (await CreatePageAsync(new SavedWorkshopEntry(Pack, "Survival Pack"), new SavedWorkshopEntry(9100, "Vehicles Pack"))).WorkshopCollections;

        vm.ForgetCommand.Execute(vm.Remembered[0]);

        Assert.Equal([9100UL], vm.Remembered.Select(row => row.Id));
        Assert.Equal([new SavedWorkshopEntry(9100, "Vehicles Pack")], _preferences.SavedWorkshopCollections[^1]);
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

    // The Steam account's collections

    private static string UpdatedText(DateTimeOffset at) =>
        string.Format(CultureInfo.CurrentCulture, UiText.WorkshopCollections_UpdatedFormat, at.LocalDateTime.ToString("g", CultureInfo.CurrentCulture));

    private static WorkshopUserCollectionRow UserRow(ulong id, string title, uint count, bool isOpen = false, bool isFavorite = false) =>
        new(id, title, count, isOpen, isFavorite);

    [Fact]
    public async Task FirstShow_ListsTheFavoritedAndPublishedCollectionsSteamGives()
    {
        _runner.Collections = SteamCollections;

        var vm = await CreateAsync();

        Assert.Equal([SteamGame.RootPath], _runner.ListRuns);
        Assert.Equal([UserRow(3623026433, "Favorite Pack", 201), UserRow(2690796635, "2690796635", 71)], vm.Favorited);
        Assert.Equal([UserRow(3815132834, "my", 214)], vm.Published);
        Assert.False(vm.IsFavoritedEmpty);
        Assert.Equal(UpdatedText(_time.GetUtcNow()), vm.UserCollectionsUpdatedText);
        Assert.Null(vm.UserCollectionsErrorText);
        Assert.False(vm.IsFetchingUserCollections);
    }

    [Fact]
    public async Task Fetched_IsSavedWithItsAccountAndTime()
    {
        _runner.Collections = SteamCollections;

        await CreateAsync();

        Assert.Equal([new SavedWorkshopUserCollections(_time.GetUtcNow(), SteamCollections)], _preferences.SavedWorkshopUserCollections);
    }

    [Fact]
    public async Task SteamListsNone_GroupsSayTheyAreEmpty()
    {
        var vm = await CreateAsync();

        Assert.True(vm.IsFavoritedEmpty);
        Assert.True(vm.IsPublishedEmpty);
    }

    [Fact]
    public async Task SectionShownAgain_DoesNotAskSteamAgain()
    {
        var page = await CreatePageAsync();

        page.Section = ModsSection.Mods;
        page.Section = ModsSection.WorkshopCollections;

        Assert.Single(_runner.ListRuns);
    }

    [Fact]
    public async Task RefreshUserCollections_AsksSteamAgainAndShowsTheNewLists()
    {
        var vm = await CreateAsync();
        _runner.Collections = SteamCollections;
        _time.Advance(TimeSpan.FromMinutes(5));

        await vm.RefreshUserCollectionsCommand.ExecuteAsync(null);

        Assert.Equal(2, _runner.ListRuns.Count);
        Assert.Equal(3815132834UL, Assert.Single(vm.Published).Id);
        Assert.Equal(UpdatedText(_time.GetUtcNow()), vm.UserCollectionsUpdatedText);
    }

    [Fact]
    public async Task SavedForTheSameAccount_ShowsAtOnceThenRefreshesOnce()
    {
        _savedUserCollections = SavedCollections;
        _runner.Collections = SteamCollections;
        _runner.ListGate = new TaskCompletionSource();
        var page = await CreateInactivePageAsync();
        var vm = page.WorkshopCollections;

        var activating = page.ActivateCommand.ExecuteAsync(null);

        Assert.Equal([UserRow(5001, "Saved Favorite", 12)], vm.Favorited);
        Assert.Equal(UpdatedText(SavedCollections.FetchedAt), vm.UserCollectionsUpdatedText);
        Assert.True(vm.IsFetchingUserCollections);
        Assert.False(vm.RefreshUserCollectionsCommand.CanExecute(null));
        _runner.ListGate.SetResult();
        await activating;
        Assert.Equal(3623026433UL, vm.Favorited[0].Id);
        Assert.Single(_runner.ListRuns);
        Assert.True(vm.RefreshUserCollectionsCommand.CanExecute(null));
    }

    [Fact]
    public async Task SavedForAnotherAccount_IsNotShown()
    {
        _savedUserCollections = SavedCollections with { Collections = SavedCollections.Collections with { AccountId = 99 } };
        _runState.IsGameRunning = true;

        var vm = await CreateAsync();

        Assert.Empty(vm.Favorited);
        Assert.False(vm.IsFavoritedEmpty);
        Assert.Null(vm.UserCollectionsUpdatedText);
    }

    [Fact]
    public async Task AccountUnreadable_SavedIsNotShownButSteamIsStillAsked()
    {
        _savedUserCollections = SavedCollections;
        _service.AccountError = () => TestErrors.Create(ErrorCode.WorkshopSubscriptionsUnreadable);
        _runner.ListError = () => TestErrors.Create(ErrorCode.WorkshopSteamNotRunning);

        var vm = await CreateAsync();

        Assert.Empty(vm.Favorited);
        Assert.Null(vm.UserCollectionsUpdatedText);
        Assert.Single(_runner.ListRuns);
    }

    [Fact]
    public async Task GameRuns_AsksNothingAndTheSkipDoesNotUseUpTheSessionsFetch()
    {
        _runState.IsGameRunning = true;
        var page = await CreatePageAsync();

        Assert.Empty(_runner.ListRuns);
        Assert.False(page.WorkshopCollections.RefreshUserCollectionsCommand.CanExecute(null));

        _runState.IsGameRunning = false;
        Assert.True(page.WorkshopCollections.RefreshUserCollectionsCommand.CanExecute(null));
        page.Section = ModsSection.Mods;
        page.Section = ModsSection.WorkshopCollections;

        Assert.Single(_runner.ListRuns);
    }

    [Fact]
    public async Task ActionRuns_RefreshUserCollectionsIsDisabledUntilItEnds()
    {
        _runner.Gate = new TaskCompletionSource();
        var vm = await OpenPackAsync();

        var running = vm.UpdateOutdatedCommand.ExecuteAsync(null);

        Assert.False(vm.RefreshUserCollectionsCommand.CanExecute(null));
        _runner.Gate.SetResult();
        await running;
        Assert.True(vm.RefreshUserCollectionsCommand.CanExecute(null));
    }

    [Fact]
    public async Task SteamNotRunning_KeepsTheSavedListsAndSaysSoWithoutADialog()
    {
        _savedUserCollections = SavedCollections;
        _runner.ListError = () => TestErrors.Create(ErrorCode.WorkshopSteamNotRunning);

        var vm = await CreateAsync();

        Assert.Equal(UiText.Error_WorkshopSteamNotRunning, vm.UserCollectionsErrorText);
        Assert.Equal([UserRow(5001, "Saved Favorite", 12)], vm.Favorited);
        Assert.Equal(UpdatedText(SavedCollections.FetchedAt), vm.UserCollectionsUpdatedText);
        Assert.Empty(_dialogs.Errors);
        Assert.Empty(_preferences.SavedWorkshopUserCollections);
    }

    [Fact]
    public async Task SteamNotRunning_DoesNotUseUpTheSessionsFetch()
    {
        _runner.ListError = () => TestErrors.Create(ErrorCode.WorkshopSteamNotRunning);
        var page = await CreatePageAsync();
        _runner.ListError = () => null;
        _runner.Collections = SteamCollections;

        page.Section = ModsSection.Mods;
        page.Section = ModsSection.WorkshopCollections;
        page.Section = ModsSection.Mods;
        page.Section = ModsSection.WorkshopCollections;

        Assert.Equal(2, _runner.ListRuns.Count);
        Assert.Equal(2, page.WorkshopCollections.Favorited.Count);
    }

    [Fact]
    public async Task OtherFetchFailure_UsesUpTheSessionsFetch()
    {
        _runner.ListError = () => TestErrors.Create(ErrorCode.WorkshopSteamInitFailed);
        var page = await CreatePageAsync();

        page.Section = ModsSection.Mods;
        page.Section = ModsSection.WorkshopCollections;

        Assert.Single(_runner.ListRuns);
    }

    [Theory]
    [InlineData(ErrorCode.WorkshopSteamInitFailed)]
    [InlineData(ErrorCode.WorkshopHelperTimedOut)]
    [InlineData(ErrorCode.WorkshopCollectionsQueryFailed)]
    public async Task FetchFails_ShowsTheReasonAsAStatusLine(ErrorCode code)
    {
        var error = TestErrors.Create(code);
        _runner.ListError = () => error;

        var vm = await CreateAsync();

        Assert.Equal(ErrorMessages.ToUserText(error), vm.UserCollectionsErrorText);
        Assert.Empty(_dialogs.Errors);
    }

    [Fact]
    public async Task FetchFailsThenSucceeds_ClearsTheReason()
    {
        _runner.ListError = () => TestErrors.Create(ErrorCode.WorkshopSteamNotRunning);
        var vm = await CreateAsync();
        _runner.ListError = () => null;
        _runner.Collections = SteamCollections;

        await vm.RefreshUserCollectionsCommand.ExecuteAsync(null);

        Assert.Null(vm.UserCollectionsErrorText);
        Assert.Equal(2, vm.Favorited.Count);
    }

    [Fact]
    public async Task OpenUserCollection_OpensItAndRemembersIt()
    {
        _runner.Collections = new WorkshopUserCollections(Account, [new WorkshopCollectionSummary(Pack, "Survival Pack", 6)], []);
        var vm = await CreateAsync();

        await vm.OpenUserCollectionCommand.ExecuteAsync(vm.Favorited[0]);

        Assert.Equal("Survival Pack", vm.Title);
        Assert.Equal(Pack, Assert.Single(vm.Remembered).Id);
        Assert.Equal([UserRow(Pack, "Survival Pack", 6, isOpen: true)], vm.Favorited);
        Assert.Single(_runner.ListRuns);
    }

    // Local favorites: collections

    private const string SharedText = "#collections\n9000\n#items\n1001\n1005\n";

    private static string Imported(int collections, int items, int skipped) =>
        string.Format(CultureInfo.CurrentCulture, UiText.WorkshopFavorites_Imported, collections, items, skipped);

    [Fact]
    public async Task ToggleOpenCollectionFavorite_ListsItUnderLocalFavoritesWithItsTitle()
    {
        var vm = await OpenPackAsync();

        vm.ToggleOpenCollectionFavoriteCommand.Execute(null);

        Assert.True(vm.IsOpenCollectionFavorite);
        Assert.Equal([new RememberedWorkshopCollectionRow(Pack, "Survival Pack", IsOpen: true, IsFavorite: true)], vm.FavoriteCollections);
        Assert.True(Assert.Single(vm.Remembered).IsFavorite);
        Assert.Equal([new SavedWorkshopEntry(Pack, "Survival Pack")], _preferences.SavedWorkshopFavorites[^1].Collections);
    }

    [Fact]
    public async Task FavoriteCollection_SavedEarlier_OpensFromLocalFavoritesAndRefreshesItsTitle()
    {
        _favorites = Favorites(_preferences, new SavedWorkshopFavorites([new SavedWorkshopEntry(Pack, null)], []));
        var vm = await CreateAsync();
        Assert.Equal("9000", vm.FavoriteCollections[0].Title);

        await vm.OpenRememberedCommand.ExecuteAsync(vm.FavoriteCollections[0]);

        Assert.Equal("Survival Pack", vm.Title);
        Assert.True(vm.IsOpenCollectionFavorite);
        Assert.Equal([new RememberedWorkshopCollectionRow(Pack, "Survival Pack", IsOpen: true, IsFavorite: true)], vm.FavoriteCollections);
        Assert.Equal(Pack, Assert.Single(vm.Remembered).Id);
    }

    [Fact]
    public async Task ToggleCollectionFavorite_Starred_RemovesItFromLocalFavorites()
    {
        var vm = await OpenPackAsync();
        vm.ToggleOpenCollectionFavoriteCommand.Execute(null);

        vm.ToggleCollectionFavoriteCommand.Execute(vm.FavoriteCollections[0]);

        Assert.Empty(vm.FavoriteCollections);
        Assert.False(vm.IsOpenCollectionFavorite);
        Assert.False(Assert.Single(vm.Remembered).IsFavorite);
        Assert.Empty(_preferences.SavedWorkshopFavorites[^1].Collections);
    }

    [Fact]
    public async Task ToggleUserCollectionFavorite_StarsTheSteamCollectionsWithTheirTitles()
    {
        _runner.Collections = SteamCollections;
        var vm = await CreateAsync();

        vm.ToggleUserCollectionFavoriteCommand.Execute(vm.Favorited[0]);
        vm.ToggleUserCollectionFavoriteCommand.Execute(vm.Favorited[1]);

        Assert.Equal([new SavedWorkshopEntry(2690796635, null), new SavedWorkshopEntry(3623026433, "Favorite Pack")], _favorites.Collections);
        Assert.Equal(
            [UserRow(3623026433, "Favorite Pack", 201, isFavorite: true), UserRow(2690796635, "2690796635", 71, isFavorite: true)],
            vm.Favorited);
    }

    [Fact]
    public async Task Stars_WhileAGameRuns_ToggleWithoutSteam()
    {
        _runner.Collections = SteamCollections;
        var vm = await OpenPackAsync();
        var lists = _runner.ListRuns.Count;
        _runState.IsGameRunning = true;

        Assert.True(vm.ToggleOpenCollectionFavoriteCommand.CanExecute(null));
        vm.ToggleOpenCollectionFavoriteCommand.Execute(null);
        vm.ToggleFavoriteCommand.Execute(Row(vm, 1005));
        vm.ToggleUserCollectionFavoriteCommand.Execute(vm.Favorited[0]);
        vm.ToggleCollectionFavoriteCommand.Execute(vm.Remembered[0]);

        Assert.Equal([3623026433UL], _favorites.Collections.Select(entry => entry.Id));
        Assert.Equal([1005UL], _favorites.Items.Select(entry => entry.Id));
        Assert.True(Row(vm, 1005).IsFavorite);
        Assert.Empty(_runner.Runs);
        Assert.Equal(lists, _runner.ListRuns.Count);
    }

    // Local favorites: starred items

    [Fact]
    public async Task FavoriteItems_StarredInBothSections_ListsThemWithTitlesAndStates()
    {
        var page = await CreatePageAsync();
        page.Section = ModsSection.WorkshopSubscriptions;
        var subscriptions = page.WorkshopSubscriptions;
        subscriptions.ToggleFavoriteCommand.Execute(subscriptions.VisibleRows.Single(row => row.Id == 1003));
        page.Section = ModsSection.WorkshopCollections;
        var vm = page.WorkshopCollections;
        vm.LinkText = PackLink;
        await vm.OpenCommand.ExecuteAsync(null);
        Assert.True(Row(vm, 1003).IsFavorite);
        vm.ToggleFavoriteCommand.Execute(Row(vm, 1005));

        await vm.OpenFavoriteItemsCommand.ExecuteAsync(null);

        Assert.True(vm.IsFavoriteItemsView);
        Assert.Equal((UiText.WorkshopFavorites_Items, (string?)null), (vm.Title, vm.IdText));
        Assert.Equal([1005UL, 1003], vm.Rows.Select(row => row.Id));
        Assert.Equal(("Weather", WorkshopItemState.NotSubscribed), (Row(vm, 1005).Title, Row(vm, 1005).Item.State));
        Assert.Equal(("Vehicles", WorkshopItemState.NeedsUpdate), (Row(vm, 1003).Title, Row(vm, 1003).Item.State));
        Assert.All(vm.Rows, row => Assert.True(row.IsFavorite));
        Assert.DoesNotContain(vm.Remembered, row => row.IsOpen);
        Assert.Equal(2, vm.FavoriteItemCount);
    }

    [Fact]
    public async Task FavoriteItems_SubscribeMissing_SubscribesOnlyTheUnsubscribedOnes()
    {
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1003, null);
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1005, null);
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1006, null);
        _runner.Succeeded = (_, id) => _service.Items.Add(Item(id, manifest: null));
        var vm = await CreateAsync();
        await vm.OpenFavoriteItemsCommand.ExecuteAsync(null);

        await vm.SubscribeMissingCommand.ExecuteAsync(null);

        var run = Assert.Single(_runner.Runs);
        Assert.Equal(WorkshopAction.Subscribe, run.Request.Action);
        Assert.Equal([1006UL, 1005], run.Request.Ids);
        Assert.True(vm.IsFavoriteItemsView);
        Assert.Equal(0, vm.MissingCount);
    }

    [Fact]
    public async Task FavoriteItems_OfferNoUnsubscribeAllNorCollectionPage()
    {
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1001, null);
        var vm = await CreateAsync();

        await vm.OpenFavoriteItemsCommand.ExecuteAsync(null);

        Assert.False(vm.ShowsSteamCollection);
        Assert.Equal(1, vm.SubscribedCount);
        Assert.True(vm.CanRunActions);
        Assert.False(vm.UnsubscribeAllCommand.CanExecute(null));
        Assert.False(vm.OpenCollectionInSteamCommand.CanExecute(null));
        Assert.False(vm.ToggleOpenCollectionFavoriteCommand.CanExecute(null));
    }

    [Fact]
    public async Task ShowsSteamCollection_SwitchingToAndFromTheStarredItems_Notifies()
    {
        var vm = await OpenPackAsync();
        var notified = 0;
        vm.PropertyChanged += (_, e) => notified += e.PropertyName == nameof(vm.ShowsSteamCollection) ? 1 : 0;

        await vm.OpenFavoriteItemsCommand.ExecuteAsync(null);
        var toStarred = (vm.ShowsSteamCollection, notified > 0);
        notified = 0;
        await vm.OpenRememberedCommand.ExecuteAsync(vm.Remembered[0]);

        Assert.Equal((false, true), toStarred);
        Assert.Equal((true, true), (vm.ShowsSteamCollection, notified > 0));
    }

    [Fact]
    public async Task FavoriteItems_Unstarred_RowLeavesWithoutAskingAgain()
    {
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1001, null);
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1005, null);
        var vm = await CreateAsync();
        await vm.OpenFavoriteItemsCommand.ExecuteAsync(null);
        var asked = (_service.GetRequests.Count, _service.CheckRequests.Count);

        vm.ToggleFavoriteCommand.Execute(Row(vm, 1005));

        Assert.Equal([1001UL], vm.Rows.Select(row => row.Id));
        Assert.Equal(1, vm.FavoriteItemCount);
        Assert.Equal(asked, (_service.GetRequests.Count, _service.CheckRequests.Count));
    }

    [Fact]
    public async Task FavoriteItems_NoneStarred_SaysHowToStarWithoutTheWebApi()
    {
        var vm = await CreateAsync();

        await vm.OpenFavoriteItemsCommand.ExecuteAsync(null);

        Assert.True(vm.IsFavoriteItemsEmpty);
        Assert.False(vm.IsCollectionEmpty);
        Assert.Empty(_service.CheckRequests);
    }

    [Fact]
    public async Task FavoriteItems_TitleLookupFails_ListsThemByIdWithAWarning()
    {
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1005, null);
        var error = TestErrors.Create(ErrorCode.WorkshopNetworkFailed);
        _service.CheckError = () => error;
        var vm = await CreateAsync();

        await vm.OpenFavoriteItemsCommand.ExecuteAsync(null);

        Assert.Equal("1005", Row(vm, 1005).Title);
        Assert.Equal([ErrorMessages.ToUserText(error)], vm.Warnings);
    }

    [Fact]
    public async Task FavoriteItems_Generate_SuggestsTheLocalFavoritesName()
    {
        _favorites.Toggle(WorkshopFavoriteKind.Item, 1001, null);
        _dialogs.Accept("Mine");
        var vm = await CreateAsync();
        await vm.OpenFavoriteItemsCommand.ExecuteAsync(null);

        await vm.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(UiText.WorkshopFavorites_CollectionName, Assert.Single(_dialogs.Prompts).InitialText);
        Assert.Equal(["Core"], Assert.Single(_collections.Saves).ModIds);
    }

    // Local favorites: share and import

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
        Assert.Equal([new SavedWorkshopEntry(Pack, "Survival Pack")], _favorites.Collections);
        Assert.Equal([new SavedWorkshopEntry(1001, "Core Library"), new SavedWorkshopEntry(1005, "Weather")], _favorites.Items);
        Assert.Equal([new RememberedWorkshopCollectionRow(Pack, "Survival Pack", IsOpen: false, IsFavorite: true)], vm.FavoriteCollections);
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

        Assert.Equal([Pack, 1005UL], _service.CollectionRequests);
        Assert.Equal([new SavedWorkshopEntry(Pack, "Survival Pack")], _favorites.Collections);
        Assert.Equal([new SavedWorkshopEntry(1005, "Weather")], _favorites.Items);
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

        Assert.Empty(_service.CollectionRequests);
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
    public async Task ImportFavorites_WhileStarredItemsAreShown_ListsTheNewOnes()
    {
        var vm = await CreateAsync();
        await vm.OpenFavoriteItemsCommand.ExecuteAsync(null);
        _dialogs.Accept("#items\n1005");

        await vm.ImportFavoritesCommand.ExecuteAsync(null);

        Assert.Equal([1005UL], vm.Rows.Select(row => row.Id));
        Assert.Equal(WorkshopItemState.NotSubscribed, Row(vm, 1005).Item.State);
    }
}
