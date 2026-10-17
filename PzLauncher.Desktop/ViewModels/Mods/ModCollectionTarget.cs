using CommunityToolkit.Mvvm.Input;

namespace PzLauncher.Desktop.ViewModels.Mods;

/// <summary>A collection offered by "add to collection"; its own command, so a menu item needs no binding to the page.</summary>
public sealed record ModCollectionTarget(string Name, IAsyncRelayCommand AddCommand);
