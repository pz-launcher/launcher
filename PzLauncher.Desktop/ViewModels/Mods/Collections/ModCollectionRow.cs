namespace PzLauncher.Desktop.ViewModels.Mods.Collections;

/// <param name="IsCurrent">The install's current collection, applied before every launch from the launcher.</param>
/// <param name="IsActiveInOrder">Its installed mods are the game's enabled list, in that order: applying it changes nothing.</param>
public sealed record ModCollectionRow(string Name, int ModCount, int MissingCount, bool IsCurrent, bool IsActiveInOrder, bool IsSelected)
{
    public bool HasMissing => MissingCount > 0;
}
