namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

/// <summary>A collection the Steam account favorited or published.</summary>
/// <param name="Title">The Steam title, else the id.</param>
/// <param name="IsOpen">The collection the section shows.</param>
public sealed record WorkshopUserCollectionRow(ulong Id, string Title, uint ChildCount, bool IsOpen);
