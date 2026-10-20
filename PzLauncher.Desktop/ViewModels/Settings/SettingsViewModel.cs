using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.Themes;
using PzLauncher.Desktop.ViewModels.Java;
using PzLauncher.Desktop.ViewModels.Net;
using PzLauncher.Desktop.ViewModels.Update;

namespace PzLauncher.Desktop.ViewModels.Settings;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IThemeService _themeService;
    private readonly IPreferenceStore _preferences;
    private readonly IWindowPlacementService _windowPlacement;
    private readonly AfterLaunchSettings _afterLaunch;

    public SettingsViewModel(
        IThemeService themeService,
        IPreferenceStore preferences,
        IWindowPlacementService windowPlacement,
        AfterLaunchSettings afterLaunch,
        JavaRuntimesViewModel java,
        UpdateSettingsViewModel updates,
        ProxySettingsViewModel proxy,
        SteamSettingsViewModel steam,
        ILocalizationService localization)
    {
        _themeService = themeService;
        _preferences = preferences;
        _windowPlacement = windowPlacement;
        _afterLaunch = afterLaunch;
        Java = java;
        Updates = updates;
        Proxy = proxy;
        Steam = steam;
        Theme = themeService.Current;
        RebuildAfterLaunchOptions();

        // Never unsubscribed: all of these are app-lifetime singletons.
        afterLaunch.PropertyChanged += OnAfterLaunchPropertyChanged;
        localization.LanguageChanged += (_, _) => RebuildAfterLaunchOptions();
    }

    public ObservableCollection<AfterLaunchOption> AfterLaunchOptions { get; } = [];

    public JavaRuntimesViewModel Java { get; }

    public UpdateSettingsViewModel Updates { get; }

    public ProxySettingsViewModel Proxy { get; }

    public SteamSettingsViewModel Steam { get; }

    [ObservableProperty] public partial SettingsSection Section { get; set; }

    [ObservableProperty] public partial AppTheme Theme { get; set; }

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
        if (value == _themeService.Current)
        {
            return;
        }

        _themeService.SetTheme(value);

        // SaveThemeAsync never throws for I/O, and a failed save must not undo the switch.
        _ = _preferences.SaveThemeAsync(value);
    }

    [RelayCommand]
    private void ResetWindowSize() => _windowPlacement.ResetToDefault();

    private void RebuildAfterLaunchOptions()
    {
        AfterLaunchOptions.Clear();
        foreach (var option in AfterLaunchSettings.Options())
        {
            AfterLaunchOptions.Add(new AfterLaunchOption(_afterLaunch, option));
        }
    }

    private void OnAfterLaunchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AfterLaunchSettings.Action))
        {
            foreach (var option in AfterLaunchOptions)
            {
                option.NotifyChecked();
            }
        }
    }
}
