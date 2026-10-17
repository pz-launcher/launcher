namespace PzLauncher.Core.Mod.Collections;

/// <param name="ExtraGameArgs">The install's game args, for <c>-cachedir=</c>.</param>
/// <param name="OriginalName">The collection to replace when renaming; <c>null</c> saves under <paramref name="Name"/>, overwriting one of that name.</param>
/// <param name="ModIds">In order; a repeated id counts once.</param>
public sealed record SaveModCollectionRequest(
    string InstallRoot,
    string ExtraGameArgs,
    string? OriginalName,
    string Name,
    IReadOnlyList<string> ModIds);
