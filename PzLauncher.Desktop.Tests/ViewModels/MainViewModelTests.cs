using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.Themes;
using PzLauncher.Desktop.ViewModels;
using PzLauncher.Desktop.ViewModels.Home;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.ViewModels.Settings;
using PzLauncher.Desktop.ViewModels.VersionSettings;
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
        return new(
            new HomeViewModel(installs, launcher, overrides, new FakeDialogService()),
            installs,
            new SettingsViewModel(new FakeThemeService(AppTheme.Light), store, new FakeWindowPlacementService(), java),
            new VersionSettingsViewModel(installs, launcher, overrides, java, new FakePlatformEnvironment(), Localization),
            new LanguageMenuViewModel(Localization, store));
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
    [InlineData(ShellPage.Settings)]
    public void Navigate_OtherPage_ShowsIt(ShellPage page)
    {
        var vm = CreateViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.CurrentPageKind = page;

        Assert.Same(page == ShellPage.Installs ? vm.Installs : vm.Settings, vm.CurrentPage);
        Assert.Contains(nameof(MainViewModel.CurrentPage), changed);
    }

    [Theory]
    [InlineData(ShellPage.Home, ShellPage.Home)]
    [InlineData(ShellPage.Installs, ShellPage.Installs)]
    [InlineData(ShellPage.Settings, ShellPage.Settings)]
    [InlineData(ShellPage.VersionSettings, ShellPage.Home)]
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
        vm.CurrentPageKind = ShellPage.VersionSettings;
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.CurrentTab = ShellPage.Installs;

        Assert.Same(vm.Installs, vm.CurrentPage);
        Assert.Contains(nameof(MainViewModel.CurrentTab), changed);
    }

    [Theory]
    [InlineData(ShellPage.VersionSettings, ShellPage.Home)]
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
    public async Task HomeVersionSettings_OpensTheInstallInUse_HomeTabStaysAndBackReturnsHome()
    {
        var vm = await CreateScannedViewModelAsync();

        vm.Home.VersionSettingsCommand.Execute(null);

        Assert.Same(vm.VersionSettings, vm.CurrentPage);
        Assert.Equal(SteamGame, vm.VersionSettings.Install);
        Assert.Equal(ShellPage.Home, vm.CurrentTab);

        vm.VersionSettings.BackCommand.Execute(null);

        Assert.Same(vm.Home, vm.CurrentPage);
    }

    [Fact]
    public async Task InstallsRowClick_OpensThatInstall_InstallsTabStaysAndBackReturnsToInstalls()
    {
        var vm = await CreateScannedViewModelAsync();
        vm.CurrentPageKind = ShellPage.Installs;

        vm.Installs.OpenSettingsCommand.Execute(SteamServer);

        Assert.Same(vm.VersionSettings, vm.CurrentPage);
        Assert.Equal(SteamServer, vm.VersionSettings.Install);
        Assert.Equal(ShellPage.Installs, vm.CurrentTab);

        vm.VersionSettings.BackCommand.Execute(null);

        Assert.Same(vm.Installs, vm.CurrentPage);
    }

    [Theory]
    [InlineData(ShellPage.Home)]
    [InlineData(ShellPage.Installs)]
    public async Task ShowTabPage_OnVersionSettings_ReturnsToWhereItWasOpenedFrom(ShellPage origin)
    {
        var vm = await CreateScannedViewModelAsync();
        vm.CurrentPageKind = origin;
        if (origin == ShellPage.Home)
        {
            vm.Home.VersionSettingsCommand.Execute(null);
        }
        else
        {
            vm.Installs.OpenSettingsCommand.Execute(ManualGame);
        }

        vm.ShowTabPage();

        Assert.Equal(origin, vm.CurrentPageKind);
    }

    [Fact]
    public async Task VersionSettings_ReopenedFromHomeAfterInstalls_FollowsTheNewOrigin()
    {
        var vm = await CreateScannedViewModelAsync();
        vm.CurrentPageKind = ShellPage.Installs;
        vm.Installs.OpenSettingsCommand.Execute(ManualGame);
        vm.CurrentTab = ShellPage.Settings;
        vm.CurrentTab = ShellPage.Home;

        vm.Home.VersionSettingsCommand.Execute(null);

        Assert.Equal(ShellPage.Home, vm.CurrentTab);
        vm.VersionSettings.BackCommand.Execute(null);
        Assert.Same(vm.Home, vm.CurrentPage);
    }

    [Fact]
    public async Task VersionSettingsUse_ShowsHomeWithThatInstallInUse()
    {
        var vm = await CreateScannedViewModelAsync();
        vm.CurrentPageKind = ShellPage.Installs;
        vm.Installs.OpenSettingsCommand.Execute(ManualGame);

        vm.VersionSettings.UseCommand.Execute(null);

        Assert.Same(vm.Home, vm.CurrentPage);
        Assert.Equal(ManualGame, vm.Installs.SelectedInstall);
    }

    [Fact]
    public void HomeSelectVersion_NavigatesToInstalls()
    {
        var vm = CreateViewModel();

        vm.Home.SelectVersionCommand.Execute(null);

        Assert.Same(vm.Installs, vm.CurrentPage);
    }
}
