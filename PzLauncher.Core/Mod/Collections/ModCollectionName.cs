namespace PzLauncher.Core.Mod.Collections;

public static class ModCollectionName
{
    // Approximates the game's SandboxOptions.isValidPresetName: a name it must be able to use as a file name.
    private static readonly char[] Forbidden = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>A name the game's collection file can hold and its save dialog would accept.</summary>
    public static bool IsValid(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.IndexOfAny(Forbidden) < 0
        && !name.Any(char.IsControl)
        // The game reads a first line starting with this as its favourites.
        && !name.StartsWith(ModCollectionFiles.FavouritesPrefix, StringComparison.Ordinal);

    /// <summary>A collection id must survive the game's <c>name:id1;id2;</c> line unchanged.</summary>
    internal static bool IsValidModId(string id) =>
        id.Length > 0
        && id.Trim().Length == id.Length
        && id.IndexOf(ModCollectionFiles.IdSeparator) < 0
        && !id.Any(char.IsControl);
}
