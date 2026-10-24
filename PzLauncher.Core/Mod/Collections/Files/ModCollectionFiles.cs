namespace PzLauncher.Core.Mod.Collections.Files;

// The game's ModSelectorModel.lua (collections) and ActiveModsFile (default.txt), build 42.21.
internal static class ModCollectionFiles
{
    public const string FavouritesPrefix = "!fav!";

    public const char NameSeparator = ':';

    public const char IdSeparator = ';';

    public const string ActiveModsBlock = "mods";

    public const string ActiveModKey = "mod";

    public const string ActiveModsVersionLine = "VERSION = 1,";

    public const string MapsBlock = "maps";

    // The launcher's own files next to the game's; the game ignores them.
    public const string OriginalSuffix = ".pzlauncher.orig";

    public static string SettingsFile(string cache) => Path.Combine(cache, "Lua", "pz_modlist_settings.cfg");

    public static string OrderFile(string cache) => Path.Combine(cache, "Lua", "pz_modlist_settings.pzlauncher.json");

    public static string ActiveModsFile(string cache) => Path.Combine(ModFolders.LocalModsFolder(cache), "default.txt");
}
