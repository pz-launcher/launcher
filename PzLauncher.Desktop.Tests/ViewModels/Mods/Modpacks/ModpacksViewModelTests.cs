using System.Globalization;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.Modpacks;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Mods.Library;
using PzLauncher.Desktop.Services.Mods.LocalCollections;
using PzLauncher.Desktop.Services.Mods.Modpacks;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.ViewModels.Mods;
using PzLauncher.Desktop.ViewModels.Mods.Modpacks;
using PzLauncher.Desktop.ViewModels.Mods.Workshop;
using PzLauncher.Desktop.Services.Shell.Dialogs;
using PzLauncher.Desktop.Services.Preferences.Records;
using PzLauncher.Desktop.ViewModels.Mods.Collections;
using static PzLauncher.Desktop.Tests.TestSupport.Fakes.TestInstalls;
using static PzLauncher.Desktop.Tests.TestSupport.Fakes.TestMods;
using static PzLauncher.Desktop.Tests.TestSupport.Fakes.TestWorkshop;

namespace PzLauncher.Desktop.Tests.ViewModels.Mods.Modpacks;

[Collection(UiTextCultureCollection.Name)]
public class ModpacksViewModelTests : UiTextCultureTest
{
    private const ulong Pack = 9000;

    private static readonly Guid ListId = new("6f1c2b0e-4d3a-4f5e-9a8b-1c2d3e4f5a6b");

    private static readonly Modpack Server = new("Server", Linked("Survival", "Core", "Maps", "Cars", "Weather"), new ModpackCollection.Steam(Pack), []);

    private static readonly Modpack Night = new(
        "Night",
        Linked("Driving", "Cars"),
        new ModpackCollection.Local(ListId, "List", [new WorkshopEntry(1002, "Map Pack"), new WorkshopEntry(1005, "Weather")]),
        []);

    private readonly FakeInstallDetectionService _detection = new();
    private readonly FakeModCollectionService _collections = new();
    private readonly FakeModCollectionDefaultsService _defaults = new();
    private readonly FakeInstallOverridesService _overrides = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeClipboardService _clipboard = new();
    private readonly FakeWorkshopService _service = new();
    private readonly FakeWorkshopActionRunner _runner = new();
    private readonly FakePreferenceStore _preferences = new();

    private readonly FakeModLibraryFile _library = new();
    private readonly GameRunState _runState = new();
    private readonly LocalCollectionStore _localCollections;
    private readonly Dictionary<string, IReadOnlyList<Modpack>> _saved = new() { [SteamGame.RootPath] = [Server, Night] };
    private WorkshopFavorites _favorites;
    private WorkshopCollectionHistory _history;
    private WorkshopUserCollectionsCache _userCollections;
    private ModpackStore _modpacks;

    public ModpacksViewModelTests()
    {
        Installs = new(_detection, new FakeFolderPicker(), new FakePlatformEnvironment(), Localization);
        _collections
            .WithMods(
                Mod("Core", workshopId: 1001),
                Mod("Maps", workshopId: 1002),
                Mod("Cars", workshopId: 1003),
                Mod("Weather", workshopId: 1005),
                Mod("Tweaks", source: InstalledModSource.Local))
            .WithCollection("Survival", "Core", "Maps", "Cars", "Weather")
            .WithCollection("Driving", "Cars")
            .WithCollection("Spare", "Tweaks");
        _service
            .WithItem(Item(1001, mods: ["Core"]))
            .WithItem(Item(1002, mods: ["Maps"]))
            .WithItem(Item(1003, mods: ["Cars"]))
            .WithPublished(1001, "Core Library", "100")
            .WithPublished(1002, "Map Pack", "100")
            .WithPublished(1003, "Vehicles", "100")
            .WithPublished(1004, "Trees", "100")
            .WithPublished(1005, "Weather", "100")
            .WithPublished(Pack, "Survival Pack", "1");
        _service.Collections[Pack] = [1001, 1004];
        _localCollections = LocalCollectionStore(
            _library, new SavedLocalCollection(ListId, "List", [new SavedWorkshopEntry(1002, "Map Pack"), new SavedWorkshopEntry(1005, "Weather")]));
        _favorites = Favorites(_preferences);
        _history = History(_preferences);
        _userCollections = UserCollections(_preferences);
        _modpacks = ModpackStoreOf(_localCollections);
    }

    private InstallsViewModel Installs { get; }

    private static ModpackPreset Linked(string name, params string[] ids) => new(name, ids, IsLinked: true);

    private async Task<ModsViewModel> CreatePageAsync()
    {
        _detection.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamGame, ManualGame));
        await Installs.ActivateCommand.ExecuteAsync(null);
        _modpacks = ModpackStoreOf(_localCollections, _library, _saved);
        var orderWriter = new ModCollectionOrderWriter(_collections, _runState, new FakePlatformEnvironment(), new ManualTimeProvider());
        var page = new ModsViewModel(
            Installs, _collections, _overrides, _defaults, new ModCollectionApplier(_collections, _dialogs), _dialogs, _clipboard,
            _runState, orderWriter, _service, _service, _runner, new FakeUrlLauncher(), _history, _userCollections, _favorites,
            new WorkshopTitles(_history, _userCollections, _favorites, _localCollections), _localCollections, _modpacks, Localization)
        {
            Section = ModsSection.Modpacks,
        };
        await page.ActivateCommand.ExecuteAsync(null);
        return page;
    }

    private async Task<ModpacksViewModel> CreateAsync() => (await CreatePageAsync()).Modpacks;

    private Modpack Saved(string name) => Assert.Single(_modpacks.For(SteamGame.RootPath), modpack => modpack.Name == name);

    private static WorkshopItemRow Row(ModpacksViewModel vm, ulong id) => vm.Rows.Single(row => row.Id == id);

    private static Task SelectAsync(ModpacksViewModel vm, string name) => vm.SelectCommand.ExecuteAsync(vm.Modpacks.Single(row => row.Name == name));

    private static void Check(IEnumerable<ModpackEditOption> options, string text) => options.Single(option => option.Text == text).IsChecked = true;

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private static string LocalText(string modpack, string list, string preset) =>
        $"#pzlauncher-modpack 1\n#name\n{modpack}\n#local-collection\n{list}\n1004\n1001\n#preset\n{preset}:Core;\n";

    private SharedModpack Shared()
    {
        Assert.NotNull(_clipboard.Text);
        return Assert.IsType<ModpackShareTextResult.Read>(ModpackShareText.Parse(_clipboard.Text)).Modpack;
    }

    private Task ImportAsync(ModpacksViewModel vm, string text)
    {
        _dialogs.Accept(text);
        _dialogs.ConfirmAnswers.Enqueue(true);
        return vm.ImportCommand.ExecuteAsync(null);
    }

    // The section

    [Fact]
    public async Task Show_ListsTheInstallsModpacksAndTheFirstOnesCollectionItemsThenTheItemsOnlyItsPresetHas()
    {
        var vm = await CreateAsync();

        Assert.Equal(["Server", "Night"], vm.Modpacks.Select(row => row.Name));
        Assert.Equal(new ModpackRow("Server", "Survival", 4, true, "Survival Pack", "9000", true, 3, true), vm.Selected);
        Assert.Equal([1001UL, 1004, 1002, 1003, 1005], vm.Rows.Select(row => row.Id));
        Assert.Equal(("Trees", WorkshopItemState.NotSubscribed), (Row(vm, 1004).Title, Row(vm, 1004).Item.State));
        Assert.Equal(("Weather", WorkshopItemState.NotSubscribed), (Row(vm, 1005).Title, Row(vm, 1005).Item.State));
        Assert.Equal(2, vm.MissingCount);
    }

    [Fact]
    public async Task Show_SavesTheRecomputedExtraItems()
    {
        await CreateAsync();

        Assert.Equal([1002UL, 1003, 1005], Saved("Server").ExtraItems);
    }

    [Fact]
    public async Task Select_LocalCollection_ListsItsItemsInOrderThenTheItemsOnlyItsPresetHas()
    {
        var vm = await CreateAsync();

        await SelectAsync(vm, "Night");

        Assert.Equal(new ModpackRow("Night", "Driving", 1, true, "List", null, true, 1, true), vm.Selected);
        Assert.Equal([1002UL, 1005, 1003], vm.Rows.Select(row => row.Id));
        Assert.Equal("Map Pack", Row(vm, 1002).Title);
    }

    [Fact]
    public async Task Select_SwitchingModpacks_ReadsNoSubscriptions()
    {
        var vm = await CreateAsync();
        var reads = _service.GetRequests.Count;

        await SelectAsync(vm, "Night");
        await SelectAsync(vm, "Server");

        Assert.Equal(reads, _service.GetRequests.Count);
        Assert.Equal([1001UL, 1004, 1002, 1003, 1005], vm.Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task OtherInstall_ShowsOnlyItsOwnModpacks()
    {
        _saved[ManualGame.RootPath] = [Server with { Name = "Other install" }];
        var page = await CreatePageAsync();

        Installs.Use(ManualGame.RootPath);
        await page.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["Other install"], page.Modpacks.Modpacks.Select(row => row.Name));
        Assert.Equal("Other install", page.Modpacks.Selected?.Name);
    }

    [Fact]
    public async Task NoModpacks_ShowsTheEmptyState()
    {
        _saved.Clear();

        var vm = await CreateAsync();

        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasSelection);
        Assert.Empty(vm.Rows);
        Assert.False(vm.RenameCommand.CanExecute(null));
    }

    [Fact]
    public async Task Show_SteamCollectionUnreadable_ListsThePresetsItemsAndWarns()
    {
        var error = TestErrors.Create(ErrorCode.WorkshopCollectionNotFound);
        _service.CollectionError = () => error;

        var vm = await CreateAsync();

        Assert.Equal([1001UL, 1002, 1003, 1005], vm.Rows.Select(row => row.Id));
        Assert.Contains(ErrorMessages.ToUserText(error), vm.Warnings);
        Assert.Equal("9000", vm.Selected?.CollectionTitle);
    }

    [Fact]
    public async Task SubscribeMissing_SubscribesTheCollectionsAndThePresetsUnsubscribedItemsInOneRun()
    {
        var vm = await CreateAsync();

        await vm.SubscribeMissingCommand.ExecuteAsync(null);

        var run = Assert.Single(_runner.Runs);
        Assert.Equal((SteamGame.RootPath, WorkshopAction.Subscribe), (run.Root, run.Request.Action));
        Assert.Equal([1004UL, 1005], run.Request.Ids);
    }

    // New, edit, rename, delete

    [Fact]
    public async Task New_SavesTheDialogsChoiceLinkedAndShowsIt()
    {
        var vm = await CreateAsync();
        _dialogs.ModpackEditResult = FakeDialogService.EditsModpack(edit =>
        {
            edit.Name = " Pack night ";
            Check(edit.Presets, "Driving");
            edit.SteamLinkText = "9000";
        });

        await vm.NewCommand.ExecuteAsync(null);

        Assert.Equal(new Modpack("Pack night", Linked("Driving", "Cars"), new ModpackCollection.Steam(Pack), [1003]), Saved("Pack night"));
        Assert.Equal(["Server", "Night", "Pack night"], vm.Modpacks.Select(row => row.Name));
        Assert.Equal("Pack night", vm.Selected?.Name);
        Assert.Equal([1001UL, 1004, 1003], vm.Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task New_OffersThePresetsAndEveryKeptCollectionOnce()
    {
        _favorites.Add([new WorkshopEntry(Pack, "Survival Pack")], []);
        _history.Remember(9001, "Opened");
        _history.Remember(Pack, null);
        _userCollections.Save(new WorkshopUserCollections(12345678, [new WorkshopCollectionSummary(9002, "Liked", 3)], [new WorkshopCollectionSummary(9003, null, 1)]));
        var vm = await CreateAsync();

        await vm.NewCommand.ExecuteAsync(null);

        var edit = Assert.Single(_dialogs.ModpackEdits);
        Assert.Equal(UiText.Modpacks_NewTitle, edit.Title);
        Assert.Equal(["Survival", "Driving", "Spare"], edit.Presets.Select(option => option.Text));
        Assert.True(edit.Presets[0].IsChecked);
        Assert.Equal(
            [
                ("Survival Pack", UiText.Mods_SectionWorkshopFavorites),
                ("Liked", UiText.WorkshopCollections_Favorited),
                ("9003", UiText.WorkshopCollections_Published),
                ("Opened", UiText.WorkshopCollections_Remembered),
            ],
            edit.SteamCollections.Select(option => (option.Text, option.Detail)));
        Assert.Equal(["List"], edit.LocalCollections.Select(option => option.Text));
        Assert.False(edit.AcceptCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("Night", nameof(UiText.Modpacks_NameTaken))]
    [InlineData(" ", nameof(UiText.Modpacks_NameInvalid))]
    [InlineData("Night 2", null)]
    public async Task New_NameMustBeValidAndFreeInTheInstall(string name, string? error)
    {
        var vm = await CreateAsync();
        string? shownError = null;
        _dialogs.ModpackEditResult = FakeDialogService.EditsModpack(edit =>
        {
            edit.Name = name;
            edit.IsLocalChecked = true;
            Check(edit.LocalCollections, "List");
            shownError = edit.NameError;
        });

        await vm.NewCommand.ExecuteAsync(null);

        Assert.Equal(error is null ? null : UiText.ResourceManager.GetString(error, UiText.Culture), shownError);
        Assert.Equal(error is null ? 3 : 2, _modpacks.For(SteamGame.RootPath).Count);
    }

    [Fact]
    public async Task New_Cancelled_SavesNothing()
    {
        var vm = await CreateAsync();
        _library.SavedModpacks.Clear();

        await vm.NewCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.ModpackEdits);
        Assert.Empty(_library.SavedModpacks);
    }

    [Fact]
    public async Task Edit_IndependentModpack_OffersItsCopiesAndRelinksThem()
    {
        var copy = new ModpackCollection.Local(null, "Old list", [new WorkshopEntry(1005, "Weather")]);
        _saved[SteamGame.RootPath] = [Night with { Preset = new ModpackPreset("Old", ["Cars"], IsLinked: false), Collection = copy }];
        var vm = await CreateAsync();
        ModpackEditViewModel? shown = null;
        _dialogs.ModpackEditResult = FakeDialogService.EditsModpack(edit =>
        {
            shown = edit;
            Check(edit.Presets, "Driving");
            Check(edit.LocalCollections, "List");
        });
        Assert.True(vm.Selected?.IsIndependent);

        await vm.EditCommand.ExecuteAsync(null);

        Assert.NotNull(shown);
        Assert.Equal(("Old", UiText.Modpacks_OwnCopy), (shown.Presets[0].Text, shown.Presets[0].Detail));
        Assert.Equal(("Old list", UiText.Modpacks_OwnCopy), (shown.LocalCollections[0].Text, shown.LocalCollections[0].Detail));
        Assert.Equal(UiText.Modpacks_EditTitle, shown.Title);
        Assert.Equal(Linked("Driving", "Cars"), Saved("Night").Preset);
        Assert.Equal(Night.Collection, Saved("Night").Collection);
        Assert.False(vm.Selected?.IsIndependent);
    }

    [Fact]
    public async Task Edit_PreselectsTheCurrentChoices()
    {
        var vm = await CreateAsync();

        await SelectAsync(vm, "Night");
        await vm.EditCommand.ExecuteAsync(null);

        var edit = Assert.Single(_dialogs.ModpackEdits);
        Assert.Equal("Night", edit.Name);
        Assert.Equal(["Driving"], edit.Presets.Where(option => option.IsChecked).Select(option => option.Text));
        Assert.True(edit.IsLocalChecked);
        Assert.Equal(["List"], edit.LocalCollections.Where(option => option.IsChecked).Select(option => option.Text));
        Assert.True(edit.AcceptCommand.CanExecute(null));
    }

    [Fact]
    public async Task Rename_KeepsItsPlaceContentsAndSelection()
    {
        var vm = await CreateAsync();
        _dialogs.Accept("Server B42 ");

        await vm.RenameCommand.ExecuteAsync(null);

        Assert.Equal(["Server B42", "Night"], vm.Modpacks.Select(row => row.Name));
        Assert.Equal("Server B42", vm.Selected?.Name);
        Assert.Equal(Server.Preset, Saved("Server B42").Preset);
    }

    [Fact]
    public async Task Delete_AfterConfirm_RemovesItAndShowsTheNext()
    {
        var vm = await CreateAsync();
        _dialogs.ConfirmAnswers.Enqueue(true);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(Format(UiText.Modpacks_DeleteMessage, "Server"), Assert.Single(_dialogs.Confirms).Message);
        Assert.Equal(["Night"], _modpacks.For(SteamGame.RootPath).Select(modpack => modpack.Name));
        Assert.Equal("Night", vm.Selected?.Name);
        Assert.Equal([1002UL, 1005, 1003], vm.Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task Delete_Declined_KeepsIt()
    {
        var vm = await CreateAsync();

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(2, _modpacks.For(SteamGame.RootPath).Count);
    }

    // Use preset

    [Fact]
    public async Task UsePreset_Linked_AppliesItAndMakesItCurrent()
    {
        var vm = await CreateAsync();

        await vm.UsePresetCommand.ExecuteAsync(null);

        Assert.Equal("Survival", Assert.Single(_collections.Applies).Name);
        Assert.Equal("Survival", _defaults.Get(SteamGame.RootPath));
        Assert.Equal(Format(UiText.ModCollections_Applied, "Survival", 4), vm.StatusText);
        Assert.Empty(_collections.Saves);
    }

    [Fact]
    public async Task UsePreset_Independent_WritesTheCopyLinksItAndAppliesIt()
    {
        _saved[SteamGame.RootPath] = [Server with { Preset = new ModpackPreset("Old", ["Cars", "Core"], IsLinked: false) }];
        var vm = await CreateAsync();

        await vm.UsePresetCommand.ExecuteAsync(null);

        Assert.Equal(["Cars", "Core"], _collections.IdsOf("Old"));
        Assert.Equal(Linked("Old", "Cars", "Core"), Saved("Server").Preset);
        Assert.Equal("Old", Assert.Single(_collections.Applies).Name);
        Assert.Equal("Old", _defaults.Get(SteamGame.RootPath));
        Assert.Empty(_dialogs.Choices);
    }

    [Fact]
    public async Task UsePreset_IndependentNameTaken_NewName_WritesUnderIt()
    {
        _saved[SteamGame.RootPath] = [Server with { Preset = new ModpackPreset("Driving", ["Cars", "Weather"], IsLinked: false) }];
        var vm = await CreateAsync();
        _dialogs.ChoiceAnswers.Enqueue(ConfirmChoice.Alternative);
        _dialogs.Accept("Driving 2");

        await vm.UsePresetCommand.ExecuteAsync(null);

        Assert.Equal(Format(UiText.ModCollections_OverwriteMessage, "Driving"), Assert.Single(_dialogs.Choices).Prompt.Message);
        Assert.Equal(["Cars", "Weather"], _collections.IdsOf("Driving 2"));
        Assert.Equal(["Cars"], _collections.IdsOf("Driving"));
        Assert.Equal(Linked("Driving 2", "Cars", "Weather"), Saved("Server").Preset);
        Assert.Equal("Driving 2", Assert.Single(_collections.Applies).Name);
    }

    [Fact]
    public async Task UsePreset_IndependentNameTaken_Overwrite_ReplacesThatPreset()
    {
        _saved[SteamGame.RootPath] = [Server with { Preset = new ModpackPreset("Driving", ["Cars", "Weather"], IsLinked: false) }];
        var vm = await CreateAsync();
        _dialogs.ChoiceAnswers.Enqueue(ConfirmChoice.Confirm);

        await vm.UsePresetCommand.ExecuteAsync(null);

        Assert.Equal(["Cars", "Weather"], _collections.IdsOf("Driving"));
        Assert.Equal(Linked("Driving", "Cars", "Weather"), Saved("Server").Preset);
        Assert.Equal("Driving", Assert.Single(_collections.Applies).Name);
    }

    [Fact]
    public async Task UsePreset_IndependentNameTaken_Cancelled_WritesNothing()
    {
        _saved[SteamGame.RootPath] = [Server with { Preset = new ModpackPreset("Driving", ["Cars", "Weather"], IsLinked: false) }];
        var vm = await CreateAsync();

        await vm.UsePresetCommand.ExecuteAsync(null);

        Assert.Empty(_collections.Saves);
        Assert.Empty(_collections.Applies);
        Assert.False(Saved("Server").Preset.IsLinked);
    }

    [Fact]
    public async Task UsePreset_GameRunning_IsDisabled()
    {
        var vm = await CreateAsync();

        _runState.IsGameRunning = true;

        Assert.False(vm.UsePresetCommand.CanExecute(null));
    }

    // Links

    [Fact]
    public async Task PresetChangedInTheGame_TheLinkedCopyAndTheExtraItemsFollowOnTheNextRead()
    {
        var page = await CreatePageAsync();
        _collections.Collections[0] = ("Survival", ["Core", "Cars"]);

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(Linked("Survival", "Core", "Cars"), Saved("Server").Preset);
        Assert.Equal([1003UL], Saved("Server").ExtraItems);
        Assert.Equal([1001UL, 1004, 1003], page.Modpacks.Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task PresetChangedInTheGameWhileShown_SavesTheModpacksOnceWithoutReadingTheSubscriptions()
    {
        var page = await CreatePageAsync();
        _runState.IsGameRunning = true;
        _collections.Collections[0] = ("Survival", ["Core", "Cars"]);
        _library.SavedModpacks.Clear();
        var reads = _service.GetRequests.Count;

        _runState.IsGameRunning = false;

        Assert.Equal(Linked("Survival", "Core", "Cars"), Saved("Server").Preset);
        Assert.Equal([1003UL], Saved("Server").ExtraItems);
        Assert.Single(_library.SavedModpacks);
        Assert.Equal(reads, _service.GetRequests.Count);
        Assert.Equal([1001UL, 1004, 1003], page.Modpacks.Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task PresetGoneFromTheGame_MakesTheModpackIndependentOnTheNextRead()
    {
        var page = await CreatePageAsync();
        _collections.Collections.RemoveAll(collection => collection.Name == "Survival");

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(Server.Preset with { IsLinked = false }, Saved("Server").Preset);
        Assert.False(page.Modpacks.Selected?.IsPresetLinked);
    }

    [Fact]
    public async Task PresetReadFails_KeepsTheLink()
    {
        var page = await CreatePageAsync();
        _collections.Collections.Clear();
        _collections.GetError = () => TestErrors.Create(ErrorCode.ModCollectionCacheFolderUnresolved, SteamGame.RootPath);

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.True(Saved("Server").Preset.IsLinked);
        Assert.True(Saved("Night").Preset.IsLinked);
    }

    [Fact]
    public async Task PresetRenamedInTheLauncher_KeepsTheLinkAndTheCurrentPresetInOneSave()
    {
        _defaults.Save(SteamGame.RootPath, "Survival");
        var page = await CreatePageAsync();
        _library.SavedModpacks.Clear();
        _dialogs.Accept("Survival B42");

        await page.Collections.RenameCommand.ExecuteAsync(null);

        Assert.Equal(Linked("Survival B42", "Core", "Maps", "Cars", "Weather"), Saved("Server").Preset);
        Assert.Equal("Survival B42", _defaults.Get(SteamGame.RootPath));
        Assert.Single(_library.SavedModpacks);
    }

    [Fact]
    public async Task PresetEditedInTheLauncher_TheLinkedCopyFollowsInOneSave()
    {
        var page = await CreatePageAsync();
        page.Section = ModsSection.Collections;
        _library.SavedModpacks.Clear();

        await page.Collections.RemoveModCommand.ExecuteAsync(page.Collections.Entries.Single(entry => entry.Id == "Weather"));

        Assert.Equal(Linked("Survival", "Core", "Maps", "Cars"), Saved("Server").Preset);
        Assert.Single(_library.SavedModpacks);
    }

    [Fact]
    public async Task PresetDelete_Unused_AsksAsBefore()
    {
        var page = await CreatePageAsync();
        page.Collections.SelectCommand.Execute(page.Collections.Rows.Single(row => row.Name == "Spare"));
        _dialogs.ConfirmAnswers.Enqueue(true);

        await page.Collections.DeleteCommand.ExecuteAsync(null);

        Assert.Empty(_dialogs.Choices);
        Assert.Equal(["Spare"], _collections.Deletes);
    }

    [Theory]
    [InlineData(ConfirmChoice.Confirm)]
    [InlineData(ConfirmChoice.Alternative)]
    [InlineData(ConfirmChoice.Cancel)]
    public async Task PresetDelete_UsedByAModpack_OffersToKeepItOrDeleteItToo(ConfirmChoice choice)
    {
        var page = await CreatePageAsync();
        _dialogs.ChoiceAnswers.Enqueue(choice);

        await page.Collections.DeleteCommand.ExecuteAsync(null);

        var asked = Assert.Single(_dialogs.Choices);
        Assert.Equal(Format(UiText.Modpacks_PresetUsedMessage, "Survival"), asked.Prompt.Message);
        Assert.Equal(["Server"], asked.Prompt.Items);
        Assert.Empty(_dialogs.Confirms);
        string[] deleted = choice == ConfirmChoice.Cancel ? [] : ["Survival"];
        Assert.Equal(deleted, _collections.Deletes);
        switch (choice)
        {
            case ConfirmChoice.Confirm:
                Assert.Equal(Server.Preset with { IsLinked = false }, Saved("Server").Preset);
                break;
            case ConfirmChoice.Alternative:
                Assert.Equal(["Night"], _modpacks.For(SteamGame.RootPath).Select(modpack => modpack.Name));
                break;
            case ConfirmChoice.Cancel:
                Assert.True(Saved("Server").Preset.IsLinked);
                break;
        }
    }

    [Theory]
    [InlineData(ConfirmChoice.Confirm)]
    [InlineData(ConfirmChoice.Alternative)]
    [InlineData(ConfirmChoice.Cancel)]
    public async Task LocalCollectionDelete_UsedByModpacks_OffersToKeepThemOrDeleteThemToo(ConfirmChoice choice)
    {
        _saved[ManualGame.RootPath] = [Night with { Name = "Far" }];
        var page = await CreatePageAsync();
        _dialogs.ChoiceAnswers.Enqueue(choice);

        await page.LocalCollections.DeleteCommand.ExecuteAsync(null);

        var asked = Assert.Single(_dialogs.Choices);
        Assert.Equal(Format(UiText.Modpacks_LocalCollectionUsedMessage, "List"), asked.Prompt.Message);
        Assert.Equal(["Night", Format(UiText.Modpacks_UserInInstall, "Far", ManualGame.RootPath)], asked.Prompt.Items);
        Assert.Equal(choice == ConfirmChoice.Cancel, _localCollections.Find(ListId) is not null);
        switch (choice)
        {
            case ConfirmChoice.Confirm:
                Assert.Equal(new ModpackCollection.Local(null, "List", [new WorkshopEntry(1002, "Map Pack"), new WorkshopEntry(1005, "Weather")]), Saved("Night").Collection);
                Assert.Equal("Far", Assert.Single(_modpacks.For(ManualGame.RootPath)).Name);
                break;
            case ConfirmChoice.Alternative:
                Assert.Equal(["Server"], _modpacks.For(SteamGame.RootPath).Select(modpack => modpack.Name));
                Assert.Empty(_modpacks.For(ManualGame.RootPath));
                break;
            case ConfirmChoice.Cancel:
                Assert.Equal(Night.Collection, Saved("Night").Collection);
                break;
        }
    }

    [Fact]
    public async Task LocalCollectionChanged_TheShownModpackListsItsItemsAgain()
    {
        var vm = await CreateAsync();
        await SelectAsync(vm, "Night");

        _localCollections.Move(ListId, 1, 0);

        Assert.Equal([1005UL, 1002, 1003], vm.Rows.Select(row => row.Id));
    }

    // Share

    [Fact]
    public async Task Share_SteamCollection_CopiesTheModpackWithItsRecomputedExtraItems()
    {
        _saved[SteamGame.RootPath] = [Server with { ExtraItems = [1002, 1004] }];
        var vm = await CreateAsync();

        await vm.ShareCommand.ExecuteAsync(null);

        var shared = Shared();
        Assert.Equal("Server", shared.Name);
        Assert.Equal(new SharedModpackCollection.Steam(Pack), shared.Collection);
        Assert.Equal([1002UL, 1003, 1005], shared.ExtraItems);
        Assert.Equal("Survival", shared.Preset.Name);
        Assert.Equal(["Core", "Maps", "Cars", "Weather"], shared.Preset.ModIds);
        Assert.Equal(Format(UiText.Modpacks_Shared, "Server"), vm.StatusText);
    }

    [Fact]
    public async Task Share_LocalCollection_CopiesItsItemsAndCountsThePresetModsItCantBring()
    {
        _saved[SteamGame.RootPath] = [Night with { Preset = new ModpackPreset("Own", ["Cars", "Tweaks", "Gone"], IsLinked: false) }];
        var vm = await CreateAsync();

        await vm.ShareCommand.ExecuteAsync(null);

        var shared = Shared();
        var local = Assert.IsType<SharedModpackCollection.Local>(shared.Collection);
        Assert.Equal("List", local.Name);
        Assert.Equal([1002UL, 1005], local.Items);
        Assert.Equal([1003UL], shared.ExtraItems);
        Assert.Equal(Format(UiText.Modpacks_SharedPartly, "Night", 1, 1), vm.StatusText);
    }

    // Import

    private const string SteamText = "#pzlauncher-modpack 1\n#name\nShared\n#steam-collection\n9000\n#items\n1003\n1006\n#preset\nShared:Core;Cars;Other;\n";

    [Fact]
    public async Task Import_SteamCollection_WritesThePresetKeepsTheCollectionSavesTheModpackAndSubscribesItsMissingItemsInOneRun()
    {
        var vm = await CreateAsync();
        _clipboard.Text = SteamText;

        await ImportAsync(vm, SteamText);

        Assert.Equal(SteamText.Trim(), Assert.Single(_dialogs.Prompts).InitialText);
        var confirm = Assert.Single(_dialogs.Confirms);
        Assert.Equal(Format(UiText.Modpacks_ImportConfirmMessage, "Shared"), confirm.Message);
        Assert.Equal(
            [Format(UiText.Modpacks_ImportSteamCollection, "9000"), Format(UiText.Modpacks_ImportExtraItems, 2), Format(UiText.Modpacks_ImportPreset, "Shared", 3)],
            confirm.Items);
        Assert.Equal(["Core", "Cars", "Other"], _collections.IdsOf("Shared"));
        Assert.Equal(new WorkshopEntry(Pack, "Survival Pack"), Assert.Single(_history.Items));
        Assert.Equal(new Modpack("Shared", Linked("Shared", "Core", "Cars", "Other"), new ModpackCollection.Steam(Pack), [1003, 1006]), Saved("Shared"));
        Assert.Equal("Shared", vm.Selected?.Name);
        Assert.Equal([1001UL, 1004, 1003, 1006], vm.Rows.Select(row => row.Id));
        var run = Assert.Single(_runner.Runs);
        Assert.Equal((SteamGame.RootPath, WorkshopAction.Subscribe), (run.Root, run.Request.Action));
        Assert.Equal([1004UL, 1006], run.Request.Ids);
        Assert.Equal(Format(UiText.Modpacks_Imported, "Shared"), vm.StatusText);
    }

    [Fact]
    public async Task Import_LocalCollection_SavesItAndLinksTheModpackToIt()
    {
        var vm = await CreateAsync();

        await ImportAsync(vm, LocalText("Shared", "Friends", "Shared"));

        Assert.Equal(Format(UiText.Modpacks_ImportLocalCollection, "Friends", 2), Assert.Single(_dialogs.Confirms).Items[0]);
        var list = Assert.Single(_localCollections.All, collection => collection.Name == "Friends");
        Assert.Equal([1004UL, 1001], list.Items.Select(item => item.Id));
        Assert.Equal(new ModpackCollection.Local(list.Id, "Friends", list.Items), Saved("Shared").Collection);
        Assert.Empty(_history.Items);
        Assert.Equal([1004UL], Assert.Single(_runner.Runs).Request.Ids);
    }

    [Fact]
    public async Task Import_SteamNotRunning_KeepsWhatWasImportedAndShowsTheError()
    {
        var error = TestErrors.Create(ErrorCode.WorkshopSteamNotRunning);
        _runner.RunError = _ => error;
        var vm = await CreateAsync();

        await ImportAsync(vm, SteamText);

        Assert.Equal(ErrorMessages.ToUserText(error), Assert.Single(_dialogs.WorkshopActions).ErrorText);
        Assert.Equal(["Core", "Cars", "Other"], _collections.IdsOf("Shared"));
        Assert.True(Saved("Shared").Preset.IsLinked);
        Assert.Equal("Shared", vm.Selected?.Name);
    }

    [Theory]
    [InlineData("Server", "Shared", "Friends")]
    [InlineData("Shared", "Survival", "Friends")]
    [InlineData("Shared", "Shared", "List")]
    public async Task Import_NameClashCancelled_WritesNothing(string modpack, string preset, string list)
    {
        var vm = await CreateAsync();
        var modpacks = _modpacks.For(SteamGame.RootPath);
        var localCollections = _localCollections.All.ToList();

        await ImportAsync(vm, LocalText(modpack, list, preset));

        Assert.Single(_dialogs.Choices);
        Assert.Empty(_collections.Saves);
        Assert.Equal(modpacks, _modpacks.For(SteamGame.RootPath));
        Assert.Equal(localCollections, _localCollections.All);
        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public async Task Import_ModpackNameTaken_NewName_ImportsUnderItAndKeepsTheOther()
    {
        var vm = await CreateAsync();
        _dialogs.ChoiceAnswers.Enqueue(ConfirmChoice.Alternative);
        _dialogs.Accept(LocalText("Server", "Friends", "Shared"));
        _dialogs.Accept("Server 2");
        _dialogs.ConfirmAnswers.Enqueue(true);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(Format(UiText.Modpacks_OverwriteMessage, "Server"), Assert.Single(_dialogs.Choices).Prompt.Message);
        Assert.Equal(UiText.Modpacks_NameTaken, _dialogs.Prompts[1].Validate("Night"));
        Assert.Equal(["Server", "Night", "Server 2"], _modpacks.For(SteamGame.RootPath).Select(modpack => modpack.Name));
        Assert.Equal(new ModpackCollection.Steam(Pack), Saved("Server").Collection);
        Assert.Equal("Server 2", vm.Selected?.Name);
    }

    [Fact]
    public async Task Import_EveryNameTaken_Overwrite_ReplacesEachInPlaceAndLinkedModpacksFollow()
    {
        var vm = await CreateAsync();
        _dialogs.ChoiceAnswers.Enqueue(ConfirmChoice.Confirm);
        _dialogs.ChoiceAnswers.Enqueue(ConfirmChoice.Confirm);
        _dialogs.ChoiceAnswers.Enqueue(ConfirmChoice.Confirm);

        await ImportAsync(vm, LocalText("Server", "List", "Survival"));

        Assert.Equal(
            [
                Format(UiText.Modpacks_OverwriteMessage, "Server"),
                Format(UiText.ModCollections_OverwriteMessage, "Survival"),
                Format(UiText.LocalCollections_OverwriteMessage, "List"),
            ],
            _dialogs.Choices.Select(choice => choice.Prompt.Message));
        Assert.Equal(["Core"], _collections.IdsOf("Survival"));
        Assert.Equal([1004UL, 1001], _localCollections.Find(ListId)?.Items.Select(item => item.Id));
        Assert.Equal(["Server", "Night"], _modpacks.For(SteamGame.RootPath).Select(modpack => modpack.Name));
        var imported = Saved("Server");
        Assert.Equal(Linked("Survival", "Core"), imported.Preset);
        Assert.Equal(ListId, Assert.IsType<ModpackCollection.Local>(imported.Collection).Linked);
        Assert.Equal([1004UL, 1001], Assert.IsType<ModpackCollection.Local>(Saved("Night").Collection).Items.Select(item => item.Id));
    }

    [Fact]
    public async Task Import_GameRunning_IsDisabled()
    {
        var vm = await CreateAsync();

        _runState.IsGameRunning = true;

        Assert.False(vm.ImportCommand.CanExecute(null));
    }

    [Fact]
    public async Task Import_UnrelatedClipboardText_IsNotOffered()
    {
        var vm = await CreateAsync();
        _clipboard.Text = "Survival:Core;Maps;";

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal("", Assert.Single(_dialogs.Prompts).InitialText);
    }

    [Theory]
    [InlineData("#pzlauncher-modpack 2\n#name\nLater\n", nameof(UiText.Modpacks_ImportNewer))]
    [InlineData("#pzlauncher-modpack 1\n#name\nShared\n#preset\nShared:Core;\n", nameof(UiText.Modpacks_ImportIncomplete))]
    [InlineData("Survival:Core;Maps;", nameof(UiText.Modpacks_ImportNothing))]
    [InlineData(SteamText, null)]
    public async Task Import_PastedText_IsCheckedBeforeItCanBeAccepted(string text, string? error)
    {
        var vm = await CreateAsync();

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(error is null ? null : UiText.ResourceManager.GetString(error, UiText.Culture), Assert.Single(_dialogs.Prompts).Validate(text));
    }

    [Fact]
    public async Task Import_PresetNameTheGameRefuses_IsNamedBeforeItCanBeAccepted()
    {
        var vm = await CreateAsync();

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(
            Format(UiText.ModCollections_ImportNameInvalid, "a/b"),
            Assert.Single(_dialogs.Prompts).Validate("#pzlauncher-modpack 1\n#name\nShared\n#steam-collection\n9000\n#preset\na/b:Core;\n"));
    }

    [Fact]
    public async Task PresetImport_ModpackText_PointsToModpacks()
    {
        var page = await CreatePageAsync();
        _clipboard.Text = SteamText;

        await page.Collections.ImportCommand.ExecuteAsync(null);

        var prompt = Assert.Single(_dialogs.Prompts);
        Assert.Equal(UiText.ModCollections_ImportModpack, prompt.Validate(prompt.InitialText));
        Assert.Empty(_collections.Saves);
    }
}
