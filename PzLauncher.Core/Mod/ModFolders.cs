namespace PzLauncher.Core.Mod;

// The game's mod roots and the mod.info names a lookup needs.
internal static class ModFolders
{
    public const string ModInfoFile = "mod.info";

    public const string CommonFolder = "common";

    public const string IdKey = "id";

    public const string JarFileKey = "javaJarFile";

    public const string CacheDirArg = "-cachedir=";

    public const string DefaultCacheFolder = "Zomboid";

    public static string StagedWorkshopFolder(string cache) => Path.Combine(cache, "Workshop");

    public static string StagedModFolder(string stagedItem, string modId) => Path.Combine(stagedItem, "Contents", "mods", modId);

    public static string LocalModFolder(string cache, string modId) => Path.Combine(cache, "mods", modId);

    public static string WorkshopModFolder(string item, string modId) => Path.Combine(item, "mods", modId);
}
