namespace PzLauncher.Desktop.ViewModels;

public enum ShellPage
{
    Home,
    Installs,
    Settings,

    /// <summary>A sub-page of Home or Installs, whichever opened it: no tab of its own.</summary>
    VersionSettings,
}
