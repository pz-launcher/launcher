using System;
using PzLauncher.Core.Update;

namespace PzLauncher.Desktop.Services.Preferences;

public static class UpdateSourcePreferences
{
    /// <summary>The value persisted in the preference file: never change one.</summary>
    public static string PreferenceName(this UpdateSource source) => source switch
    {
        UpdateSource.Auto => "auto",
        UpdateSource.GitHub => "github",
        UpdateSource.Gitee => "gitee",
    };

    public static UpdateSource? FromPreferenceName(string? name)
    {
        foreach (var source in Enum.GetValues<UpdateSource>())
        {
            if (string.Equals(source.PreferenceName(), name, StringComparison.OrdinalIgnoreCase))
            {
                return source;
            }
        }

        return null;
    }
}
