using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;

namespace PzLauncher.Core.Mod.ZombieBuddy;

// Mirrors ZombieBuddy's own installer (installer/installer.go, v4.3) and doc/Installation.md.
internal static class ZombieBuddyFiles
{
    public const string StableItemId = "3619862853";

    public const string BetaItemId = "3812624292";

    public const string NativeLoader = "zbNative.dll";

    public const string AgentJar = "ZombieBuddy.jar";

    // ZombieBuddy's self-update: zbNative.dll swaps it in for the jar at the next start.
    public const string PendingJarUpdate = "ZombieBuddy.jar.new";

    public const string NativeAgentArg = "-agentlib:zbNative";

    public const string JavaAgentArg = "-javaagent:" + AgentJar;

    // ZombieBuddy's Agent.configDir and Config.java.
    public const string ConfigDirOption = "config_dir";

    public const string DefaultConfigFolder = ".zombie_buddy";

    public const string ConfigFile = "config.json";

    public const string PreloadModsKey = "preload_mods";

    public const string InfPathKey = "infPath";

    public const string JarPathKey = "jarPath";

    // What ZombieBuddy's Loader.preloadMods checks in a Java mod.
    public const string PkgNameKey = "javaPkgName";

    public const string PreloadKey = "javaPreload";

    public const string ManifestEntry = "META-INF/MANIFEST.MF";

    public const string ManifestPreloadAttribute = "ZB-Preload";

    public static IReadOnlyList<ZombieBuddyChannel> Channels { get; } = [ZombieBuddyChannel.Stable, ZombieBuddyChannel.Beta];

    public static IReadOnlyList<string> LeftoverFiles { get; } = [NativeLoader, AgentJar, PendingJarUpdate];

    /// <summary>Every agent arg form, whatever the OS: an arg for another OS is a leftover too.</summary>
    public static IReadOnlyList<string> AllAgentArgs { get; } = [NativeAgentArg, JavaAgentArg];

    public static string ItemId(ZombieBuddyChannel channel) => channel switch
    {
        ZombieBuddyChannel.Stable => StableItemId,
        ZombieBuddyChannel.Beta => BetaItemId,
    };

    public static string ItemFolder(string library, ZombieBuddyChannel channel) => WorkshopLibraries.ItemFolder(library, ItemId(channel));

    public static string LibsFolder(string item) => Path.Combine(item, "mods", "ZombieBuddy", "libs");

    // Windows loads the jar through zbNative.dll, which also applies a pending jar update; elsewhere -javaagent loads it.
    public static IReadOnlyList<string> RequiredFiles(PlatformKind os) => os switch
    {
        PlatformKind.Windows => [NativeLoader, AgentJar],
        PlatformKind.Linux or PlatformKind.MacOS => [AgentJar],
    };

    public static string AgentArg(PlatformKind os) => os switch
    {
        PlatformKind.Windows => NativeAgentArg,
        PlatformKind.Linux or PlatformKind.MacOS => JavaAgentArg,
    };
}
