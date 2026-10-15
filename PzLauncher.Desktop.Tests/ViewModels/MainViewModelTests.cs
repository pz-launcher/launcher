using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.ViewModels;
using PzLauncher.Desktop.ViewModels.Installs;

namespace PzLauncher.Desktop.Tests.ViewModels;

[Collection(UiTextCultureCollection.Name)]
public class MainViewModelTests : UiTextCultureTest
{
    private readonly FakeLanguagePreferenceStore _store = new();

    private MainViewModel CreateViewModel() =>
        new(new InstallsViewModel(new FakeInstallDetectionService(), new FakeFolderPicker(), new FakePlatformEnvironment(), Localization), Localization, _store);

    [Fact]
    public void Constructor_CurrentLanguage_IsSelectedWithoutSaving()
    {
        Localization.SetLanguage(AppLanguage.TraditionalChinese);

        var vm = CreateViewModel();

        Assert.Equal(Enum.GetValues<AppLanguage>(), vm.Languages.Select(option => option.Language));
        Assert.Equal(AppLanguage.TraditionalChinese, vm.SelectedLanguage.Language);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void SelectedLanguage_Changed_SwitchesLanguageAndSaves()
    {
        var vm = CreateViewModel();

        vm.SelectedLanguage = vm.Languages.Single(option => option.Language == AppLanguage.SimplifiedChinese);

        Assert.Equal(AppLanguage.SimplifiedChinese, Localization.Current);
        Assert.Equal([AppLanguage.SimplifiedChinese], _store.Saved);
    }
}
