using PzLauncher.Core.Net;

namespace PzLauncher.Desktop.Services.Preferences;

public static class ProxyModePreferences
{
    /// <summary>The value persisted in the preference file: never change one.</summary>
    public static string PreferenceName(this ProxyMode mode) => mode switch
    {
        ProxyMode.System => "system",
        ProxyMode.None => "none",
        ProxyMode.Manual => "manual",
    };

    public static ProxyMode? FromPreferenceName(string? name) => PreferenceNames.Parse<ProxyMode>(name, PreferenceName);
}
