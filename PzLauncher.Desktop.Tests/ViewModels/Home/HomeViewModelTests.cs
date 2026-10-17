using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.ViewModels.Home;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.ViewModels.Mods;
using PzLauncher.Desktop.ViewModels.ZombieBuddy;
using static PzLauncher.Desktop.Tests.TestSupport.TestInstalls;
using static PzLauncher.Desktop.Tests.TestSupport.TestZombieBuddy;

namespace PzLauncher.Desktop.Tests.ViewModels.Home;

[Collection(UiTextCultureCollection.Name)]
public class HomeViewModelTests : UiTextCultureTest
{
    private readonly FakeInstallDetectionService _service = new();
    private readonly FakeGameLaunchService _launcher = new();
    private readonly FakeInstallOverridesService _overrides = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeZombieBuddyService _zombieBuddy = new();
    private readonly FakeZombieBuddyChannelService _channels = new();
    private readonly FakeZabricService _zabric = new();
    private readonly FakeZBSkipOnlineChecksService _skipOnlineChecks = new();
    private readonly FakeModCollectionDefaultsService _defaultCollections = new();
    private readonly FakeModCollectionService _collections = new();
    private readonly GameRunState _runState = new();
    private readonly ModCollectionOrderWriter _orderWriter;

    public HomeViewModelTests()
    {
        _orderWriter = new ModCollectionOrderWriter(_collections, _runState, new FakePlatformEnvironment(), new ManualTimeProvider());
        Installs = new(_service, new FakeFolderPicker(), new FakePlatformEnvironment(), Localization);
    }

    private InstallsViewModel Installs { get; }

    private HomeViewModel CreateViewModel() =>
        new(
            Installs, _launcher, _overrides, _dialogs, _zombieBuddy, _channels, _zabric, _skipOnlineChecks, _collections, _defaultCollections,
            new ModCollectionApplier(_collections, _dialogs), _orderWriter, _runState);

    private async Task<HomeViewModel> CreateActivatedViewModelAsync(params PzInstall[] installs)
    {
        _service.ScanResults.Enqueue(FakeInstallDetectionService.Found(installs));
        var vm = CreateViewModel();
        await vm.ActivateCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task SelectedInstall_FollowsTheInstallsPageSelection()
    {
        _service.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamGame, SteamServer));
        var vm = CreateViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await vm.ActivateCommand.ExecuteAsync(null);

        Assert.Equal(SteamGame, vm.SelectedInstall);
        Assert.False(vm.IsEmpty);

        changed.Clear();
        Installs.Use(SteamServer.RootPath);

        Assert.Equal(SteamServer, vm.SelectedInstall);
        Assert.Contains(nameof(HomeViewModel.SelectedInstall), changed);
    }

    [Fact]
    public async Task Activate_Scanning_IsBusyThenEmptyWhenNothingFound()
    {
        _service.ScanGate = new TaskCompletionSource();
        var vm = CreateViewModel();

        var activate = vm.ActivateCommand.ExecuteAsync(null);

        Assert.True(vm.IsScanning);
        Assert.False(vm.IsEmpty);

        _service.ScanGate.SetResult();
        await activate;

        Assert.False(vm.IsScanning);
        Assert.True(vm.IsEmpty);
        Assert.Null(vm.SelectedInstall);
    }

    [Fact]
    public async Task Activate_AlsoFromInstallsPage_ScansOnce()
    {
        var vm = CreateViewModel();

        await vm.ActivateCommand.ExecuteAsync(null);
        await Installs.ActivateCommand.ExecuteAsync(null);
        await vm.ActivateCommand.ExecuteAsync(null);

        Assert.Equal(1, _service.ScanCalls);
    }

    [Fact]
    public async Task LaunchAndSettingsCommands_EnabledOnlyForAGameInstall()
    {
        var vm = CreateViewModel();
        Assert.False(vm.LaunchCommand.CanExecute(null));
        Assert.False(vm.LaunchSettingsCommand.CanExecute(null));

        _service.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamServer, SteamGame));
        var raised = 0;
        vm.LaunchCommand.CanExecuteChanged += (_, _) => raised++;
        await vm.ActivateCommand.ExecuteAsync(null);

        Assert.Equal(SteamServer, vm.SelectedInstall);
        Assert.False(vm.LaunchCommand.CanExecute(null));
        Assert.False(vm.LaunchSettingsCommand.CanExecute(null));

        Installs.Use(SteamGame.RootPath);

        Assert.True(vm.LaunchCommand.CanExecute(null));
        Assert.True(vm.LaunchSettingsCommand.CanExecute(null));
        Assert.True(raised > 0);
    }

    [Fact]
    public async Task Launch_Succeeds_RunningForEveryGameUntilExit()
    {
        var vm = await CreateActivatedViewModelAsync(SteamGame, ManualGame);

        var launch = vm.LaunchCommand.ExecuteAsync(null);

        Assert.True(vm.IsGameRunning);
        Assert.True(_runState.IsGameRunning);
        Assert.False(vm.LaunchCommand.CanExecute(null));
        Installs.Use(ManualGame.RootPath);
        Assert.False(vm.LaunchCommand.CanExecute(null));
        Assert.True(vm.LaunchSettingsCommand.CanExecute(null));

        _launcher.Exit.SetResult(Result.Ok());
        await launch;

        Assert.False(vm.IsGameRunning);
        Assert.False(_runState.IsGameRunning);
        Assert.True(vm.LaunchCommand.CanExecute(null));
        Assert.Empty(_dialogs.Errors);
    }

    [Fact]
    public async Task IsGameRunning_FollowsTheSharedRunState()
    {
        var vm = await CreateActivatedViewModelAsync(SteamGame);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _runState.IsGameRunning = true;

        Assert.True(vm.IsGameRunning);
        Assert.False(vm.LaunchCommand.CanExecute(null));
        Assert.Contains(nameof(HomeViewModel.IsGameRunning), changed);
    }

    public static TheoryData<ZombieBuddyStatus> NothingToSetUp => [ZombieBuddyStatus.Unsupported, NotSubscribed, Initialized()];

    [Theory]
    [MemberData(nameof(NothingToSetUp))]
    public async Task Launch_ZombieBuddyNeedsNothing_LaunchesWithoutTheSetupDialog(ZombieBuddyStatus status)
    {
        _zombieBuddy.Status = _ => status;
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        await LaunchAndExitAsync(vm);

        Assert.Equal([SteamGame.RootPath], _zombieBuddy.StatusRequests);
        Assert.Empty(_dialogs.Setups);
        Assert.Single(_launcher.Launches);
    }

    [Fact]
    public async Task Launch_ZombieBuddyStatusFails_LaunchesAndTheLaunchReportsTheProblem()
    {
        _zombieBuddy.Status = _ => TestErrors.Create(ErrorCode.LaunchConfigMalformed, "ProjectZomboid64.json");
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        await LaunchAndExitAsync(vm);

        Assert.Empty(_dialogs.Setups);
        Assert.Single(_launcher.Launches);
    }

    [Theory]
    [InlineData(ZombieBuddyAction.Initialize, ZombieBuddySetupMode.Initialize)]
    [InlineData(ZombieBuddyAction.Remove, ZombieBuddySetupMode.Remove)]
    public async Task Launch_ZombieBuddyNeedsSetup_ShowsTheDialogOfferingLaunchThenLaunches(ZombieBuddyAction action, ZombieBuddySetupMode mode)
    {
        _zombieBuddy.Status = _ => action == ZombieBuddyAction.Initialize ? NotInitialized() : Leftovers;
        var launchedWhenShown = new List<int>();
        _dialogs.SetupResult = _ =>
        {
            launchedWhenShown.Add(_launcher.Launches.Count);
            return Task.FromResult(true);
        };
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        await LaunchAndExitAsync(vm);

        var setup = Assert.Single(_dialogs.Setups);
        Assert.Equal((mode, true), (setup.Mode, setup.OffersLaunch));
        Assert.Equal([0], launchedWhenShown);
        Assert.Single(_launcher.Launches);
    }

    [Fact]
    public async Task Launch_SetupDialogCancelled_DoesNotLaunch()
    {
        _zombieBuddy.Status = _ => NotInitialized();
        _dialogs.SetupResult = _ => Task.FromResult(false);
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.Setups);
        Assert.Empty(_zabric.Preloads);
        Assert.Empty(_skipOnlineChecks.Syncs);
        Assert.Empty(_launcher.Launches);
        Assert.True(vm.LaunchCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(ZombieBuddyChannel.Beta, ZombieBuddyChannel.Beta, true)]
    [InlineData(ZombieBuddyChannel.Stable, ZombieBuddyChannel.Stable, true)]
    [InlineData(ZombieBuddyChannel.Beta, ZombieBuddyChannel.Stable, false)]
    [InlineData(ZombieBuddyChannel.Stable, ZombieBuddyChannel.Beta, false)]
    public async Task Launch_Initialize_UsesTheSavedChannelWhenSubscribedElseTheSubscribedOne(
        ZombieBuddyChannel saved, ZombieBuddyChannel expected, bool bothSubscribed)
    {
        _channels.Set(SteamGame.RootPath, saved);
        ZombieBuddyItem[] subscribed = bothSubscribed ? [StableItem, BetaItem] : [expected == ZombieBuddyChannel.Beta ? BetaItem : StableItem];
        _zombieBuddy.Status = _ => NotInitialized(subscribed);
        _dialogs.SetupResult = FakeDialogService.RunsSetup();
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        await LaunchAndExitAsync(vm);

        Assert.Equal([(SteamGame.RootPath, expected)], _zombieBuddy.Initializations);
    }

    [Fact]
    public async Task Launch_PassesTheSelectedInstallsStoredOverrides()
    {
        var stored = new GameLaunchOverrides(@"C:\Java\jdk-25", 8192, 4096, "-XX:+UseZGC", "-debug", GarbageCollector.G1);
        _overrides.Set(ManualGame.RootPath, stored);
        _overrides.Set(SteamGame.RootPath, GameLaunchOverrides.Empty with { ExtraGameArgs = "-other" });
        var vm = await CreateActivatedViewModelAsync(SteamGame, ManualGame);
        Installs.Use(ManualGame.RootPath);

        var launch = vm.LaunchCommand.ExecuteAsync(null);
        _launcher.Exit.SetResult(Result.Ok());
        await launch;

        Assert.Equal([new LaunchGameRequest(ManualGame.RootPath, stored)], _launcher.Launches);
    }

    [Fact]
    public async Task Launch_AfterTheSetupGate_EnsuresZabricPreloadWithTheInstallsGameArgs()
    {
        _overrides.Set(SteamGame.RootPath, GameLaunchOverrides.Empty with { ExtraGameArgs = @"-cachedir=D:\pz" });
        _zombieBuddy.Status = _ => NotInitialized();
        var preloadsWhenShown = new List<int>();
        _dialogs.SetupResult = _ =>
        {
            preloadsWhenShown.Add(_zabric.Preloads.Count);
            return Task.FromResult(true);
        };
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        await LaunchAndExitAsync(vm);

        Assert.Equal([0], preloadsWhenShown);
        Assert.Equal([(SteamGame.RootPath, @"-cachedir=D:\pz")], _zabric.Preloads);
        Assert.Empty(_dialogs.Errors);
        Assert.Single(_launcher.Launches);
    }

    [Fact]
    public async Task Launch_ZabricPreloadFails_ShowsTheErrorThenLaunches()
    {
        var error = TestErrors.Create(ErrorCode.ZombieBuddyPreloadConfigInvalid, @"C:\Users\u\.zombie_buddy\config.json");
        _zabric.PreloadResult = error;
        var launchesWhenShown = new List<int>();
        _dialogs.Shown += () => launchesWhenShown.Add(_launcher.Launches.Count);
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        await LaunchAndExitAsync(vm);

        Assert.Equal([ErrorMessages.ToUserText(error)], _dialogs.Errors);
        Assert.Equal([0], launchesWhenShown);
        Assert.Single(_launcher.Launches);
    }

    public static TheoryData<Result<ZombieBuddyPreload>, bool> ZabricOutcomes => new()
    {
        { ZombieBuddyPreload.Written, true },
        { ZombieBuddyPreload.Current, true },
        { ZombieBuddyPreload.NotApplicable, false },
        { TestErrors.Create(ErrorCode.ZombieBuddyPreloadConfigInvalid, @"C:\Users\u\.zombie_buddy\config.json"), false },
    };

    [Theory]
    [MemberData(nameof(ZabricOutcomes))]
    public async Task Launch_AfterZabric_SyncsSkipOnlineChecksWithTheInstallsGameArgsAndWhetherZabricIsPreloaded(
        Result<ZombieBuddyPreload> zabric, bool zabricPreloaded)
    {
        _overrides.Set(SteamGame.RootPath, GameLaunchOverrides.Empty with { ExtraGameArgs = @"-cachedir=D:\pz" });
        _zabric.PreloadResult = zabric;
        var preloadsWhenSynced = new List<int>();
        _skipOnlineChecks.Sync = () =>
        {
            preloadsWhenSynced.Add(_zabric.Preloads.Count);
            return Result.Ok();
        };
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        await LaunchAndExitAsync(vm);

        Assert.Equal([1], preloadsWhenSynced);
        Assert.Equal([(SteamGame.RootPath, @"-cachedir=D:\pz", zabricPreloaded)], _skipOnlineChecks.Syncs);
        Assert.Single(_launcher.Launches);
    }

    [Fact]
    public async Task Launch_SkipOnlineChecksSyncFails_ShowsTheErrorThenLaunches()
    {
        var error = TestErrors.Create(ErrorCode.ZombieBuddyCopyFailed, @"E:\SteamLibrary\steamapps\common\ProjectZomboid\ZBSkipOnlineChecks.jar");
        _skipOnlineChecks.Sync = () => error;
        var launchesWhenShown = new List<int>();
        _dialogs.Shown += () => launchesWhenShown.Add(_launcher.Launches.Count);
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        await LaunchAndExitAsync(vm);

        Assert.Equal([ErrorMessages.ToUserText(error)], _dialogs.Errors);
        Assert.Equal([0], launchesWhenShown);
        Assert.Single(_launcher.Launches);
    }

    [Fact]
    public async Task Launch_Fails_ShowsTheErrorsUserTextAndStaysEnabled()
    {
        var error = TestErrors.Create(ErrorCode.LaunchJavaMissing, @"C:\Java\jdk-25\bin\javaw.exe");
        _launcher.Launch = _ => error;
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.Equal([ErrorMessages.ToUserText(error)], _dialogs.Errors);
        Assert.False(vm.IsGameRunning);
        Assert.True(vm.LaunchCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(ErrorCode.LaunchGameCrashed)]
    [InlineData(ErrorCode.LaunchGameExitedEarly)]
    public async Task Launch_GameExitsWithAFailure_ShowsItAfterTheButtonIsBack(ErrorCode code)
    {
        var crash = TestErrors.Create(code, @"E:\SteamLibrary\steamapps\common\ProjectZomboid\hs_err_pid4242.log");
        var vm = await CreateActivatedViewModelAsync(SteamGame);
        var runningWhenShown = new List<bool>();
        _dialogs.Shown += () => runningWhenShown.Add(vm.IsGameRunning);

        var launch = vm.LaunchCommand.ExecuteAsync(null);
        _launcher.Exit.SetResult(crash);
        await launch;

        Assert.Equal([ErrorMessages.ToUserText(crash)], _dialogs.Errors);
        Assert.Equal([false], runningWhenShown);
        Assert.True(vm.LaunchCommand.CanExecute(null));
    }

    private void ArrangeDefaultCollection(params string[] ids)
    {
        _collections.WithMods(TestMods.Mod(ZombieBuddyMod.Id), TestMods.Mod("ModA"), TestMods.Mod("ModB")).WithCollection("Pack", ids);
        _defaultCollections.Save(SteamGame.RootPath, "Pack");
    }

    [Fact]
    public async Task Launch_NoDefaultCollection_LeavesTheEnabledListAlone()
    {
        _collections.WithMods(TestMods.Mod("ModA")).WithCollection("Pack", "ModA");
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        await LaunchAndExitAsync(vm);

        Assert.Empty(_collections.Applies);
        Assert.Empty(_dialogs.Prompts);
        Assert.Single(_launcher.Launches);
    }

    [Fact]
    public async Task Launch_DefaultCollection_AppliesItWithTheInstallsGameArgsBeforeZombieBuddyAndTheLaunch()
    {
        ArrangeDefaultCollection(ZombieBuddyMod.Id, "ModA");
        _overrides.Set(SteamGame.RootPath, GameLaunchOverrides.Empty with { ExtraGameArgs = @"-cachedir=D:\pz" });
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        await LaunchAndExitAsync(vm);

        Assert.Equal([(SteamGame.RootPath, @"-cachedir=D:\pz", "Pack")], _collections.Applies);
        Assert.Equal([ZombieBuddyMod.Id, "ModA"], _collections.ActiveIds);
        Assert.Single(_launcher.Launches);
    }

    [Fact]
    public async Task Launch_DefaultCollectionWithZombieBuddy_KeepsTodaysSetupAndPreload()
    {
        ArrangeDefaultCollection(ZombieBuddyMod.Id, "ModA");
        _zombieBuddy.Status = _ => NotInitialized();
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        await LaunchAndExitAsync(vm);

        Assert.Equal(ZombieBuddySetupMode.Initialize, Assert.Single(_dialogs.Setups).Mode);
        Assert.Single(_zabric.Preloads);
        Assert.Single(_launcher.Launches);
    }

    [Fact]
    public async Task Launch_DefaultCollectionWithoutZombieBuddy_LeftoversOpenTheRemoveDialogAndSkipThePreload()
    {
        ArrangeDefaultCollection("ModA");
        _zombieBuddy.Status = _ => Initialized();
        _dialogs.SetupResult = FakeDialogService.RunsSetup();
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        await LaunchAndExitAsync(vm);

        var setup = Assert.Single(_dialogs.Setups);
        Assert.Equal((ZombieBuddySetupMode.Remove, true), (setup.Mode, setup.OffersLaunch));
        Assert.Equal([SteamGame.RootPath], _zombieBuddy.Removals);
        Assert.Empty(_zombieBuddy.Initializations);
        Assert.Empty(_zabric.Preloads);
        Assert.Empty(_skipOnlineChecks.Syncs);
        Assert.Single(_launcher.Launches);
    }

    [Fact]
    public async Task Launch_DefaultCollectionWithoutZombieBuddy_RemoveDialogCancelled_DoesNotLaunch()
    {
        ArrangeDefaultCollection("ModA");
        _zombieBuddy.Status = _ => Initialized();
        _dialogs.SetupResult = _ => Task.FromResult(false);
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.Setups);
        Assert.Empty(_launcher.Launches);
    }

    [Fact]
    public async Task Launch_DefaultCollectionWithoutZombieBuddy_NoLeftovers_NeitherInitializesNorPreloads()
    {
        ArrangeDefaultCollection("ModA");
        _zombieBuddy.Status = _ => NotInitialized();
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        await LaunchAndExitAsync(vm);

        Assert.Empty(_dialogs.Setups);
        Assert.Empty(_zabric.Preloads);
        Assert.Empty(_skipOnlineChecks.Syncs);
        Assert.Single(_launcher.Launches);
    }

    [Fact]
    public async Task Launch_DefaultCollectionApplyCancelled_DoesNotLaunch()
    {
        ArrangeDefaultCollection("ModA");
        _collections.ActiveIds = ["ModB"];
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.Prompts);
        Assert.Empty(_collections.Applies);
        Assert.Equal(["ModB"], _collections.ActiveIds);
        Assert.Empty(_zombieBuddy.StatusRequests);
        Assert.Empty(_launcher.Launches);
        Assert.True(vm.LaunchCommand.CanExecute(null));
    }

    [Fact]
    public async Task Launch_DefaultCollectionDropsMods_CancelOnTheNotice_DoesNotLaunch()
    {
        ArrangeDefaultCollection("ModA", "NotInstalled");
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.Confirms);
        Assert.Empty(_collections.Applies);
        Assert.Empty(_launcher.Launches);
    }

    [Fact]
    public async Task Launch_DefaultCollectionApplyFails_ShowsTheErrorThenLaunchesAsToday()
    {
        ArrangeDefaultCollection("ModA");
        var error = TestErrors.Create(ErrorCode.ModCollectionFileWriteFailed, @"C:\Users\Test\Zomboid\mods\default.txt");
        _collections.ApplyError = _ => error;
        _zombieBuddy.Status = _ => NotInitialized();
        var launchesWhenShown = new List<int>();
        _dialogs.Shown += () => launchesWhenShown.Add(_launcher.Launches.Count);
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        await LaunchAndExitAsync(vm);

        Assert.Equal([ErrorMessages.ToUserText(error)], _dialogs.Errors);
        Assert.Equal([0], launchesWhenShown);
        Assert.Equal(ZombieBuddySetupMode.Initialize, Assert.Single(_dialogs.Setups).Mode);
        Assert.Single(_zabric.Preloads);
        Assert.Single(_launcher.Launches);
    }

    [Fact]
    public async Task Launch_DefaultCollectionDeletedAfterTheListWasRead_ShowsNotFoundThenLaunches()
    {
        ArrangeDefaultCollection("ModA");
        var vm = await CreateActivatedViewModelAsync(SteamGame);
        Assert.True((await _collections.DeleteAsync(SteamGame.RootPath, "", "Pack", TestContext.Current.CancellationToken)).IsSuccess);

        await LaunchAndExitAsync(vm);

        Assert.Single(_dialogs.Errors);
        Assert.Single(_launcher.Launches);
    }

    [Fact]
    public async Task Launch_RightAfterAReorder_WritesItThenAppliesTheNewOrder()
    {
        ArrangeDefaultCollection("ModA", "ModB");
        var savesWhenApplied = new List<int>();
        _collections.ApplyError = _ =>
        {
            savesWhenApplied.Add(_collections.Saves.Count);
            return null;
        };
        var vm = await CreateActivatedViewModelAsync(SteamGame);
        _orderWriter.Queue(SteamGame.RootPath, "", "Pack", ["ModB", "ModA"]);

        await LaunchAndExitAsync(vm);

        Assert.Equal([1], savesWhenApplied);
        Assert.Equal(["ModB", "ModA"], _collections.ActiveIds);
        Assert.Single(_launcher.Launches);
    }

    [Fact]
    public async Task Launch_ReorderWriteFails_ShowsTheErrorThenAppliesAndLaunches()
    {
        ArrangeDefaultCollection("ModA", "ModB");
        var error = TestErrors.Create(ErrorCode.ModCollectionFileWriteFailed, @"C:\Users\Test\Zomboid\Lua\pz_modlist_settings.cfg");
        _collections.SaveError = _ => error;
        var vm = await CreateActivatedViewModelAsync(SteamGame);
        _orderWriter.Queue(SteamGame.RootPath, "", "Pack", ["ModB", "ModA"]);

        await LaunchAndExitAsync(vm);

        Assert.Equal([ErrorMessages.ToUserText(error)], _dialogs.Errors);
        Assert.Equal(["ModA", "ModB"], _collections.ActiveIds);
        Assert.Single(_launcher.Launches);
    }

    // Collection picker

    private static IEnumerable<string?> ChoiceNames(HomeViewModel vm) => vm.CollectionChoices.Select(choice => choice.Name);

    [Fact]
    public async Task Collections_ListTheInstallsCollectionsThenNone_WithTheCurrentOneSelected()
    {
        _collections.WithCollection("Pack", "ModA").WithCollection("Other", "ModB");
        _defaultCollections.Save(SteamGame.RootPath, "Other");
        _overrides.Set(SteamGame.RootPath, GameLaunchOverrides.Empty with { ExtraGameArgs = @"-cachedir=D:\pz" });

        var vm = await CreateActivatedViewModelAsync(SteamGame);

        Assert.Equal(["Pack", "Other", null], ChoiceNames(vm));
        Assert.Equal(new CollectionChoice("Other"), vm.CollectionSelection);
        Assert.Equal([(SteamGame.RootPath, @"-cachedir=D:\pz")], _collections.GetRequests);
        Assert.True(vm.IsGameSelected);
        Assert.True(vm.CanChooseCollection);
    }

    [Fact]
    public async Task Collections_NoCurrentCollection_SelectsNone()
    {
        _collections.WithCollection("Pack", "ModA");

        var vm = await CreateActivatedViewModelAsync(SteamGame);

        Assert.Equal(CollectionChoice.None, vm.CollectionSelection);
        Assert.True(vm.CollectionSelection?.IsNone);
    }

    [Fact]
    public async Task CollectionSelection_SetsAndClearsTheCurrentCollection_WithoutWritingFiles()
    {
        _collections.WithMods(TestMods.Mod("ModA")).WithCollection("Pack", "ModA");
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        vm.CollectionSelection = vm.CollectionChoices[0];

        Assert.Equal("Pack", _defaultCollections.Get(SteamGame.RootPath));

        vm.CollectionSelection = null;

        Assert.Equal(new CollectionChoice("Pack"), vm.CollectionSelection);

        vm.CollectionSelection = CollectionChoice.None;

        Assert.Null(_defaultCollections.Get(SteamGame.RootPath));
        Assert.Empty(_collections.Saves);
        Assert.Empty(_collections.Applies);
        Assert.Empty(_collections.Deletes);
    }

    [Fact]
    public async Task Launch_NoneChosen_LeavesTheEnabledListAlone()
    {
        ArrangeDefaultCollection("ModA");
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        vm.CollectionSelection = CollectionChoice.None;
        await LaunchAndExitAsync(vm);

        Assert.Empty(_collections.Applies);
        Assert.Single(_launcher.Launches);
    }

    [Fact]
    public async Task Launch_CollectionChosen_AppliesIt()
    {
        _collections.WithMods(TestMods.Mod("ModA")).WithCollection("Pack", "ModA");
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        vm.CollectionSelection = new CollectionChoice("Pack");
        await LaunchAndExitAsync(vm);

        Assert.Equal([(SteamGame.RootPath, "", "Pack")], _collections.Applies);
    }

    [Fact]
    public async Task Collections_SavedNameGone_ShowsNoneAndForgetsIt_SoTheLaunchDoesntApply()
    {
        _collections.WithCollection("Pack", "ModA");
        _defaultCollections.Save(SteamGame.RootPath, "Deleted in-game");

        var vm = await CreateActivatedViewModelAsync(SteamGame);

        Assert.Equal(CollectionChoice.None, vm.CollectionSelection);
        Assert.Null(_defaultCollections.Get(SteamGame.RootPath));

        await LaunchAndExitAsync(vm);

        Assert.Empty(_collections.Applies);
        Assert.Empty(_dialogs.Errors);
        Assert.Single(_launcher.Launches);
    }

    [Fact]
    public async Task Collections_ReadFails_OffersOnlyNone_WithoutAnError()
    {
        _collections.WithCollection("Pack", "ModA");
        _defaultCollections.Save(SteamGame.RootPath, "Pack");
        _collections.GetError = () => TestErrors.Create(ErrorCode.ModCollectionCacheFolderUnresolved, SteamGame.RootPath);

        var vm = await CreateActivatedViewModelAsync(SteamGame);

        Assert.Equal([null], ChoiceNames(vm));
        Assert.Equal(CollectionChoice.None, vm.CollectionSelection);
        Assert.Empty(_dialogs.Errors);
        Assert.Equal("Pack", _defaultCollections.Get(SteamGame.RootPath));
    }

    [Fact]
    public async Task Collections_DedicatedServer_HiddenAndNotRead()
    {
        _collections.WithCollection("Pack", "ModA");

        var vm = await CreateActivatedViewModelAsync(SteamServer);

        Assert.False(vm.IsGameSelected);
        Assert.False(vm.CanChooseCollection);
        Assert.Equal([null], ChoiceNames(vm));
        Assert.Empty(_collections.GetRequests);
    }

    [Fact]
    public async Task Collections_GameRunning_DisablesTheChoice_AndTheExitRereadsThem()
    {
        _collections.WithCollection("Pack", "ModA");
        var vm = await CreateActivatedViewModelAsync(SteamGame);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _runState.IsGameRunning = true;

        Assert.False(vm.CanChooseCollection);
        Assert.Contains(nameof(HomeViewModel.CanChooseCollection), changed);

        _collections.WithCollection("Added in-game", "ModB");
        _runState.IsGameRunning = false;

        Assert.True(vm.CanChooseCollection);
        Assert.Equal(["Pack", "Added in-game", null], ChoiceNames(vm));
    }

    [Fact]
    public async Task Collections_InstallChanges_ShowsTheNewInstallsCurrentCollection()
    {
        _collections.WithCollection("Pack", "ModA").WithCollection("Other", "ModB");
        _defaultCollections.Save(ManualGame.RootPath, "Other");
        var vm = await CreateActivatedViewModelAsync(SteamGame, ManualGame);

        Installs.Use(ManualGame.RootPath);

        Assert.Equal(ManualGame.RootPath, _collections.GetRequests[^1].Root);
        Assert.Equal(new CollectionChoice("Other"), vm.CollectionSelection);
    }

    [Fact]
    public async Task Collections_ActivateAgain_ShowsAChangeMadeOnTheModsPage()
    {
        _collections.WithCollection("Pack", "ModA");
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        _collections.WithCollection("Other", "ModB");
        _defaultCollections.Save(SteamGame.RootPath, "Other");
        await vm.ActivateCommand.ExecuteAsync(null);

        Assert.Equal(["Pack", "Other", null], ChoiceNames(vm));
        Assert.Equal(new CollectionChoice("Other"), vm.CollectionSelection);
    }

    [Fact]
    public async Task RefreshCollections_RereadsTheFiles()
    {
        _collections.WithCollection("Pack", "ModA");
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        _collections.WithCollection("Added in-game", "ModB");
        await vm.RefreshCollectionsCommand.ExecuteAsync(null);

        Assert.Equal(2, _collections.GetRequests.Count);
        Assert.Equal(["Pack", "Added in-game", null], ChoiceNames(vm));
    }

    private async Task LaunchAndExitAsync(HomeViewModel vm)
    {
        var launch = vm.LaunchCommand.ExecuteAsync(null);
        _launcher.Exit.TrySetResult(Result.Ok());
        await launch;
    }

    [Fact]
    public void ChooseInstall_RaisesRequest()
    {
        var vm = CreateViewModel();
        var raised = 0;
        vm.ChooseInstallRequested += (_, _) => raised++;

        vm.ChooseInstallCommand.Execute(null);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task LaunchSettings_RequestsTheInstallInUse()
    {
        var vm = await CreateActivatedViewModelAsync(SteamGame, ManualGame);
        Installs.Use(ManualGame.RootPath);
        var requested = new List<PzInstall>();
        vm.LaunchSettingsRequested += (_, install) => requested.Add(install);

        vm.LaunchSettingsCommand.Execute(null);

        Assert.Equal([ManualGame], requested);
    }
}
