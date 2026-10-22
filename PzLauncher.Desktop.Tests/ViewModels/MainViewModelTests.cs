using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.Themes;
using PzLauncher.Desktop.ViewModels;
using PzLauncher.Desktop.ViewModels.Home;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.ViewModels.Mods;
using PzLauncher.Desktop.ViewModels.Settings;
using PzLauncher.Desktop.ViewModels.LaunchSettings;
using PzLauncher.Desktop.ViewModels.Update;
using static PzLauncher.Desktop.Tests.TestSupport.TestInstalls;

namespace PzLauncher.Desktop.Tests.ViewModels;

[Collection(UiTextCultureCollection.Name)]
public class MainViewModelTests : UiTextCultureTest
{
    private readonly FakeInstallDetectionService _detection = new();

    private MainViewModel CreateViewModel()
    {
        var store = new FakePreferenceStore();
        var installs = new InstallsViewModel(_detection, new FakeFolderPicker(), new FakePlatformEnvironment(), Localization);
        var java = TestJava.ViewModel(Localization, new FakeJavaDiscoveryService(), store);
        var launcher = new FakeGameLaunchService();
        var overrides = new FakeInstallOverridesService();
        var zombieBuddy = new FakeZombieBuddyService();
        var channels = new FakeZombieBuddyChannelService();
        var runState = new GameRunState();
        var dialogs = new FakeDialogService();
        var collections = new FakeModCollectionService();
        var defaults = new FakeModCollectionDefaultsService();
        var applier = new ModCollectionApplier(collections, dialogs);
        var orderWriter = new ModCollectionOrderWriter(collections, runState, new FakePlatformEnvironment(), new ManualTimeProvider());
        var updates = TestUpdates.Coordinator(new FakeUpdateService(), store, runState: runState);
        var workshop = new FakeWorkshopService();
        var history = TestWorkshop.History();
        var userCollections = TestWorkshop.UserCollections();
        var localCollections = TestWorkshop.LocalCollectionStore();
        var favorites = TestWorkshop.Favorites();
        var afterLaunch = new AfterLaunchSettings(store, new(null, null, null), new FakePlatformEnvironment());
        return new(
            new HomeViewModel(
                installs, launcher, overrides, dialogs, zombieBuddy, channels, new FakeZombieBuddyModsService(),
                collections, defaults, applier, orderWriter, TestWorkshop.LaunchCheck(new FakeWorkshopService()),
                new FakeWorkshopService(), new FakeWorkshopActionRunner(), runState,
                new AfterLaunchFlow(afterLaunch, new FakeLauncherWindow(), new FakeAppLifetime(), dialogs, launcher)),
            installs,
            new ModsViewModel(
                installs, collections, overrides, defaults, applier, dialogs, new FakeClipboardService(), runState, orderWriter,
                workshop, workshop, new FakeWorkshopActionRunner(), new FakeUrlLauncher(), history, userCollections, favorites,
                new WorkshopTitles(history, userCollections, favorites, localCollections), localCollections, TestWorkshop.ModpackStoreOf(localCollections), Localization),
            new SettingsViewModel(
                new FakeThemeService(AppTheme.Light),
                store,
                new FakeWindowPlacementService(),
                afterLaunch,
                java,
                new UpdateSettingsViewModel(updates, Localization),
                TestProxy.ViewModel(Localization, store),
                TestSteam.ViewModel(Localization, store)),
            new LaunchSettingsViewModel(
                installs, launcher, overrides, java, zombieBuddy, channels, runState, dialogs, new FakePlatformEnvironment(), Localization),
            new LanguageMenuViewModel(Localization, store),
            new UpdateBannerViewModel(updates, runState, new FakeUrlLauncher(), Localization));
    }

    private async Task<MainViewModel> CreateScannedViewModelAsync()
    {
        _detection.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamGame, ManualGame, SteamServer));
        var vm = CreateViewModel();
        await vm.Installs.ActivateCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public void Constructor_StartsOnHome()
    {
        var vm = CreateViewModel();

        Assert.Equal(ShellPage.Home, vm.CurrentPageKind);
        Assert.Same(vm.Home, vm.CurrentPage);
    }

    [Theory]
    [InlineData(ShellPage.Installs)]
    [InlineData(ShellPage.Mods)]
    [InlineData(ShellPage.Settings)]
    public void Navigate_OtherPage_ShowsIt(ShellPage page)
    {
        var vm = CreateViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.CurrentPageKind = page;

        Assert.Same(
            page switch
            {
                ShellPage.Home => vm.Home,
                ShellPage.Installs => vm.Installs,
                ShellPage.Mods => vm.Mods,
                ShellPage.Settings => vm.Settings,
                ShellPage.LaunchSettings => vm.LaunchSettings,
            },
            vm.CurrentPage);
        Assert.Contains(nameof(MainViewModel.CurrentPage), changed);
    }

    [Theory]
    [InlineData(ShellPage.Home, ShellPage.Home)]
    [InlineData(ShellPage.Installs, ShellPage.Installs)]
    [InlineData(ShellPage.Mods, ShellPage.Mods)]
    [InlineData(ShellPage.Settings, ShellPage.Settings)]
    [InlineData(ShellPage.LaunchSettings, ShellPage.Home)]
    public void CurrentTab_SubPageHighlightsItsTab(ShellPage page, ShellPage tab)
    {
        var vm = CreateViewModel();

        vm.CurrentPageKind = page;

        Assert.Equal(tab, vm.CurrentTab);
    }

    [Fact]
    public void CurrentTab_SetFromSubPage_NavigatesThere()
    {
        var vm = CreateViewModel();
        vm.CurrentPageKind = ShellPage.LaunchSettings;
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.CurrentTab = ShellPage.Installs;

        Assert.Same(vm.Installs, vm.CurrentPage);
        Assert.Contains(nameof(MainViewModel.CurrentTab), changed);
    }

    [Theory]
    [InlineData(ShellPage.LaunchSettings, ShellPage.Home)]
    [InlineData(ShellPage.Home, ShellPage.Home)]
    [InlineData(ShellPage.Installs, ShellPage.Installs)]
    [InlineData(ShellPage.Settings, ShellPage.Settings)]
    public void ShowTabPage_ShowsTheHighlightedTabsOwnPage(ShellPage page, ShellPage expected)
    {
        var vm = CreateViewModel();
        vm.CurrentPageKind = page;

        vm.ShowTabPage();

        Assert.Equal(expected, vm.CurrentPageKind);
    }

    [Fact]
    public async Task HomeLaunchSettings_OpensTheInstallInUse_HomeTabStaysAndBackReturnsHome()
    {
        var vm = await CreateScannedViewModelAsync();

        vm.Home.LaunchSettingsCommand.Execute(null);

        Assert.Same(vm.LaunchSettings, vm.CurrentPage);
        Assert.Equal(SteamGame, vm.LaunchSettings.Install);
        Assert.Equal(ShellPage.Home, vm.CurrentTab);

        vm.LaunchSettings.BackCommand.Execute(null);

        Assert.Same(vm.Home, vm.CurrentPage);
    }

    [Fact]
    public async Task InstallsRowClick_ShowsHome()
    {
        var vm = await CreateScannedViewModelAsync();
        vm.CurrentPageKind = ShellPage.Installs;

        vm.Installs.ChooseCommand.Execute(ManualGame);

        Assert.Same(vm.Home, vm.CurrentPage);
    }

    [Fact]
    public async Task InstallsLaunchSettings_OpensThatInstall_InstallsTabStaysAndBackReturnsToInstalls()
    {
        var vm = await CreateScannedViewModelAsync();
        vm.CurrentPageKind = ShellPage.Installs;

        vm.Installs.OpenSettingsCommand.Execute(SteamServer);

        Assert.Same(vm.LaunchSettings, vm.CurrentPage);
        Assert.Equal(SteamServer, vm.LaunchSettings.Install);
        Assert.Equal(ShellPage.Installs, vm.CurrentTab);

        vm.LaunchSettings.BackCommand.Execute(null);

        Assert.Same(vm.Installs, vm.CurrentPage);
    }

    [Theory]
    [InlineData(ShellPage.Home)]
    [InlineData(ShellPage.Installs)]
    public async Task ShowTabPage_OnLaunchSettings_ReturnsToWhereItWasOpenedFrom(ShellPage origin)
    {
        var vm = await CreateScannedViewModelAsync();
        vm.CurrentPageKind = origin;
        if (origin == ShellPage.Home)
        {
            vm.Home.LaunchSettingsCommand.Execute(null);
        }
        else
        {
            vm.Installs.OpenSettingsCommand.Execute(ManualGame);
        }

        vm.ShowTabPage();

        Assert.Equal(origin, vm.CurrentPageKind);
    }

    [Fact]
    public async Task LaunchSettings_ReopenedFromHomeAfterInstalls_FollowsTheNewOrigin()
    {
        var vm = await CreateScannedViewModelAsync();
        vm.CurrentPageKind = ShellPage.Installs;
        vm.Installs.OpenSettingsCommand.Execute(ManualGame);
        vm.CurrentTab = ShellPage.Settings;
        vm.CurrentTab = ShellPage.Home;

        vm.Home.LaunchSettingsCommand.Execute(null);

        Assert.Equal(ShellPage.Home, vm.CurrentTab);
        vm.LaunchSettings.BackCommand.Execute(null);
        Assert.Same(vm.Home, vm.CurrentPage);
    }

    [Fact]
    public async Task LaunchSettingsUse_ShowsHomeWithThatInstallInUse()
    {
        var vm = await CreateScannedViewModelAsync();
        vm.CurrentPageKind = ShellPage.Installs;
        vm.Installs.OpenSettingsCommand.Execute(ManualGame);

        vm.LaunchSettings.UseCommand.Execute(null);

        Assert.Same(vm.Home, vm.CurrentPage);
        Assert.Equal(ManualGame, vm.Installs.SelectedInstall);
    }

    [Fact]
    public void HomeChooseInstall_NavigatesToInstalls()
    {
        var vm = CreateViewModel();

        vm.Home.ChooseInstallCommand.Execute(null);

        Assert.Same(vm.Installs, vm.CurrentPage);
    }
}
