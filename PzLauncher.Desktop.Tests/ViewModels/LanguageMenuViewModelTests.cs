using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.ViewModels;

namespace PzLauncher.Desktop.Tests.ViewModels;

[Collection(UiTextCultureCollection.Name)]
public class LanguageMenuViewModelTests : UiTextCultureTest
{
    private readonly FakePreferenceStore _store = new();

    private LanguageMenuViewModel CreateViewModel() => new(Localization, _store);

    private static AppLanguage CheckedLanguage(LanguageMenuViewModel vm) =>
        vm.Languages.Single(option => option.IsCurrent).Language;

    [Fact]
    public void Constructor_ListsEveryLanguage_CurrentCheckedWithoutSaving()
    {
        Localization.SetLanguage(AppLanguage.TraditionalChinese);

        var vm = CreateViewModel();

        Assert.Equal(Enum.GetValues<AppLanguage>(), vm.Languages.Select(option => option.Language));
        Assert.Equal(AppLanguage.TraditionalChinese, CheckedLanguage(vm));
        Assert.Empty(_store.SavedLanguages);
    }

    [Theory]
    [InlineData(AppLanguage.SimplifiedChinese)]
    [InlineData(AppLanguage.English)]
    public void Select_Language_SwitchesChecksAndSavesOnlyAChange(AppLanguage chosen)
    {
        var vm = CreateViewModel();
        var changes = 0;
        Localization.LanguageChanged += (_, _) => changes++;

        vm.Languages.Single(option => option.Language == chosen).SelectCommand.Execute(null);

        AppLanguage[] expected = chosen == AppLanguage.English ? [] : [chosen];
        Assert.Equal(chosen, Localization.Current);
        Assert.Equal(chosen, CheckedLanguage(vm));
        Assert.Equal(expected.Length, changes);
        Assert.Equal(expected, _store.SavedLanguages);
        Assert.Empty(_store.SavedThemes);
    }
}
