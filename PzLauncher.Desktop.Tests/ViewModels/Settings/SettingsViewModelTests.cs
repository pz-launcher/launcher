using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.Themes;
using PzLauncher.Desktop.ViewModels.Settings;

namespace PzLauncher.Desktop.Tests.ViewModels.Settings;

public class SettingsViewModelTests
{
    private readonly FakePreferenceStore _store = new();
    private readonly FakeWindowPlacementService _windowPlacement = new();

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void Constructor_CurrentTheme_IsSelectedWithoutApplyingOrSaving(AppTheme current)
    {
        var theme = new FakeThemeService(current);

        var vm = new SettingsViewModel(theme, _store, _windowPlacement);

        Assert.Equal(current, vm.Theme);
        Assert.Equal(SettingsSection.Appearance, vm.Section);
        Assert.Empty(theme.Applied);
        Assert.Empty(_store.SavedThemes);
    }

    [Theory]
    [InlineData(AppTheme.Light, AppTheme.Dark)]
    [InlineData(AppTheme.Dark, AppTheme.Light)]
    [InlineData(AppTheme.Light, AppTheme.Light)]
    [InlineData(AppTheme.Dark, AppTheme.Dark)]
    public void Theme_Chosen_AppliesAndSavesOnlyAChange(AppTheme current, AppTheme chosen)
    {
        var theme = new FakeThemeService(current);
        var vm = new SettingsViewModel(theme, _store, _windowPlacement);

        vm.Theme = chosen;

        AppTheme[] expected = chosen == current ? [] : [chosen];
        Assert.Equal(chosen, theme.Current);
        Assert.Equal(expected, theme.Applied);
        Assert.Equal(expected, _store.SavedThemes);
        Assert.Empty(_store.SavedLanguages);
    }

    [Fact]
    public void ResetWindowSize_Executed_ResetsThroughTheServiceOnce()
    {
        var vm = new SettingsViewModel(new FakeThemeService(AppTheme.Light), _store, _windowPlacement);

        vm.ResetWindowSizeCommand.Execute(null);

        Assert.Equal(1, _windowPlacement.ResetCalls);
    }
}
