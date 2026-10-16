using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.Themes;
using PzLauncher.Desktop.ViewModels;
using PzLauncher.Desktop.ViewModels.Home;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.ViewModels.Settings;
using PzLauncher.Desktop.ViewModels.VersionSettings;

namespace PzLauncher.Desktop.Tests.ViewModels;

[Collection(UiTextCultureCollection.Name)]
public class MainViewModelTests : UiTextCultureTest
{
    private MainViewModel CreateViewModel()
    {
        var store = new FakePreferenceStore();
        var installs = new InstallsViewModel(new FakeInstallDetectionService(), new FakeFolderPicker(), new FakePlatformEnvironment(), Localization);
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
    public void HomeVersionSettings_OpensSubPage_BackReturnsHome()
    {
        var vm = CreateViewModel();

        vm.Home.VersionSettingsCommand.Execute(null);

        Assert.Same(vm.VersionSettings, vm.CurrentPage);

        vm.VersionSettings.BackCommand.Execute(null);

        Assert.Same(vm.Home, vm.CurrentPage);
    }

    [Fact]
    public void HomeSelectVersion_NavigatesToInstalls()
    {
        var vm = CreateViewModel();

        vm.Home.SelectVersionCommand.Execute(null);

        Assert.Same(vm.Installs, vm.CurrentPage);
    }
}
