using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Shell.Lifetime;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.Themes;
using PzLauncher.Desktop.ViewModels.Java;
using PzLauncher.Desktop.ViewModels.Net;
using PzLauncher.Desktop.ViewModels.Update;

namespace PzLauncher.Desktop.ViewModels.Settings;

public partial class SettingsViewModel(
    IThemeService themeService,
    IPreferenceStore preferences,
    IWindowPlacementService windowPlacement,
    AfterLaunchSettings afterLaunch,
    JavaRuntimesViewModel java,
    UpdateSettingsViewModel updates,
    ProxySettingsViewModel proxy,
    SteamSettingsViewModel steam) : ViewModelBase
{
    public AfterLaunchSettings AfterLaunch { get; } = afterLaunch;

    public JavaRuntimesViewModel Java { get; } = java;

    public UpdateSettingsViewModel Updates { get; } = updates;

    public ProxySettingsViewModel Proxy { get; } = proxy;

    public SteamSettingsViewModel Steam { get; } = steam;

    [ObservableProperty] public partial SettingsSection Section { get; set; }

    [ObservableProperty] public partial AppTheme Theme { get; set; } = themeService.Current;

    partial void OnSectionChanged(SettingsSection value)
    {
        switch (value)
        {
            case SettingsSection.Java:
                // Fire and forget: the scan reports its own state and errors through the Java view model.
                _ = Java.EnsureScannedAsync();
                break;
            case SettingsSection.Steam:
                // Re-read each time: the user may have changed the launch options in Steam meanwhile.
                _ = Steam.LoadAsync();
                break;
            case SettingsSection.General:
            case SettingsSection.Appearance:
            case SettingsSection.Updates:
            case SettingsSection.Proxy:
                break;
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
