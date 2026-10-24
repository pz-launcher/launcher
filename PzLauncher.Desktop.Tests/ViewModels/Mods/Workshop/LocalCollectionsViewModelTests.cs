using System.Globalization;
using PzLauncher.Core.Common;
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
using PzLauncher.Desktop.ViewModels.Mods.Workshop;
using PzLauncher.Desktop.Services.Preferences.Records;
using PzLauncher.Desktop.ViewModels.Mods.Collections;
using static PzLauncher.Desktop.Tests.TestSupport.Fakes.TestInstalls;
using static PzLauncher.Desktop.Tests.TestSupport.Fakes.TestWorkshop;

namespace PzLauncher.Desktop.Tests.ViewModels.Mods.Workshop;

[Collection(UiTextCultureCollection.Name)]
public class LocalCollectionsViewModelTests : UiTextCultureTest
{
    private const ulong Pack = 9000;

    private static readonly Guid ServerId = new("6f1c2b0e-4d3a-4f5e-9a8b-1c2d3e4f5a6b");

    private static readonly Guid OtherId = new("0a9b8c7d-6e5f-4a3b-8c2d-1e0f9a8b7c6d");

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
    private LocalCollections _localCollections;

    public LocalCollectionsViewModelTests()
    {
        Installs = new(_detection, new FakeFolderPicker(), new FakePlatformEnvironment(), Localization);
        _localCollections = Saved(1003, 1005, 1001);
        _service
            .WithItem(Item(1001, mods: ["Core"], collections: ["Survival"]))
            .WithItem(Item(1002, mods: ["Maps"]))
            .WithItem(Item(1003, mods: ["Cars"]))
            .WithItem(Item(1004, manifest: null))
            .WithPublished(1001, "Core Library", "100")
            .WithPublished(1002, "Map Pack", "100")
            .WithPublished(1003, "Vehicles", "300")
            .WithPublished(1005, "Weather", "500")
            .WithPublished(Pack, "Survival Pack", "1");
        _service.Collections[Pack] = [1003, 1001, 1005];
    }

    private InstallsViewModel Installs { get; }

    private LocalCollections Saved(params ulong[] serverItems) =>
        LocalCollectionStore(
            _preferences,
            new SavedLocalCollection(ServerId, "Server", [.. serverItems.Select(id => new SavedWorkshopEntry(id, null))]),
            new SavedLocalCollection(OtherId, "Other", [new SavedWorkshopEntry(1002, "Map Pack")]));

    private async Task<ModsViewModel> CreatePageAsync(ModsSection section = ModsSection.LocalCollections)
    {
        _detection.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamGame));
        await Installs.ActivateCommand.ExecuteAsync(null);
        var orderWriter = new ModCollectionOrderWriter(_collections, _runState, new FakePlatformEnvironment(), new ManualTimeProvider());
        var history = History(_preferences);
        var userCollections = UserCollections(_preferences);
        var favorites = Favorites(_preferences);
        var page = new ModsViewModel(
            Installs, _collections, _overrides, new FakeModCollectionDefaultsService(), new ModCollectionApplier(_collections, _dialogs), _dialogs,
            _clipboard, _runState, orderWriter, _service, _service, _runner, _urls, history, userCollections, favorites,
            new WorkshopTitles(history, userCollections, favorites, _localCollections), _localCollections, ModpackStoreOf(_localCollections), Localization)
        {
            Section = section,
        };
        await page.ActivateCommand.ExecuteAsync(null);
        return page;
    }

    private async Task<LocalCollectionsViewModel> CreateAsync() => (await CreatePageAsync()).LocalCollections;

    private static WorkshopItemRow Row(WorkshopSectionViewModel vm, ulong id) => vm.Rows.Single(row => row.Id == id);

    private static async Task<WorkshopCollectionsViewModel> OpenPackAsync(ModsViewModel page)
    {
        var collections = page.WorkshopCollections;
        collections.LinkText = "9000";
        await collections.OpenCommand.ExecuteAsync(null);
        return collections;
    }

    private IEnumerable<ulong> SavedIds(Guid id) => _localCollections.Find(id)!.Items.Select(item => item.Id);

    private static string AddedTo(int added, string name, int skipped) =>
        string.Format(CultureInfo.CurrentCulture, UiText.LocalCollections_AddedTo, added, name, skipped);

    // The section

    [Fact]
    public async Task Show_ListsTheCollectionsAndTheFirstOnesItemsInOrderWithTitlesAndStates()
    {
        var vm = await CreateAsync();

        Assert.Equal([new LocalCollectionRow(ServerId, "Server", 3, true), new LocalCollectionRow(OtherId, "Other", 1, false)], vm.Collections);
        Assert.Equal("Server", vm.Selected?.Name);
        Assert.Equal([1003UL, 1005, 1001], vm.Rows.Select(row => row.Id));
        Assert.Equal(("Vehicles", WorkshopItemState.NeedsUpdate), (Row(vm, 1003).Title, Row(vm, 1003).Item.State));
        Assert.Equal(("Weather", WorkshopItemState.NotSubscribed), (Row(vm, 1005).Title, Row(vm, 1005).Item.State));
        Assert.Equal(1, vm.MissingCount);
        Assert.Equal(1, vm.OutdatedCount);
    }

    [Fact]
    public async Task Show_KeepsTheWebApiTitles()
    {
        await CreateAsync();

        Assert.Equal(
            [new WorkshopEntry(1003, "Vehicles"), new WorkshopEntry(1005, "Weather"), new WorkshopEntry(1001, "Core Library")],
            _localCollections.Find(ServerId)!.Items);
    }

    [Fact]
    public async Task Select_ShowsThatCollectionsItems()
    {
        var vm = await CreateAsync();

        await vm.SelectCommand.ExecuteAsync(vm.Collections[1]);

        Assert.Equal(OtherId, vm.Selected?.Id);
        Assert.True(vm.Collections[1].IsSelected);
        Assert.Equal([1002UL], vm.Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task NoCollections_ShowsTheEmptyStateWithoutTheWebApi()
    {
        _localCollections = LocalCollectionStore(_preferences);

        var vm = await CreateAsync();

        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasSelection);
        Assert.Empty(vm.Rows);
        Assert.Empty(_service.CheckRequests);
        Assert.False(vm.RenameCommand.CanExecute(null));
        Assert.False(vm.AddItemsCommand.CanExecute(null));
    }

    [Fact]
    public async Task New_CreatesItEmptyAndSelectsIt()
    {
        var vm = await CreateAsync();
        _dialogs.Accept("  Weekend ");

        await vm.NewCommand.ExecuteAsync(null);

        Assert.Equal("Weekend", vm.Selected?.Name);
        Assert.Equal(["Server", "Other", "Weekend"], vm.Collections.Select(row => row.Name));
        Assert.Empty(vm.Rows);
        Assert.True(vm.IsListEmpty);
        Assert.Equal(["Server", "Other", "Weekend"], _preferences.SavedLocalCollections[^1].Select(collection => collection.Name));
    }

    [Fact]
    public async Task NamePrompt_RejectsTakenBlankAndMultilineNames()
    {
        var vm = await CreateAsync();

        await vm.NewCommand.ExecuteAsync(null);

        var prompt = Assert.Single(_dialogs.Prompts);
        Assert.Equal(UiText.LocalCollections_NameTaken, prompt.Validate(" Other "));
        Assert.Equal(UiText.LocalCollections_NameInvalid, prompt.Validate("  "));
        Assert.Equal(UiText.LocalCollections_NameInvalid, prompt.Validate("Two\nlines"));
        Assert.Null(prompt.Validate("other"));
        Assert.Equal(2, _localCollections.All.Count);
    }

    [Fact]
    public async Task Rename_KeepsTheItemsAndTheSelection()
    {
        var vm = await CreateAsync();
        _dialogs.Accept("Server B42");

        await vm.RenameCommand.ExecuteAsync(null);

        var prompt = Assert.Single(_dialogs.Prompts);
        Assert.Equal("Server", prompt.InitialText);
        Assert.Null(prompt.Validate("Server"));
        Assert.Equal(new LocalCollectionRow(ServerId, "Server B42", 3, true), vm.Selected);
        Assert.Equal([1003UL, 1005, 1001], SavedIds(ServerId));
    }

    [Fact]
    public async Task Delete_Confirmed_RemovesItAndShowsTheNextOne()
    {
        var vm = await CreateAsync();
        _dialogs.ConfirmAnswers.Enqueue(true);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Contains("Server", Assert.Single(_dialogs.Confirms).Message, StringComparison.Ordinal);
        Assert.Null(_localCollections.Find(ServerId));
        Assert.Equal([new LocalCollectionRow(OtherId, "Other", 1, true)], vm.Collections);
        Assert.Equal([1002UL], vm.Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task Delete_Cancelled_KeepsIt()
    {
        var vm = await CreateAsync();
        var saves = _preferences.SavedLocalCollections.Count;

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(2, _localCollections.All.Count);
        Assert.Equal(saves, _preferences.SavedLocalCollections.Count);
        Assert.Equal(ServerId, vm.Selected?.Id);
    }

    // Paste

    [Fact]
    public async Task AddItems_AddsNewItemsAtTheEndAndCountsWhatItSkipped()
    {
        var vm = await CreateAsync();
        _dialogs.Accept("1001\n9000\nhttps://steamcommunity.com/sharedfiles/filedetails/?id=1002\nnot an id\n1004\n1002\n");

        await vm.AddItemsCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(_dialogs.Prompts).IsMultiline);
        Assert.Equal<IReadOnlyList<ulong>>([[Pack, 1002, 1004]], _service.ClassifyRequests);
        Assert.Equal([1003UL, 1005, 1001, 1002, 1004], SavedIds(ServerId));
        Assert.Equal([1003UL, 1005, 1001, 1002, 1004], vm.Rows.Select(row => row.Id));
        Assert.Equal("Map Pack", Row(vm, 1002).Title);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, UiText.LocalCollections_ItemsAdded, 2, 1, 1, 1), vm.StatusText);
        Assert.False(vm.IsAddingItems);
    }

    [Fact]
    public async Task AddItems_WebApiCannotSortTheIds_ShowsTheErrorAndAddsNothing()
    {
        var error = TestErrors.Create(ErrorCode.WorkshopNetworkFailed);
        _service.CollectionError = () => error;
        var vm = await CreateAsync();
        var saves = _preferences.SavedLocalCollections.Count;
        _dialogs.Accept("1002");

        await vm.AddItemsCommand.ExecuteAsync(null);

        Assert.Equal([ErrorMessages.ToUserText(error)], _dialogs.Errors);
        Assert.Equal([1003UL, 1005, 1001], SavedIds(ServerId));
        Assert.Equal(saves, _preferences.SavedLocalCollections.Count);
        Assert.Null(vm.StatusText);
    }

    [Fact]
    public async Task AddItems_OnlyListedIds_AsksNoWebApi()
    {
        var vm = await CreateAsync();
        _dialogs.Accept("1001\n1003");

        await vm.AddItemsCommand.ExecuteAsync(null);

        Assert.Empty(_service.ClassifyRequests);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, UiText.LocalCollections_ItemsAdded, 0, 2, 0, 0), vm.StatusText);
    }

    [Fact]
    public async Task AddItems_Prompt_RejectsTextWithoutAnIdAndIgnoresUnrelatedClipboardText()
    {
        _clipboard.Text = "survival pack:Core;Maps;";
        var vm = await CreateAsync();

        await vm.AddItemsCommand.ExecuteAsync(null);

        var prompt = Assert.Single(_dialogs.Prompts);
        Assert.Equal("", prompt.InitialText);
        Assert.Equal(UiText.LocalCollections_AddItemsNothing, prompt.Validate("no ids here"));
        Assert.Null(prompt.Validate("1001"));
    }

    [Fact]
    public async Task AddItems_ClipboardHoldsIds_PrefillsThem()
    {
        _clipboard.Text = " 1002\n1004 ";
        var vm = await CreateAsync();

        await vm.AddItemsCommand.ExecuteAsync(null);

        Assert.Equal("1002\n1004", Assert.Single(_dialogs.Prompts).InitialText);
    }

    // Order

    [Fact]
    public async Task MoveCommands_ReorderTheRowsAndTheSavedListWithoutAskingAgain()
    {
        var vm = await CreateAsync();
        var asked = (_service.GetRequests.Count, _service.CheckRequests.Count);

        vm.MoveToTopCommand.Execute(Row(vm, 1001));
        Assert.Equal([1001UL, 1003, 1005], vm.Rows.Select(row => row.Id));
        vm.MoveDownCommand.Execute(Row(vm, 1001));
        Assert.Equal([1003UL, 1001, 1005], vm.Rows.Select(row => row.Id));
        vm.MoveToBottomCommand.Execute(Row(vm, 1003));
        Assert.Equal([1001UL, 1005, 1003], vm.Rows.Select(row => row.Id));
        vm.MoveUpCommand.Execute(Row(vm, 1003));

        Assert.Equal([1001UL, 1003, 1005], vm.Rows.Select(row => row.Id));
        Assert.Equal([1001UL, 1003, 1005], SavedIds(ServerId));
        Assert.Equal(asked, (_service.GetRequests.Count, _service.CheckRequests.Count));
    }

    [Fact]
    public async Task Move_ByDrag_MovesByThePlacesShown()
    {
        var vm = await CreateAsync();

        Assert.True(vm.CanReorder);
        vm.Move(2, 0);

        Assert.Equal([1001UL, 1003, 1005], vm.Rows.Select(row => row.Id));
        Assert.Equal([1001UL, 1003, 1005], SavedIds(ServerId));
    }

    [Fact]
    public async Task MoveCommands_DisabledPastTheEnds()
    {
        var vm = await CreateAsync();

        Assert.False(vm.MoveUpCommand.CanExecute(Row(vm, 1003)));
        Assert.False(vm.MoveToTopCommand.CanExecute(Row(vm, 1003)));
        Assert.True(vm.MoveDownCommand.CanExecute(Row(vm, 1003)));
        Assert.False(vm.MoveDownCommand.CanExecute(Row(vm, 1001)));
        Assert.False(vm.MoveToBottomCommand.CanExecute(Row(vm, 1001)));
    }

    [Fact]
    public async Task RemoveItem_DropsItsRowWithoutAskingAgain()
    {
        var vm = await CreateAsync();
        var asked = (_service.GetRequests.Count, _service.CheckRequests.Count);

        vm.RemoveItemCommand.Execute(Row(vm, 1005));

        Assert.Equal([1003UL, 1001], vm.Rows.Select(row => row.Id));
        Assert.Equal([1003UL, 1001], SavedIds(ServerId));
        Assert.Equal(2, vm.Selected?.ItemCount);
        Assert.Equal(asked, (_service.GetRequests.Count, _service.CheckRequests.Count));
    }

    [Fact]
    public async Task Generate_FollowsTheListOrderAndSuggestsTheCollectionsName()
    {
        _localCollections = Saved(1003, 1001);
        _dialogs.Accept("Server");
        var vm = await CreateAsync();
        vm.MoveToTopCommand.Execute(Row(vm, 1001));

        await vm.GenerateCommand.ExecuteAsync(null);

        Assert.Equal("Server", Assert.Single(_dialogs.Prompts).InitialText);
        Assert.Equal(["Core", "Cars"], Assert.Single(_collections.Saves).ModIds);
    }

    // Adding from other sections

    [Fact]
    public async Task AddToLocalCollection_SubscriptionRow_AddsItToThePickedOneAtTheEnd()
    {
        _dialogs.LocalCollectionPickResult = FakeDialogService.PicksLocalCollection("Other");
        var page = await CreatePageAsync(ModsSection.WorkshopSubscriptions);
        var subscriptions = page.WorkshopSubscriptions;

        await subscriptions.AddToLocalCollectionCommand.ExecuteAsync(Row(subscriptions, 1003));

        Assert.Equal([1002UL, 1003], SavedIds(OtherId));
        Assert.Equal(AddedTo(1, "Other", 0), subscriptions.StatusText);
        Assert.Equal(["Server", "Other"], Assert.Single(_dialogs.LocalCollectionPicks).Options.Select(option => option.Name));
    }

    [Fact]
    public async Task AddToLocalCollection_SubscriptionSelection_AddsTheNewOnesAndCountsTheRest()
    {
        _dialogs.LocalCollectionPickResult = FakeDialogService.PicksLocalCollection("Other");
        var page = await CreatePageAsync(ModsSection.WorkshopSubscriptions);
        var subscriptions = page.WorkshopSubscriptions;
        Assert.False(subscriptions.AddSelectedToLocalCollectionCommand.CanExecute(null));
        Row(subscriptions, 1001).IsSelected = true;
        Row(subscriptions, 1002).IsSelected = true;

        await subscriptions.AddSelectedToLocalCollectionCommand.ExecuteAsync(null);

        Assert.Equal([1002UL, 1001], SavedIds(OtherId));
        Assert.Equal(AddedTo(1, "Other", 1), subscriptions.StatusText);
    }

    [Fact]
    public async Task AddToLocalCollection_NewOne_CreatesItWithTheItems()
    {
        _dialogs.LocalCollectionPickResult = FakeDialogService.PicksNewLocalCollection(" Favorites list ");
        var page = await CreatePageAsync(ModsSection.WorkshopSubscriptions);
        var subscriptions = page.WorkshopSubscriptions;

        await subscriptions.AddToLocalCollectionCommand.ExecuteAsync(Row(subscriptions, 1001));

        var created = _localCollections.All[^1];
        Assert.Equal("Favorites list", created.Name);
        Assert.Equal([new WorkshopEntry(1001, "Core Library")], created.Items);
        Assert.Equal(AddedTo(1, "Favorites list", 0), subscriptions.StatusText);
    }

    [Fact]
    public async Task AddToLocalCollection_Cancelled_ChangesNothing()
    {
        var page = await CreatePageAsync(ModsSection.WorkshopSubscriptions);
        var saves = _preferences.SavedLocalCollections.Count;

        await page.WorkshopSubscriptions.AddToLocalCollectionCommand.ExecuteAsync(Row(page.WorkshopSubscriptions, 1001));

        Assert.Equal(saves, _preferences.SavedLocalCollections.Count);
        Assert.Null(page.WorkshopSubscriptions.StatusText);
    }

    [Fact]
    public async Task AddToLocalCollection_OpenedCollectionAndFavoritesRows_AddToThePickedOne()
    {
        _dialogs.LocalCollectionPickResult = FakeDialogService.PicksLocalCollection("Other");
        var page = await CreatePageAsync(ModsSection.WorkshopCollections);
        var collections = await OpenPackAsync(page);
        collections.ToggleFavoriteCommand.Execute(Row(collections, 1001));

        await collections.AddToLocalCollectionCommand.ExecuteAsync(Row(collections, 1005));
        page.Section = ModsSection.WorkshopFavorites;
        await page.WorkshopFavorites.AddToLocalCollectionCommand.ExecuteAsync(Row(page.WorkshopFavorites, 1001));

        Assert.Equal([1002UL, 1005, 1001], SavedIds(OtherId));
        Assert.Equal(AddedTo(1, "Other", 0), collections.StatusText);
        Assert.Equal(AddedTo(1, "Other", 0), page.WorkshopFavorites.StatusText);
    }

    [Fact]
    public async Task AddedElsewhere_ShowsWhenTheSectionIsShownAgain()
    {
        _dialogs.LocalCollectionPickResult = FakeDialogService.PicksLocalCollection("Server");
        var page = await CreatePageAsync();
        page.Section = ModsSection.WorkshopSubscriptions;
        await page.WorkshopSubscriptions.AddToLocalCollectionCommand.ExecuteAsync(Row(page.WorkshopSubscriptions, 1002));

        page.Section = ModsSection.LocalCollections;

        Assert.Equal([1003UL, 1005, 1001, 1002], page.LocalCollections.Rows.Select(row => row.Id));
        Assert.Equal(4, page.LocalCollections.Selected?.ItemCount);
    }

    [Fact]
    public async Task SaveAsLocalCollection_OpenedCollection_SavesItsItemsInCollectionOrder()
    {
        var page = await CreatePageAsync(ModsSection.WorkshopCollections);
        await OpenPackAsync(page);
        _dialogs.Accept("Survival Pack");

        await page.WorkshopCollections.SaveAsLocalCollectionCommand.ExecuteAsync(null);

        var prompt = Assert.Single(_dialogs.Prompts);
        Assert.Equal("Survival Pack", prompt.InitialText);
        Assert.Equal(UiText.LocalCollections_NameTaken, prompt.Validate("Server"));
        var saved = _localCollections.All[^1];
        Assert.Equal("Survival Pack", saved.Name);
        Assert.Equal(
            [new WorkshopEntry(1003, "Vehicles"), new WorkshopEntry(1001, "Core Library"), new WorkshopEntry(1005, "Weather")],
            saved.Items);
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, UiText.LocalCollections_SavedAs, 3, "Survival Pack"),
            page.WorkshopCollections.StatusText);
    }

    [Fact]
    public async Task SaveAsLocalCollection_NoneOpen_IsDisabled()
    {
        var page = await CreatePageAsync(ModsSection.WorkshopCollections);

        Assert.False(page.WorkshopCollections.SaveAsLocalCollectionCommand.CanExecute(null));
    }

    // The pick dialog

    [Fact]
    public void Pick_PreselectsTheFirstCollection()
    {
        var pick = new LocalCollectionPickViewModel(_localCollections);

        Assert.Equal([true, false], pick.Options.Select(option => option.IsChecked));
        Assert.False(pick.IsNewChecked);
        Assert.True(pick.AcceptCommand.CanExecute(null));
    }

    [Fact]
    public void Pick_NoCollections_StartsOnANewOneThatNeedsAName()
    {
        var pick = new LocalCollectionPickViewModel(LocalCollectionStore());

        Assert.False(pick.HasOptions);
        Assert.True(pick.IsNewChecked);
        Assert.Null(pick.NewNameError);
        Assert.False(pick.AcceptCommand.CanExecute(null));
    }

    [Fact]
    public void Pick_TypingAName_PicksANewOneAndChecksTheName()
    {
        var pick = new LocalCollectionPickViewModel(_localCollections);

        pick.NewName = "Other";

        Assert.True(pick.IsNewChecked);
        Assert.All(pick.Options, option => Assert.False(option.IsChecked));
        Assert.Equal(UiText.LocalCollections_NameTaken, pick.NewNameError);
        Assert.False(pick.AcceptCommand.CanExecute(null));

        pick.NewName = "Weekend";
        pick.AcceptCommand.Execute(null);

        Assert.Equal(new LocalCollectionTarget.New("Weekend"), pick.Target);
    }

    [Fact]
    public void Pick_CheckingAnExistingOne_LeavesTheNewOne()
    {
        var pick = new LocalCollectionPickViewModel(_localCollections) { NewName = "Weekend" };

        pick.Options[1].IsChecked = true;
        pick.AcceptCommand.Execute(null);

        Assert.False(pick.IsNewChecked);
        Assert.False(pick.Options[0].IsChecked);
        Assert.Equal(new LocalCollectionTarget.Existing(OtherId), pick.Target);
    }
}
