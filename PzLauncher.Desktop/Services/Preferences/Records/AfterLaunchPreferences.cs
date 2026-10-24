using PzLauncher.Desktop.Services.Launch;

namespace PzLauncher.Desktop.Services.Preferences.Records;

public static class AfterLaunchPreferences
{
    /// <summary>The value persisted in the preference file: never change one.</summary>
    public static string PreferenceName(this AfterLaunchAction action) => action switch
    {
        AfterLaunchAction.Keep => "keep",
        AfterLaunchAction.Minimize => "minimize",
        AfterLaunchAction.MinimizeToTray => "tray",
        AfterLaunchAction.Close => "close",
    };

    public static AfterLaunchAction? FromPreferenceName(string? name) => PreferenceNames.Parse<AfterLaunchAction>(name, PreferenceName);
}
