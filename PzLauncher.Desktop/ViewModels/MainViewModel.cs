using CommunityToolkit.Mvvm.ComponentModel;
using PzLauncher.Core.Installs;
using PzLauncher.Desktop.ViewModels.Home;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.ViewModels.Settings;
using PzLauncher.Desktop.ViewModels.VersionSettings;

namespace PzLauncher.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    // The page that opened VersionSettings: its tab stays highlighted and back returns there.
    private ShellPage _versionSettingsOrigin = ShellPage.Home;

    public MainViewModel(
        HomeViewModel home,
        InstallsViewModel installs,
        SettingsViewModel settings,
        VersionSettingsViewModel versionSettings,
        LanguageMenuViewModel languageMenu)
    {
        Home = home;
        Installs = installs;
        Settings = settings;
        VersionSettings = versionSettings;
        LanguageMenu = languageMenu;

        // Never unsubscribed: these view models are app-lifetime singletons.
        home.SelectVersionRequested += (_, _) => CurrentPageKind = ShellPage.Installs;
        home.VersionSettingsRequested += (_, install) => OpenVersionSettings(install, ShellPage.Home);
        installs.VersionSettingsRequested += (_, install) => OpenVersionSettings(install, ShellPage.Installs);
        versionSettings.BackRequested += (_, _) => CurrentPageKind = _versionSettingsOrigin;
        versionSettings.HomeRequested += (_, _) => CurrentPageKind = ShellPage.Home;
    }

    public HomeViewModel Home { get; }

    public InstallsViewModel Installs { get; }

    public SettingsViewModel Settings { get; }

    public VersionSettingsViewModel VersionSettings { get; }

    public LanguageMenuViewModel LanguageMenu { get; }

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
            ShellPage.Settings => ShellPage.Settings,
            ShellPage.VersionSettings => _versionSettingsOrigin,
        };
        set => CurrentPageKind = value;
    }

    /// <summary>For a click on the highlighted tab, which changes no binding: leaves a sub-page for the tab's own page.</summary>
    public void ShowTabPage() => CurrentPageKind = CurrentTab;

    public ViewModelBase CurrentPage => CurrentPageKind switch
    {
        ShellPage.Home => Home,
        ShellPage.Installs => Installs,
        ShellPage.Settings => Settings,
        ShellPage.VersionSettings => VersionSettings,
    };

    private void OpenVersionSettings(PzInstall install, ShellPage origin)
    {
        _versionSettingsOrigin = origin;
        VersionSettings.Open(install);
        CurrentPageKind = ShellPage.VersionSettings;
    }
}
