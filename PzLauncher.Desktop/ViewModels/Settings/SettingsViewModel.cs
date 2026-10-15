using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.Themes;
using PzLauncher.Desktop.ViewModels.Java;

namespace PzLauncher.Desktop.ViewModels.Settings;

public partial class SettingsViewModel(
    IThemeService themeService,
    IPreferenceStore preferences,
    IWindowPlacementService windowPlacement,
    JavaRuntimesViewModel java) : ViewModelBase
{
    public JavaRuntimesViewModel Java { get; } = java;

    [ObservableProperty] public partial SettingsSection Section { get; set; }

    [ObservableProperty] public partial AppTheme Theme { get; set; } = themeService.Current;

    partial void OnSectionChanged(SettingsSection value)
    {
        if (value == SettingsSection.Java)
        {
            // Fire and forget: the scan reports its own state and errors through the Java view model.
            _ = Java.EnsureScannedAsync();
        }
    }

    partial void OnThemeChanged(AppTheme value)
    {
        if (value == themeService.Current)
        {
            return;
        }

        themeService.SetTheme(value);

        // SaveThemeAsync never throws for I/O, and a failed save must not undo the switch.
        _ = preferences.SaveThemeAsync(value);
    }

    [RelayCommand]
    private void ResetWindowSize() => windowPlacement.ResetToDefault();
}
