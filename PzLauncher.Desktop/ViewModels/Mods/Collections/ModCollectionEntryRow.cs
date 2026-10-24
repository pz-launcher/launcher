using CommunityToolkit.Mvvm.ComponentModel;

namespace PzLauncher.Desktop.ViewModels.Mods.Collections;

/// <param name="name">The installed mod's name; its id when it isn't installed.</param>
public sealed partial class ModCollectionEntryRow(string id, string name, bool isMissing, int number) : ObservableObject
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    public bool IsMissing { get; } = isMissing;

    /// <summary>Position in load order, from 1.</summary>
    [ObservableProperty] public partial int Number { get; set; } = number;
}
