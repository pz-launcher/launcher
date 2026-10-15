using CommunityToolkit.Mvvm.ComponentModel;
using PzLauncher.Desktop.ViewModels.Home;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.ViewModels.Settings;

namespace PzLauncher.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public MainViewModel(
        HomeViewModel home,
        InstallsViewModel installs,
        SettingsViewModel settings,
        LanguageMenuViewModel languageMenu)
    {
        Home = home;
        Installs = installs;
        Settings = settings;
        LanguageMenu = languageMenu;

        // Never unsubscribed: both view models are app-lifetime singletons.
        home.SelectVersionRequested += (_, _) => CurrentPageKind = ShellPage.Installs;
    }

    public HomeViewModel Home { get; }

    public InstallsViewModel Installs { get; }

    public SettingsViewModel Settings { get; }

    public LanguageMenuViewModel LanguageMenu { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    public partial ShellPage CurrentPageKind { get; set; }

#pragma warning disable CS8524 // No discard arm on purpose: CS8509 makes a new ShellPage without a view model a build error.
    public ViewModelBase CurrentPage => CurrentPageKind switch
    {
        ShellPage.Home => Home,
        ShellPage.Installs => Installs,
        ShellPage.Settings => Settings,
    };
#pragma warning restore CS8524
}
