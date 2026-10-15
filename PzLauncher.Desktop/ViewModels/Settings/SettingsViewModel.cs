using CommunityToolkit.Mvvm.ComponentModel;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.Themes;

namespace PzLauncher.Desktop.ViewModels.Settings;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IThemeService _themeService;
    private readonly IPreferenceStore _preferences;

    public SettingsViewModel(IThemeService themeService, IPreferenceStore preferences)
    {
        _themeService = themeService;
        _preferences = preferences;
        Theme = themeService.Current;
    }

    [ObservableProperty] public partial SettingsSection Section { get; set; }

    [ObservableProperty] public partial AppTheme Theme { get; set; }

    partial void OnThemeChanged(AppTheme value)
    {
        if (value == _themeService.Current)
        {
            return;
        }

        _themeService.SetTheme(value);

        // SaveThemeAsync never throws for I/O, and a failed save must not undo the switch.
        _ = _preferences.SaveThemeAsync(value);
    }
}
