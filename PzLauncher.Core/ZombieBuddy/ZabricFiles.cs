namespace PzLauncher.Core.ZombieBuddy;

// Mirrors Zabric's PreloadConfig and widener/ModFolders, and ZombieBuddy's Config.java / Agent.configDir.
internal static class ZabricFiles
{
    public const string ModId = "Zabric";

    public const string ItemId = "3820726385";

    public const string ModInfo = "mod.info";

    public const string CommonFolder = "common";

    public const string IdKey = "id";

    public const string JarFileKey = "javaJarFile";

    public const string PkgNameKey = "javaPkgName";

    public const string PreloadKey = "javaPreload";

    public const string ManifestEntry = "META-INF/MANIFEST.MF";

    public const string ManifestPreloadAttribute = "ZB-Preload";

    public const string ConfigDirOption = "config_dir";

    public const string DefaultConfigFolder = ".zombie_buddy";

    public const string ConfigFile = "config.json";

    public const string PreloadModsKey = "preload_mods";

    public const string InfPathKey = "infPath";

    public const string JarPathKey = "jarPath";

    public const string CacheDirArg = "-cachedir=";

    public const string DefaultCacheFolder = "Zomboid";

    public static string StagedWorkshopFolder(string cache) => Path.Combine(cache, "Workshop");

    public static string StagedModFolder(string stagedItem) => Path.Combine(stagedItem, "Contents", "mods", ModId);

    public static string LocalModFolder(string cache) => Path.Combine(cache, "mods", ModId);

    public static string WorkshopModFolder(string item) => Path.Combine(item, "mods", ModId);
}
