using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.Themes;
using PzLauncher.Desktop.ViewModels.Settings;
using PzLauncher.Desktop.ViewModels.Update;

namespace PzLauncher.Desktop.Tests.ViewModels.Settings;

[Collection(UiTextCultureCollection.Name)]
public class SettingsViewModelTests : UiTextCultureTest
{
    private readonly FakePreferenceStore _store = new();
    private readonly FakeWindowPlacementService _windowPlacement = new();
    private readonly FakeJavaDiscoveryService _java = new();
    private readonly FakeSteamLaunchHookService _steam = new();

    private SettingsViewModel CreateViewModel(FakeThemeService theme, AfterLaunchAction? afterLaunch = null) =>
        new(
            theme,
            _store,
            _windowPlacement,
            new AfterLaunchSettings(_store, new UserPreferences(null, null, null) { AfterLaunch = afterLaunch }),
            TestJava.ViewModel(Localization, _java, _store),
            new UpdateSettingsViewModel(TestUpdates.Coordinator(new FakeUpdateService(), _store), Localization),
            TestProxy.ViewModel(Localization, _store),
            TestSteam.ViewModel(Localization, _store, _steam));

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void Constructor_CurrentTheme_IsSelectedWithoutApplyingOrSaving(AppTheme current)
    {
        var theme = new FakeThemeService(current);

        var vm = CreateViewModel(theme);

        Assert.Equal(current, vm.Theme);
        Assert.Equal(SettingsSection.General, vm.Section);
        Assert.Empty(theme.Applied);
        Assert.Empty(_store.SavedThemes);
        Assert.Equal(0, _java.ScanCalls);
    }

    [Theory]
    [InlineData(AppTheme.Light, AppTheme.Dark)]
    [InlineData(AppTheme.Dark, AppTheme.Light)]
    [InlineData(AppTheme.Light, AppTheme.Light)]
    [InlineData(AppTheme.Dark, AppTheme.Dark)]
    public void Theme_Chosen_AppliesAndSavesOnlyAChange(AppTheme current, AppTheme chosen)
    {
        var theme = new FakeThemeService(current);
        var vm = CreateViewModel(theme);

        vm.Theme = chosen;

        AppTheme[] expected = chosen == current ? [] : [chosen];
        Assert.Equal(chosen, theme.Current);
        Assert.Equal(expected, theme.Applied);
        Assert.Equal(expected, _store.SavedThemes);
        Assert.Empty(_store.SavedLanguages);
    }

    [Fact]
    public void AfterLaunch_NothingSaved_IsUnchosen()
    {
        var vm = CreateViewModel(new FakeThemeService(AppTheme.Light));

        Assert.Null(vm.AfterLaunch.Action);
    }

    [Fact]
    public void AfterLaunch_Chosen_SavesEachChangeButNotTheLoadedValue()
    {
        var vm = CreateViewModel(new FakeThemeService(AppTheme.Light), AfterLaunchAction.Minimize);

        Assert.Equal(AfterLaunchAction.Minimize, vm.AfterLaunch.Action);
        Assert.Empty(_store.SavedAfterLaunch);

        vm.AfterLaunch.Action = AfterLaunchAction.Close;
        vm.AfterLaunch.Action = AfterLaunchAction.Keep;

        Assert.Equal([AfterLaunchAction.Close, AfterLaunchAction.Keep], _store.SavedAfterLaunch);
    }

    [Fact]
    public void ResetWindowSize_Executed_ResetsThroughTheServiceOnce()
    {
        var vm = CreateViewModel(new FakeThemeService(AppTheme.Light));

        vm.ResetWindowSizeCommand.Execute(null);

        Assert.Equal(1, _windowPlacement.ResetCalls);
    }

    [Fact]
    public void Section_JavaSelectedRepeatedly_ScansOnce()
    {
        var vm = CreateViewModel(new FakeThemeService(AppTheme.Light));

        vm.Section = SettingsSection.Java;
        vm.Section = SettingsSection.Appearance;
        vm.Section = SettingsSection.Java;

        Assert.Equal(1, _java.ScanCalls);
        Assert.True(vm.Java.HasScanned);
    }

    [Fact]
    public void Section_SteamSelectedRepeatedly_ReadsSteamEachTime()
    {
        var vm = CreateViewModel(new FakeThemeService(AppTheme.Light));

        vm.Section = SettingsSection.Steam;
        vm.Section = SettingsSection.Appearance;
        vm.Section = SettingsSection.Steam;

        Assert.Equal(2, _steam.StatusRequests.Count);
        Assert.True(vm.Steam.IsAvailable);
    }
}
