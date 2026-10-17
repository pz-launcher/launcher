using System;
using CommunityToolkit.Mvvm.ComponentModel;
using PzLauncher.Core.Mod;

namespace PzLauncher.Desktop.ViewModels.Mods;

/// <param name="collectionNames">The collections holding the mod, joined for display.</param>
public sealed partial class InstalledModRow(InstalledMod mod, bool isEnabled, string collectionNames, bool isSelected, Action<InstalledModRow> selectionChanged)
    : ObservableObject
{
    public InstalledMod Mod { get; } = mod;

    /// <summary>Listed in the game's enabled mod list.</summary>
    public bool IsEnabled { get; } = isEnabled;

    public string CollectionNames { get; } = collectionNames;

    [ObservableProperty] public partial bool IsSelected { get; set; } = isSelected;

    partial void OnIsSelectedChanged(bool value) => selectionChanged(this);
}
