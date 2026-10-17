using CommunityToolkit.Mvvm.ComponentModel;
using PzLauncher.Core.Installs;
using PzLauncher.Desktop.ViewModels.Home;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.ViewModels.Mods;
using PzLauncher.Desktop.ViewModels.Settings;
using PzLauncher.Desktop.ViewModels.LaunchSettings;
using PzLauncher.Desktop.ViewModels.Update;

namespace PzLauncher.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    // The page that opened LaunchSettings: its tab stays highlighted and back returns there.
    private ShellPage _launchSettingsOrigin = ShellPage.Home;

    public MainViewModel(
        HomeViewModel home,
        InstallsViewModel installs,
        ModsViewModel mods,
        SettingsViewModel settings,
        LaunchSettingsViewModel launchSettings,
        LanguageMenuViewModel languageMenu,
        UpdateBannerViewModel updateBanner)
    {
        Home = home;
        Installs = installs;
        Mods = mods;
        Settings = settings;
        LaunchSettings = launchSettings;
        LanguageMenu = languageMenu;
        UpdateBanner = updateBanner;

        // Never unsubscribed: these view models are app-lifetime singletons.
        home.ChooseInstallRequested += (_, _) => CurrentPageKind = ShellPage.Installs;
        home.LaunchSettingsRequested += (_, install) => OpenLaunchSettings(install, ShellPage.Home);
        installs.HomeRequested += (_, _) => CurrentPageKind = ShellPage.Home;
        installs.LaunchSettingsRequested += (_, install) => OpenLaunchSettings(install, ShellPage.Installs);
        launchSettings.BackRequested += (_, _) => CurrentPageKind = _launchSettingsOrigin;
        launchSettings.HomeRequested += (_, _) => CurrentPageKind = ShellPage.Home;
    }

    public HomeViewModel Home { get; }

    public InstallsViewModel Installs { get; }

    public ModsViewModel Mods { get; }

    public SettingsViewModel Settings { get; }

    public LaunchSettingsViewModel LaunchSettings { get; }

    public LanguageMenuViewModel LanguageMenu { get; }

    public UpdateBannerViewModel UpdateBanner { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage), nameof(CurrentTab))]
    public partial ShellPage CurrentPageKind { get; set; }

    /// <summary>The highlighted title-bar tab: a sub-page highlights the tab it belongs to.</summary>
    public ShellPage CurrentTab
    {
        get => CurrentPageKind switch
        {
            ShellPage.Home => ShellPage.Home,
            ShellPage.Installs => ShellPage.Installs,
            ShellPage.Mods => ShellPage.Mods,
            ShellPage.Settings => ShellPage.Settings,
            ShellPage.LaunchSettings => _launchSettingsOrigin,
        };
        set => CurrentPageKind = value;
    }

    /// <summary>For a click on the highlighted tab, which changes no binding: leaves a sub-page for the tab's own page.</summary>
    public void ShowTabPage() => CurrentPageKind = CurrentTab;

    public ViewModelBase CurrentPage => CurrentPageKind switch
    {
        ShellPage.Home => Home,
        ShellPage.Installs => Installs,
        ShellPage.Mods => Mods,
        ShellPage.Settings => Settings,
        ShellPage.LaunchSettings => LaunchSettings,
    };

    private void OpenLaunchSettings(PzInstall install, ShellPage origin)
    {
        _launchSettingsOrigin = origin;
        LaunchSettings.Open(install);
        CurrentPageKind = ShellPage.LaunchSettings;
    }
}
