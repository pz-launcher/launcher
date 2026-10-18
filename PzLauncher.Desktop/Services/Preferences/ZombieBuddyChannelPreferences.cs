using PzLauncher.Core.Mod.ZombieBuddy;

namespace PzLauncher.Desktop.Services.Preferences;

public static class ZombieBuddyChannelPreferences
{
    /// <summary>The value persisted in the preference file: never change one.</summary>
    public static string PreferenceName(this ZombieBuddyChannel channel) => channel switch
    {
        ZombieBuddyChannel.Stable => "stable",
        ZombieBuddyChannel.Beta => "beta",
    };

    public static ZombieBuddyChannel? FromPreferenceName(string? name) => PreferenceNames.Parse<ZombieBuddyChannel>(name, PreferenceName);
}
