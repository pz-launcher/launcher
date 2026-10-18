namespace PzLauncher.Desktop.Themes;

public enum AppTheme
{
    Light,
    Dark,
}

public static class AppThemes
{
    /// <summary>Also the value persisted in the preference file: never change one.</summary>
    public static string PreferenceName(this AppTheme theme) => theme switch
    {
        AppTheme.Light => "light",
        AppTheme.Dark => "dark",
    };

    public static AppTheme? FromPreferenceName(string? name) => PreferenceNames.Parse<AppTheme>(name, PreferenceName);
}
