namespace PzLauncher.Core.Mod.Collections;

/// <param name="CacheFolder">The game cache folder the files were read from.</param>
/// <param name="InstalledMods">In the game's lookup order.</param>
/// <param name="ActiveIds">The enabled list in <c>default.txt</c> order; empty when the file is missing.</param>
/// <param name="ActiveMatchesCollection">No installed mod is enabled, or some collection <see cref="ModCollection.IsActive"/>.</param>
public sealed record ModCollectionState(
    string CacheFolder,
    IReadOnlyList<ModCollection> Collections,
    IReadOnlyList<InstalledMod> InstalledMods,
    IReadOnlyList<string> ActiveIds,
    bool ActiveMatchesCollection);
