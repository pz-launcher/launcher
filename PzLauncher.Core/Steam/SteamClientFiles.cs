using System.Globalization;

namespace PzLauncher.Core.Steam;

/// <summary>The Steam client's own files, process and per-account launch options.</summary>
internal static class SteamClientFiles
{
    public const string WindowsExecutable = "steam.exe";

    public const string ProcessName = "steam";

    /// <summary>Asks the running client to exit; it saves its config files on the way out.</summary>
    public const string ShutdownArgument = "-shutdown";

    public const string LaunchOptionsKey = "LaunchOptions";

    /// <summary>Steam starts the game's own command line where this appears in the launch options.</summary>
    public const string CommandPlaceholder = "%command%";

    public const string MostRecentKey = "MostRecent";

    public const string TimestampKey = "timestamp";

    public const string PersonaNameKey = "PersonaName";

    public const string AccountNameKey = "AccountName";

    public static string LoginUsersPath(string steamRoot) => Path.Combine(steamRoot, "config", "loginusers.vdf");

    /// <summary>The account's own files: its config, its Workshop subscription list.</summary>
    public static string UserDataFolder(string steamRoot, uint accountId) =>
        Path.Combine(steamRoot, "userdata", accountId.ToString(CultureInfo.InvariantCulture));

    public static string LocalConfigPath(string steamRoot, uint accountId) => Path.Combine(UserDataFolder(steamRoot, accountId), "config", "localconfig.vdf");

    /// <summary>Where an app's per-account settings live in <c>localconfig.vdf</c>; keys match case-insensitively.</summary>
    public static IReadOnlyList<string> AppBlockPath(int appId) =>
        ["UserLocalConfigStore", "Software", "Valve", "Steam", "apps", appId.ToString(CultureInfo.InvariantCulture)];
}
