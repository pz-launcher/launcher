using System;

namespace PzLauncher.Desktop.Themes;

public enum AppTheme
{
    Light,
    Dark,
}

#pragma warning disable CS8524 // No discard arms on purpose: CS8509 makes a new AppTheme without a mapping a build error.
public static class AppThemes
{
    /// <summary>Also the value persisted in the preference file: never change one.</summary>
    public static string PreferenceName(this AppTheme theme) => theme switch
    {
        AppTheme.Light => "light",
        AppTheme.Dark => "dark",
    };

    public static AppTheme? FromPreferenceName(string? name)
    {
        foreach (var theme in Enum.GetValues<AppTheme>())
        {
            if (string.Equals(theme.PreferenceName(), name, StringComparison.OrdinalIgnoreCase))
            {
                return theme;
            }
        }

        return null;
    }
}
#pragma warning restore CS8524
