using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.Themes;
using PzLauncher.Desktop.ViewModels;
using PzLauncher.Desktop.ViewModels.Home;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.ViewModels.Settings;

namespace PzLauncher.Desktop.Tests.ViewModels;

[Collection(UiTextCultureCollection.Name)]
public class MainViewModelTests : UiTextCultureTest
{
    private MainViewModel CreateViewModel()
    {
        var store = new FakePreferenceStore();
        var installs = new InstallsViewModel(new FakeInstallDetectionService(), new FakeFolderPicker(), new FakePlatformEnvironment(), Localization);
        return new(
            new HomeViewModel(installs),
            installs,
            new SettingsViewModel(new FakeThemeService(AppTheme.Light), store),
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

    [Fact]
    public void HomeSelectVersion_NavigatesToInstalls()
    {
        var vm = CreateViewModel();

        vm.Home.SelectVersionCommand.Execute(null);

        Assert.Same(vm.Installs, vm.CurrentPage);
    }
}
