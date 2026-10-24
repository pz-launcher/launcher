namespace PzLauncher.Core.Server;

// Verified on the Windows B42 server (task research/pz-server-runtime.md); the Linux layout is not yet.
internal static class PzServerFiles
{
    /// <summary>PZ's user folder below the home directory when no <c>-cachedir=</c> is given.</summary>
    public const string DefaultCacheFolder = "Zomboid";

    public const string ServerFolder = "Server";

    public const string IniExtension = ".ini";

    public const string DatabaseFolder = "db";

    public const string DatabaseExtension = ".db";

    public const string SavesFolder = "Saves";

    public const string MultiplayerFolder = "Multiplayer";

    /// <summary>One per cache folder; truncated on every start and whenever it reaches its size limit.</summary>
    public const string ConsoleFile = "server-console.txt";

    public const string RconPortKey = "RCONPort";

    public const string RconPasswordKey = "RCONPassword";

    public const int DefaultRconPort = 27015;

    /// <summary>Any value of this JVM property makes RCON listen on 127.0.0.1 only.</summary>
    public const string RconLoopbackProperty = "-Drconlo";

    public const string RconLoopbackArg = RconLoopbackProperty + "=1";

    public const string ServerNameArg = "-servername";

    public const string CacheDirArgPrefix = "-cachedir=";

    /// <summary>Creates the admin account when there is none, and resets its password when there is.</summary>
    public const string AdminPasswordArg = "-adminpassword";

    /// <summary>Printed before PZ reads the first admin password from stdin, which on the null device ends the start.</summary>
    public const string AdminPasswordPrompt = "Enter new administrator password";

    public const string QuitCommand = "quit";

    // From the known start-server.sh, not yet run on Linux; confirmed in the task's Linux check (research/pz-server-runtime.md).
    /// <summary>The server's own natives on Linux, first on the loader path before the game's <c>natives</c>.</summary>
    public const string LinuxNativesFolder = "linux64";

    /// <summary>Bare names, as in <c>start-server.sh</c>; unlike the game, no <c>libPZXInitThreads64.so</c>.</summary>
    public static IReadOnlyList<string> LinuxPreloadLibraries { get; } = ["libjsig.so"];

    public static string IniFile(string cacheDir, string name) => Path.Combine(cacheDir, ServerFolder, name + IniExtension);

    public static string DatabaseFile(string cacheDir, string name) => Path.Combine(cacheDir, DatabaseFolder, name + DatabaseExtension);

    public static string WorldFolder(string cacheDir, string name) => Path.Combine(cacheDir, SavesFolder, MultiplayerFolder, name);

    public static string ConsolePath(string cacheDir) => Path.Combine(cacheDir, ConsoleFile);
}
