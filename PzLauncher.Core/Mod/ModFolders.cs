namespace PzLauncher.Core.Mod;

// The game's mod roots and the mod.info names a lookup needs.
internal static class ModFolders
{
    public const string ModInfoFile = "mod.info";

    public const string CommonFolder = "common";

    public const string IdKey = "id";

    public const string NameKey = "name";

    public const string RequireKey = "require";

    public const string LoadAfterKey = "loadAfter";

    public const string LoadBeforeKey = "loadBefore";

    public const string IncompatibleKey = "incompatible";

    public const string JarFileKey = "javaJarFile";

    public const string CacheDirArg = "-cachedir=";

    public const string DefaultCacheFolder = "Zomboid";

    public static string StagedWorkshopFolder(string cache) => Path.Combine(cache, "Workshop");

    public static string StagedModsFolder(string stagedItem) => Path.Combine(stagedItem, "Contents", "mods");

    public static string LocalModsFolder(string cache) => Path.Combine(cache, "mods");

    public static string WorkshopModsFolder(string item) => Path.Combine(item, "mods");
}
