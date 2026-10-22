namespace PzLauncher.Desktop.ViewModels.Mods.Modpacks;

/// <param name="IsPresetLinked">The preset copy follows the install's preset of that name; otherwise the modpack is independent of it.</param>
/// <param name="CollectionTitle">The Steam collection's title, else its id; or the local collection's name.</param>
/// <param name="CollectionIdText">The Steam collection's id; <c>null</c> for a local collection.</param>
/// <param name="IsCollectionLinked">Always for a Steam collection, which is referenced by id.</param>
/// <param name="IsSelected">The modpack the section shows.</param>
public sealed record ModpackRow(
    string Name,
    string PresetName,
    int PresetModCount,
    bool IsPresetLinked,
    string CollectionTitle,
    string? CollectionIdText,
    bool IsCollectionLinked,
    int ExtraItemCount,
    bool IsSelected)
{
    public bool IsSteamCollection => CollectionIdText is not null;

    /// <summary>A source of the modpack is gone, so it keeps its own copy.</summary>
    public bool IsIndependent => !IsPresetLinked || !IsCollectionLinked;
}
