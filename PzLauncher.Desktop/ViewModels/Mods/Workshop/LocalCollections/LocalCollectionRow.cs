using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop.LocalCollections;

/// <param name="isSelected">The collection the local collections section shows.</param>
public sealed partial class LocalCollectionRow(Guid id, string name, bool isSelected) : ObservableObject
{
    public Guid Id { get; } = id;

    public string Name { get; } = name;

    public bool IsSelected { get; } = isSelected;

    /// <summary>Follows the collection in place, so an added, moved or removed item keeps the row.</summary>
    [ObservableProperty] public partial int ItemCount { get; internal set; }
}
