using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.ViewModels.Mods;
using static PzLauncher.Desktop.Tests.TestSupport.TestInstalls;
using static PzLauncher.Desktop.Tests.TestSupport.TestMods;

namespace PzLauncher.Desktop.Tests.ViewModels.Mods;

[Collection(UiTextCultureCollection.Name)]
public class ModsViewModelTests : UiTextCultureTest
{
    private static readonly TimeSpan OrderSaveDelay = TimeSpan.FromMilliseconds(300);

    private readonly FakeInstallDetectionService _detection = new();
    private readonly FakeModCollectionService _service = new();
    private readonly FakeInstallOverridesService _overrides = new();
    private readonly FakeModCollectionDefaultsService _defaults = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeClipboardService _clipboard = new();
    private readonly GameRunState _runState = new();
    private readonly ManualTimeProvider _time = new();
    private readonly ModCollectionOrderWriter _orderWriter;

    public ModsViewModelTests()
    {
        _orderWriter = new ModCollectionOrderWriter(_service, _runState, new FakePlatformEnvironment(), _time);
        Installs = new(_detection, new FakeFolderPicker(), new FakePlatformEnvironment(), Localization);
        _service
            .WithMods(
                Mod("Core", name: "Core Library", workshopId: "1001"),
                Mod("Maps", name: "Map Pack", workshopId: "1002", require: ["Core"]),
                Mod("Cars", name: "Vehicles", workshopId: "1003"),
                Mod("Tweaks", name: "Tweaks", source: InstalledModSource.Local))
            .WithCollection("Survival", "Maps", "Core", "Gone")
            .WithCollection("Driving", "Cars");
        _service.ActiveIds = ["Cars"];
    }

    private InstallsViewModel Installs { get; }

    private async Task<ModsViewModel> CreateActivatedAsync(params PzInstall[] installs)
    {
        _detection.ScanResults.Enqueue(FakeInstallDetectionService.Found(installs.Length == 0 ? [SteamGame] : installs));
        await Installs.ActivateCommand.ExecuteAsync(null);
        var vm = new ModsViewModel(
            Installs, _service, _overrides, _defaults, new ModCollectionApplier(_service, _dialogs), _dialogs, _clipboard, _runState, _orderWriter, Localization);
        await vm.ActivateCommand.ExecuteAsync(null);
        return vm;
    }

    private static void Select(ModsViewModel vm, string name) =>
        vm.Collections.SelectCommand.Execute(vm.Collections.Rows.Single(row => row.Name == name));

    // Page

    [Fact]
    public async Task Activate_ReadsTheInstallInUseWithItsGameArgs()
    {
        _overrides.Set(SteamGame.RootPath, GameLaunchOverrides.Empty with { ExtraGameArgs = @"-cachedir=D:\pz" });

        var vm = await CreateActivatedAsync();

        Assert.Equal([(SteamGame.RootPath, @"-cachedir=D:\pz")], _service.GetRequests);
        Assert.NotNull(vm.State);
        Assert.True(vm.CanWrite);
    }

    [Fact]
    public async Task Activate_EveryVisit_ReadsTheFilesAgain()
    {
        var vm = await CreateActivatedAsync();

        await vm.ActivateCommand.ExecuteAsync(null);

        Assert.Equal(2, _service.GetRequests.Count);
    }

    [Fact]
    public async Task DedicatedServerInUse_ShowsNoInstallAndAllowsNoWrites()
    {
        var vm = await CreateActivatedAsync(SteamServer);

        Assert.True(vm.HasNoGameInstall);
        Assert.Null(vm.State);
        Assert.Empty(_service.GetRequests);
        Assert.False(vm.Collections.NewCommand.CanExecute(null));
    }

    [Fact]
    public async Task InstallInUseChanges_ReadsTheNewInstall()
    {
        var vm = await CreateActivatedAsync(SteamGame, ManualGame);

        Installs.Use(ManualGame.RootPath);
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Contains(_service.GetRequests, request => request.Root == ManualGame.RootPath);
        Assert.Equal(ManualGame, vm.Install);
    }

    [Fact]
    public async Task ReadFails_ShowsTheErrorAndNoState()
    {
        var error = TestErrors.Create(ErrorCode.ModCollectionCacheFolderUnresolved, SteamGame.RootPath);
        _service.GetError = () => error;

        var vm = await CreateActivatedAsync();

        Assert.Equal(ErrorMessages.ToUserText(error), vm.ErrorText);
        Assert.Null(vm.State);
        Assert.False(vm.CanWrite);
    }

    // Installed mods

    [Fact]
    public async Task InstalledMods_ShowEveryModByNameWithEnabledAndCollections()
    {
        var vm = await CreateActivatedAsync();

        var rows = vm.Mods.VisibleRows;
        Assert.Equal(["Core Library", "Map Pack", "Tweaks", "Vehicles"], rows.Select(row => row.Mod.Name));
        Assert.Equal(["Cars"], rows.Where(row => row.IsEnabled).Select(row => row.Mod.Id));
        Assert.Equal("Survival", rows.Single(row => row.Mod.Id == "Core").CollectionNames);
        Assert.Equal("", rows.Single(row => row.Mod.Id == "Tweaks").CollectionNames);
    }

    [Theory]
    [InlineData("map", "Maps")]
    [InlineData("CARS", "Cars")]
    [InlineData("1001", "Core")]
    public async Task InstalledMods_Search_MatchesNameIdOrWorkshopId(string search, string expected)
    {
        var vm = await CreateActivatedAsync();

        vm.Mods.SearchText = search;

        Assert.Equal([expected], vm.Mods.VisibleRows.Select(row => row.Mod.Id));
    }

    [Theory]
    [InlineData(InstalledModsFilter.All, 4)]
    [InlineData(InstalledModsFilter.Enabled, 1)]
    [InlineData(InstalledModsFilter.NotEnabled, 3)]
    public async Task InstalledMods_Filter_NarrowsByEnabledList(InstalledModsFilter filter, int count)
    {
        var vm = await CreateActivatedAsync();

        vm.Mods.Filter = filter;

        Assert.Equal(count, vm.Mods.VisibleRows.Count);
    }

    [Fact]
    public async Task InstalledMods_SearchMatchesNothing_IsFilterEmpty()
    {
        var vm = await CreateActivatedAsync();

        vm.Mods.SearchText = "no such mod";

        Assert.True(vm.Mods.IsFilterEmpty);
        Assert.False(vm.Mods.IsEmpty);
    }

    [Fact]
    public async Task InstalledMods_AddToCollection_AppendsTheNewSelectedModsAndNeverTouchesTheEnabledList()
    {
        var vm = await CreateActivatedAsync();
        foreach (var row in vm.Mods.VisibleRows.Where(row => row.Mod.Id is "Tweaks" or "Core" or "Cars"))
        {
            row.IsSelected = true;
        }

        Assert.True(vm.Mods.CanAddToCollection);
        await vm.Mods.AddTargets.Single(target => target.Name == "Survival").AddCommand.ExecuteAsync(null);

        Assert.Equal(["Maps", "Core", "Gone", "Tweaks", "Cars"], _service.IdsOf("Survival"));
        Assert.Equal(0, vm.Mods.SelectedCount);
        Assert.Empty(_service.Applies);
        Assert.Equal(["Cars"], _service.ActiveIds);
    }

    [Fact]
    public async Task InstalledMods_Selection_SurvivesAReload()
    {
        var vm = await CreateActivatedAsync();
        vm.Mods.VisibleRows.Single(row => row.Mod.Id == "Tweaks").IsSelected = true;

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(vm.Mods.VisibleRows.Single(row => row.Mod.Id == "Tweaks").IsSelected);
        Assert.Equal(1, vm.Mods.SelectedCount);
    }

    [Fact]
    public async Task InstalledMods_GameRunning_CannotAddToCollection()
    {
        var vm = await CreateActivatedAsync();
        vm.Mods.VisibleRows[0].IsSelected = true;

        _runState.IsGameRunning = true;
        await vm.Mods.AddTargets[0].AddCommand.ExecuteAsync(null);

        Assert.False(vm.Mods.CanAddToCollection);
        Assert.Empty(_service.Saves);
    }

    // Installed mods: sorting

    private async Task<ModsViewModel> CreateWithSortModsAsync()
    {
        _service.WithMods(
            Mod("Atlas", name: "Map Pack", workshopId: "999", source: InstalledModSource.StagedWorkshop),
            Mod("draft", name: "Alpha", workshopId: "draft-item", source: InstalledModSource.StagedWorkshop));
        return await CreateActivatedAsync();
    }

    private static string VisibleIds(ModsViewModel vm) => string.Join(",", vm.Mods.VisibleRows.Select(row => row.Mod.Id));

    [Fact]
    public async Task InstalledMods_Sort_DefaultsToNameAscending()
    {
        var vm = await CreateWithSortModsAsync();

        Assert.Equal(InstalledModsSortColumn.Name, vm.Mods.SortColumn);
        Assert.False(vm.Mods.SortDescending);
        Assert.Equal("draft,Core,Atlas,Maps,Tweaks,Cars", VisibleIds(vm));
    }

    [Theory]
    [InlineData(InstalledModsSortColumn.Name, "draft,Core,Atlas,Maps,Tweaks,Cars", "Cars,Tweaks,Atlas,Maps,Core,draft")]
    [InlineData(InstalledModsSortColumn.Id, "Atlas,Cars,Core,draft,Maps,Tweaks", "Tweaks,Maps,draft,Core,Cars,Atlas")]
    [InlineData(InstalledModsSortColumn.WorkshopId, "Atlas,Core,Maps,Cars,draft,Tweaks", "draft,Cars,Maps,Core,Atlas,Tweaks")]
    [InlineData(InstalledModsSortColumn.Source, "draft,Atlas,Core,Maps,Cars,Tweaks", "Tweaks,Core,Maps,Cars,draft,Atlas")]
    [InlineData(InstalledModsSortColumn.Enabled, "Cars,draft,Core,Atlas,Maps,Tweaks", "draft,Core,Atlas,Maps,Tweaks,Cars")]
    [InlineData(InstalledModsSortColumn.Collections, "Cars,Core,Maps,draft,Atlas,Tweaks", "Core,Maps,Cars,draft,Atlas,Tweaks")]
    public async Task InstalledMods_SortBy_AscendingThenReversedOnTheSecondClick(InstalledModsSortColumn column, string ascending, string descending)
    {
        var vm = await CreateWithSortModsAsync();
        if (column == InstalledModsSortColumn.Name)
        {
            vm.Mods.SortByCommand.Execute(InstalledModsSortColumn.Id);
        }

        vm.Mods.SortByCommand.Execute(column);

        Assert.Equal(column, vm.Mods.SortColumn);
        Assert.False(vm.Mods.SortDescending);
        Assert.Equal(ascending, VisibleIds(vm));

        vm.Mods.SortByCommand.Execute(column);

        Assert.True(vm.Mods.SortDescending);
        Assert.Equal(descending, VisibleIds(vm));
    }

    [Fact]
    public async Task InstalledMods_SortBy_AnotherColumn_StartsAscending()
    {
        var vm = await CreateWithSortModsAsync();
        vm.Mods.SortByCommand.Execute(InstalledModsSortColumn.Id);
        vm.Mods.SortByCommand.Execute(InstalledModsSortColumn.Id);

        vm.Mods.SortByCommand.Execute(InstalledModsSortColumn.Source);

        Assert.Equal(InstalledModsSortColumn.Source, vm.Mods.SortColumn);
        Assert.False(vm.Mods.SortDescending);
        Assert.Equal("draft,Atlas,Core,Maps,Cars,Tweaks", VisibleIds(vm));
    }

    [Fact]
    public async Task InstalledMods_Sort_AppliesToTheSearchedAndFilteredRows()
    {
        var vm = await CreateWithSortModsAsync();
        vm.Mods.SortByCommand.Execute(InstalledModsSortColumn.WorkshopId);
        vm.Mods.SortByCommand.Execute(InstalledModsSortColumn.WorkshopId);

        vm.Mods.Filter = InstalledModsFilter.NotEnabled;

        Assert.Equal("draft,Maps,Core,Atlas,Tweaks", VisibleIds(vm));

        vm.Mods.SearchText = "map";

        Assert.Equal("Maps,Atlas", VisibleIds(vm));
    }

    [Fact]
    public async Task InstalledMods_Sort_SurvivesAReload()
    {
        var vm = await CreateWithSortModsAsync();
        vm.Mods.SortByCommand.Execute(InstalledModsSortColumn.Collections);
        vm.Mods.SortByCommand.Execute(InstalledModsSortColumn.Collections);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(InstalledModsSortColumn.Collections, vm.Mods.SortColumn);
        Assert.True(vm.Mods.SortDescending);
        Assert.Equal("Core,Maps,Cars,draft,Atlas,Tweaks", VisibleIds(vm));
    }

    [Fact]
    public async Task InstalledMods_AddToCollection_AppendsInTheShownOrder()
    {
        var vm = await CreateWithSortModsAsync();
        vm.Mods.SortByCommand.Execute(InstalledModsSortColumn.Name);
        foreach (var row in vm.Mods.VisibleRows.Where(row => row.Mod.Id is "Tweaks" or "Core" or "Cars"))
        {
            row.IsSelected = true;
        }

        await vm.Mods.AddTargets.Single(target => target.Name == "Survival").AddCommand.ExecuteAsync(null);

        Assert.Equal(["Maps", "Core", "Gone", "Cars", "Tweaks"], _service.IdsOf("Survival"));
    }

    // Collections

    [Fact]
    public async Task Collections_ShowCountsMissingAndTheCurrentCollection()
    {
        _defaults.Save(SteamGame.RootPath, "Survival");

        var vm = await CreateActivatedAsync();

        Assert.Equal(
            [
                new ModCollectionRow("Survival", 3, 1, IsCurrent: true, IsActiveInOrder: false, IsSelected: true),
                new ModCollectionRow("Driving", 1, 0, IsCurrent: false, IsActiveInOrder: true, IsSelected: false),
            ],
            vm.Collections.Rows);
        Assert.Equal(["Maps", "Core", "Gone"], vm.Collections.Entries.Select(entry => entry.Id));
        Assert.Equal([false, false, true], vm.Collections.Entries.Select(entry => entry.IsMissing));
        Assert.Equal(["Map Pack", "Core Library", "Gone"], vm.Collections.Entries.Select(entry => entry.Name));
        Assert.Equal([1, 2, 3], vm.Collections.Entries.Select(entry => entry.Number));
    }

    [Fact]
    public async Task New_CreatesAnEmptyCollectionAndSelectsIt()
    {
        var vm = await CreateActivatedAsync();
        _dialogs.Accept(" Fresh ");

        await vm.Collections.NewCommand.ExecuteAsync(null);

        Assert.Equal([], _service.IdsOf("Fresh"));
        Assert.Equal("Fresh", vm.Collections.Selected?.Name);
        Assert.Empty(vm.Collections.Entries);
    }

    [Fact]
    public async Task New_ExistingName_OverwritesOnlyAfterConfirm()
    {
        var vm = await CreateActivatedAsync();
        _dialogs.Accept("Driving");
        _dialogs.ConfirmAnswers.Enqueue(true);

        await vm.Collections.NewCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.Confirms);
        Assert.Equal([], _service.IdsOf("Driving"));
    }

    [Fact]
    public async Task New_Cancelled_WritesNothing()
    {
        var vm = await CreateActivatedAsync();

        await vm.Collections.NewCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.Prompts);
        Assert.Empty(_service.Saves);
    }

    [Fact]
    public async Task NewFromEnabled_SavesTheEnabledListInItsOrder()
    {
        _service.ActiveIds = ["Tweaks", "Cars", "Unknown"];
        var vm = await CreateActivatedAsync();
        _dialogs.Accept("Current");

        await vm.Collections.NewFromEnabledCommand.ExecuteAsync(null);

        Assert.Equal(["Tweaks", "Cars", "Unknown"], _service.IdsOf("Current"));
    }

    [Fact]
    public async Task NewFromEnabled_NothingEnabled_IsDisabled()
    {
        _service.ActiveIds = [];

        var vm = await CreateActivatedAsync();

        Assert.False(vm.Collections.NewFromEnabledCommand.CanExecute(null));
    }

    [Fact]
    public async Task Rename_KeepsTheOrderAndTheCurrentMark()
    {
        _defaults.Save(SteamGame.RootPath, "Survival");
        var vm = await CreateActivatedAsync();
        _dialogs.Accept("Survival B42");

        await vm.Collections.RenameCommand.ExecuteAsync(null);

        var save = Assert.Single(_service.Saves);
        Assert.Equal((SteamGame.RootPath, "Survival", "Survival B42"), (save.InstallRoot, save.OriginalName, save.Name));
        Assert.Equal(["Maps", "Core", "Gone"], _service.IdsOf("Survival B42"));
        Assert.Null(_service.IdsOf("Survival"));
        Assert.Equal("Survival B42", _defaults.Get(SteamGame.RootPath));
        Assert.True(vm.Collections.Selected?.IsCurrent);
    }

    [Fact]
    public async Task Rename_SameName_WritesNothing()
    {
        var vm = await CreateActivatedAsync();
        _dialogs.Accept("Survival");

        await vm.Collections.RenameCommand.ExecuteAsync(null);

        Assert.Empty(_dialogs.Confirms);
        Assert.Empty(_service.Saves);
    }

    [Fact]
    public async Task Delete_AfterConfirm_RemovesItAndClearsTheCurrentMark()
    {
        _defaults.Save(SteamGame.RootPath, "Survival");
        var vm = await CreateActivatedAsync();
        _dialogs.ConfirmAnswers.Enqueue(true);

        await vm.Collections.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(["Survival"], _service.Deletes);
        Assert.Null(_defaults.Get(SteamGame.RootPath));
        Assert.Equal("Driving", vm.Collections.Selected?.Name);
    }

    [Fact]
    public async Task Delete_Declined_WritesNothing()
    {
        var vm = await CreateActivatedAsync();

        await vm.Collections.DeleteCommand.ExecuteAsync(null);

        Assert.Empty(_service.Deletes);
    }

    [Fact]
    public async Task Share_CopiesTheGamesShareTextInTheShownOrder()
    {
        var vm = await CreateActivatedAsync();

        await vm.Collections.ShareCommand.ExecuteAsync(null);

        Assert.Equal("Survival:Maps;Core;Gone;\n", _clipboard.Text);
        Assert.NotNull(vm.Collections.StatusText);
    }

    [Fact]
    public async Task Import_PrefillsFromTheClipboard_SavesEveryLineAndOverwritesOnlyAfterConfirm()
    {
        const string text = "!fav!Core;\nShared:Tweaks;Cars;\nDriving:Maps;\n";
        _clipboard.Text = text;
        var vm = await CreateActivatedAsync();
        _dialogs.Accept(text);
        _dialogs.ConfirmAnswers.Enqueue(false);

        await vm.Collections.ImportCommand.ExecuteAsync(null);

        var prompt = Assert.Single(_dialogs.Prompts);
        Assert.Equal(text.Trim(), prompt.InitialText);
        Assert.True(prompt.IsMultiline);
        Assert.Equal(["Tweaks", "Cars"], _service.IdsOf("Shared"));
        Assert.Equal(["Cars"], _service.IdsOf("Driving"));
        Assert.Equal("Shared", vm.Collections.Selected?.Name);
    }

    [Fact]
    public async Task Import_ClipboardWithoutCollections_StartsEmpty_AndValidatesThePaste()
    {
        _clipboard.Text = "hello";
        var vm = await CreateActivatedAsync();

        await vm.Collections.ImportCommand.ExecuteAsync(null);

        var prompt = Assert.Single(_dialogs.Prompts);
        Assert.Equal("", prompt.InitialText);
        Assert.Equal(UiText.ModCollections_ImportNothing, prompt.Validate("no collection here"));
        Assert.NotNull(prompt.Validate("bad|name:Core;"));
        Assert.Null(prompt.Validate("Good:Core;"));
        Assert.Empty(_service.Saves);
    }

    [Fact]
    public async Task Move_WritesOnceWithTheFinalOrder()
    {
        var vm = await CreateActivatedAsync();

        vm.Collections.Move(0, 2);
        vm.Collections.Move(2, 1);

        Assert.Empty(_service.Saves);
        Assert.Equal(["Core", "Maps", "Gone"], vm.Collections.Entries.Select(entry => entry.Id));
        Assert.Equal([1, 2, 3], vm.Collections.Entries.Select(entry => entry.Number));

        _time.Advance(OrderSaveDelay);

        var save = Assert.Single(_service.Saves);
        Assert.Equal(("Survival", "Survival"), (save.OriginalName, save.Name));
        Assert.Equal(["Core", "Maps", "Gone"], save.ModIds);
        Assert.Equal(2, _service.GetRequests.Count);
        Assert.Equal(["Core", "Maps", "Gone"], vm.Collections.Entries.Select(entry => entry.Id));
    }

    [Fact]
    public async Task Move_DelayedWriteFails_ShowsTheErrorAndRereads()
    {
        var vm = await CreateActivatedAsync();
        var error = TestErrors.Create(ErrorCode.ModCollectionFileWriteFailed, @"C:\Users\Test\Zomboid\Lua\pz_modlist_settings.cfg");
        _service.SaveError = _ => error;
        vm.Collections.Move(0, 1);

        _time.Advance(OrderSaveDelay);

        Assert.Equal([ErrorMessages.ToUserText(error)], _dialogs.Errors);
        Assert.Equal(2, _service.GetRequests.Count);
        Assert.Equal(["Maps", "Core", "Gone"], vm.Collections.Entries.Select(entry => entry.Id));
    }

    [Fact]
    public async Task Move_GameStartsBeforeTheWrite_WritesItWhenTheGameExits()
    {
        var vm = await CreateActivatedAsync();
        vm.Collections.Move(0, 1);
        _runState.IsGameRunning = true;

        _time.Advance(OrderSaveDelay);

        Assert.Empty(_service.Saves);
        Assert.Equal(["Core", "Maps", "Gone"], vm.Collections.Entries.Select(entry => entry.Id));

        _runState.IsGameRunning = false;

        Assert.Equal(["Core", "Maps", "Gone"], _service.IdsOf("Survival"));
        Assert.Equal(["Core", "Maps", "Gone"], vm.Collections.Entries.Select(entry => entry.Id));
    }

    [Fact]
    public async Task Move_ReloadBeforeTheWrite_KeepsTheShownOrder()
    {
        var vm = await CreateActivatedAsync();
        vm.Collections.Move(0, 1);

        await vm.ActivateCommand.ExecuteAsync(null);

        Assert.Equal(["Core", "Maps", "Gone"], vm.Collections.Entries.Select(entry => entry.Id));
    }

    [Fact]
    public async Task Move_ThenAnotherWrite_SavesTheOrderFirst()
    {
        var vm = await CreateActivatedAsync();
        vm.Collections.Move(0, 1);
        _dialogs.Accept("Fresh");

        await vm.Collections.NewCommand.ExecuteAsync(null);

        Assert.Equal(["Survival", "Fresh"], _service.Saves.Select(save => save.Name));
        Assert.Equal(["Core", "Maps", "Gone"], _service.IdsOf("Survival"));
    }

    [Fact]
    public async Task MoveUpAndDown_MoveOneStep()
    {
        var vm = await CreateActivatedAsync();

        vm.Collections.MoveDownCommand.Execute(vm.Collections.Entries[0]);
        vm.Collections.MoveUpCommand.Execute(vm.Collections.Entries[2]);
        vm.Collections.MoveUpCommand.Execute(vm.Collections.Entries[0]);

        Assert.Equal(["Core", "Gone", "Maps"], vm.Collections.Entries.Select(entry => entry.Id));
    }

    [Fact]
    public async Task MoveToTopAndBottom_MoveToTheEnds_AndWriteOnce()
    {
        var vm = await CreateActivatedAsync();
        var collections = vm.Collections;

        collections.MoveToBottomCommand.Execute(collections.Entries[0]);
        collections.MoveToTopCommand.Execute(collections.Entries[1]);

        Assert.Equal(["Gone", "Core", "Maps"], collections.Entries.Select(entry => entry.Id));
        Assert.Equal([1, 2, 3], collections.Entries.Select(entry => entry.Number));
        Assert.Empty(_service.Saves);

        _time.Advance(OrderSaveDelay);

        var save = Assert.Single(_service.Saves);
        Assert.Equal(["Gone", "Core", "Maps"], save.ModIds);
    }

    [Fact]
    public async Task MoveCommands_AreDisabledAtTheEnds()
    {
        var vm = await CreateActivatedAsync();
        var collections = vm.Collections;
        var (first, middle, last) = (collections.Entries[0], collections.Entries[1], collections.Entries[2]);

        Assert.False(collections.MoveToTopCommand.CanExecute(first));
        Assert.False(collections.MoveUpCommand.CanExecute(first));
        Assert.True(collections.MoveDownCommand.CanExecute(first));
        Assert.True(collections.MoveToBottomCommand.CanExecute(first));
        Assert.True(collections.MoveToTopCommand.CanExecute(middle));
        Assert.True(collections.MoveToBottomCommand.CanExecute(middle));
        Assert.True(collections.MoveToTopCommand.CanExecute(last));
        Assert.False(collections.MoveDownCommand.CanExecute(last));
        Assert.False(collections.MoveToBottomCommand.CanExecute(last));
    }

    [Fact]
    public async Task MoveToTop_ReevaluatesWhichRowsAreAtTheEnds()
    {
        var vm = await CreateActivatedAsync();
        var collections = vm.Collections;
        var (first, last) = (collections.Entries[0], collections.Entries[2]);
        var changes = 0;
        collections.MoveToBottomCommand.CanExecuteChanged += (_, _) => changes++;

        collections.MoveToTopCommand.Execute(last);

        Assert.True(changes > 0);
        Assert.False(collections.MoveToTopCommand.CanExecute(last));
        Assert.True(collections.MoveToTopCommand.CanExecute(first));
        Assert.False(collections.MoveToBottomCommand.CanExecute(collections.Entries[2]));
    }

    [Fact]
    public async Task AutoSort_SavesTheGamesOrder()
    {
        var vm = await CreateActivatedAsync();

        await vm.Collections.AutoSortCommand.ExecuteAsync(null);

        Assert.Equal(ModLoadOrder.AutoSort(["Maps", "Core", "Gone"], _service.InstalledMods), _service.IdsOf("Survival"));
        Assert.Equal(["Core", "Maps", "Gone"], vm.Collections.Entries.Select(entry => entry.Id));
    }

    [Fact]
    public async Task AddMod_SearchListsInstalledModsNotInTheCollection_AndAppends()
    {
        var vm = await CreateActivatedAsync();

        vm.Collections.AddSearchText = "a";

        Assert.Equal(["Tweaks", "Vehicles"], vm.Collections.AddCandidates.Select(mod => mod.Name));

        await vm.Collections.AddModCommand.ExecuteAsync(vm.Collections.AddCandidates[1]);

        Assert.Equal(["Maps", "Core", "Gone", "Cars"], _service.IdsOf("Survival"));
    }

    [Fact]
    public async Task RemoveMod_DropsItAndKeepsTheRest()
    {
        var vm = await CreateActivatedAsync();

        await vm.Collections.RemoveModCommand.ExecuteAsync(vm.Collections.Entries[0]);

        Assert.Equal(["Core", "Gone"], _service.IdsOf("Survival"));
    }

    [Fact]
    public async Task SaveFails_ShowsTheErrorAndRereads()
    {
        var vm = await CreateActivatedAsync();
        var error = TestErrors.Create(ErrorCode.ModCollectionFileWriteFailed, @"C:\Users\Test\Zomboid\Lua\pz_modlist_settings.cfg");
        _service.SaveError = _ => error;

        await vm.Collections.RemoveModCommand.ExecuteAsync(vm.Collections.Entries[0]);

        Assert.Equal([ErrorMessages.ToUserText(error)], _dialogs.Errors);
        Assert.Equal(2, _service.GetRequests.Count);
        Assert.Equal(3, vm.Collections.Entries.Count);
    }

    [Fact]
    public async Task Use_NotCurrent_AppliesItAndMakesItCurrent()
    {
        _service.ActiveIds = [];
        _defaults.Save(SteamGame.RootPath, "Survival");
        var vm = await CreateActivatedAsync();
        Select(vm, "Driving");
        var collections = vm.Collections;
        Assert.True(collections.UseCommand.CanExecute(null));
        Assert.Equal((UiText.ModCollections_Use, UiText.ModCollections_UseHint), (collections.UseText, collections.UseHint));

        await collections.UseCommand.ExecuteAsync(null);

        Assert.Equal([(SteamGame.RootPath, "", "Driving")], _service.Applies);
        Assert.Equal(["Cars"], _service.ActiveIds);
        Assert.Equal("Driving", _defaults.Get(SteamGame.RootPath));
        Assert.Equal(["Driving"], collections.Rows.Where(row => row.IsCurrent).Select(row => row.Name));
        Assert.Equal(string.Format(UiText.ModCollections_Applied, "Driving", 1), collections.StatusText);
        Assert.Equal(UiText.ModCollections_InUse, collections.UseText);
        Assert.False(collections.UseCommand.CanExecute(null));
    }

    [Fact]
    public async Task Use_NotCurrentButAlreadyTheEnabledList_IsStillOffered()
    {
        var vm = await CreateActivatedAsync();

        Select(vm, "Driving");

        Assert.True(vm.Collections.UseCommand.CanExecute(null));
        Assert.Equal(UiText.ModCollections_Use, vm.Collections.UseText);
    }

    [Fact]
    public async Task Use_Cancelled_DoesNotMakeItCurrent()
    {
        _service.ActiveIds = ["Tweaks"];
        var vm = await CreateActivatedAsync();
        Select(vm, "Driving");

        await vm.Collections.UseCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.Prompts);
        Assert.Empty(_service.Applies);
        Assert.Null(_defaults.Get(SteamGame.RootPath));
        Assert.DoesNotContain(vm.Collections.Rows, row => row.IsCurrent);
    }

    [Fact]
    public async Task Use_Fails_ShowsTheErrorAndDoesNotMakeItCurrent()
    {
        _service.ActiveIds = [];
        var error = TestErrors.Create(ErrorCode.ModCollectionFileWriteFailed, @"C:\Users\Test\Zomboid\mods\default.txt");
        _service.ApplyError = _ => error;
        var vm = await CreateActivatedAsync();
        Select(vm, "Driving");

        await vm.Collections.UseCommand.ExecuteAsync(null);

        Assert.Equal([ErrorMessages.ToUserText(error)], _dialogs.Errors);
        Assert.Null(_defaults.Get(SteamGame.RootPath));
        Assert.Null(vm.Collections.StatusText);
    }

    [Fact]
    public async Task Use_CurrentAndUpToDate_IsDisabledAsInUse()
    {
        _defaults.Save(SteamGame.RootPath, "Driving");
        var vm = await CreateActivatedAsync();

        Select(vm, "Driving");

        var collections = vm.Collections;
        Assert.False(collections.UseCommand.CanExecute(null));
        Assert.Equal((UiText.ModCollections_InUse, UiText.ModCollections_InUseHint), (collections.UseText, collections.UseHint));
    }

    [Fact]
    public async Task Use_CurrentButTheEnabledListDiffers_IsOfferedAsApplyAgain_UnlessTheGameRuns()
    {
        _defaults.Save(SteamGame.RootPath, "Survival");
        var vm = await CreateActivatedAsync();

        var collections = vm.Collections;
        Assert.Equal("Survival", collections.Selected?.Name);
        Assert.True(collections.UseCommand.CanExecute(null));
        Assert.Equal((UiText.ModCollections_ApplyAgain, UiText.ModCollections_ApplyAgainHint), (collections.UseText, collections.UseHint));

        _runState.IsGameRunning = true;

        Assert.False(collections.UseCommand.CanExecute(null));
    }

    [Fact]
    public async Task Use_CurrentWithTheSameModsInAnotherOrder_IsOfferedAsApplyAgain()
    {
        _service.WithCollection("Mixed", "Tweaks", "Cars");
        _service.ActiveIds = ["Cars", "Tweaks"];
        _defaults.Save(SteamGame.RootPath, "Mixed");
        var vm = await CreateActivatedAsync();

        Select(vm, "Mixed");

        Assert.True(vm.Collections.UseCommand.CanExecute(null));
        Assert.Equal(UiText.ModCollections_ApplyAgain, vm.Collections.UseText);
    }

    [Fact]
    public async Task Move_OfTheCurrentUpToDateCollection_OffersApplyAgainBeforeTheWrite()
    {
        _service.WithCollection("Mixed", "Cars", "Tweaks");
        _service.ActiveIds = ["Cars", "Tweaks"];
        _defaults.Save(SteamGame.RootPath, "Mixed");
        var vm = await CreateActivatedAsync();
        Select(vm, "Mixed");
        var collections = vm.Collections;
        Assert.False(collections.UseCommand.CanExecute(null));

        collections.Move(0, 1);

        Assert.Empty(_service.Saves);
        Assert.True(collections.UseCommand.CanExecute(null));
        Assert.Equal(UiText.ModCollections_ApplyAgain, collections.UseText);

        await vm.ActivateCommand.ExecuteAsync(null);

        Assert.Equal(UiText.ModCollections_ApplyAgain, collections.UseText);

        collections.Move(1, 0);

        Assert.Equal(UiText.ModCollections_InUse, collections.UseText);
        Assert.False(collections.UseCommand.CanExecute(null));
    }

    [Fact]
    public async Task GameRunning_DisablesEveryWriteButNotShare()
    {
        var vm = await CreateActivatedAsync();

        _runState.IsGameRunning = true;

        var collections = vm.Collections;
        Assert.False(vm.CanWrite);
        Assert.False(collections.NewCommand.CanExecute(null));
        Assert.False(collections.NewFromEnabledCommand.CanExecute(null));
        Assert.False(collections.ImportCommand.CanExecute(null));
        Assert.False(collections.RenameCommand.CanExecute(null));
        Assert.False(collections.DeleteCommand.CanExecute(null));
        Assert.False(collections.UseCommand.CanExecute(null));
        Assert.False(collections.AutoSortCommand.CanExecute(null));
        Assert.False(collections.AddModCommand.CanExecute(_service.InstalledMods[0]));
        Assert.False(collections.RemoveModCommand.CanExecute(collections.Entries[0]));
        Assert.False(collections.MoveUpCommand.CanExecute(collections.Entries[1]));
        Assert.False(collections.MoveDownCommand.CanExecute(collections.Entries[1]));
        Assert.False(collections.MoveToTopCommand.CanExecute(collections.Entries[1]));
        Assert.False(collections.MoveToBottomCommand.CanExecute(collections.Entries[1]));
        Assert.False(collections.CanReorder);
        Assert.True(collections.ShareCommand.CanExecute(null));

        collections.Move(0, 1);
        _time.Advance(OrderSaveDelay);

        Assert.Equal(["Maps", "Core", "Gone"], collections.Entries.Select(entry => entry.Id));
        Assert.Empty(_service.Saves);
    }

    [Fact]
    public async Task GameExits_RereadsTheFiles()
    {
        var vm = await CreateActivatedAsync();
        _runState.IsGameRunning = true;

        _service.WithCollection("Added in-game", "Tweaks");
        _runState.IsGameRunning = false;

        Assert.True(vm.CanWrite);
        Assert.Contains(vm.Collections.Rows, row => row.Name == "Added in-game");
    }
}
