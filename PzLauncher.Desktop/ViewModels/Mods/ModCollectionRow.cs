namespace PzLauncher.Desktop.ViewModels.Mods;

/// <param name="IsDefault">Applied before every launch from the launcher.</param>
/// <param name="IsActive">Its installed mods are the game's enabled list.</param>
public sealed record ModCollectionRow(string Name, int ModCount, int MissingCount, bool IsDefault, bool IsActive, bool IsSelected)
{
    public bool HasMissing => MissingCount > 0;
}
