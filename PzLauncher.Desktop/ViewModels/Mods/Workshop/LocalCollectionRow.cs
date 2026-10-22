using System;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

/// <param name="IsSelected">The collection the local collections section shows.</param>
public sealed record LocalCollectionRow(Guid Id, string Name, int ItemCount, bool IsSelected);
