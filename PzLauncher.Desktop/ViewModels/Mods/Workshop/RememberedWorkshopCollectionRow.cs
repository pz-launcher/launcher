using System.Globalization;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

/// <param name="Title">The Steam title, else the id.</param>
/// <param name="IsOpen">The collection the section shows.</param>
/// <param name="IsFavorite">Starred in the launcher's local favorites.</param>
public sealed record RememberedWorkshopCollectionRow(ulong Id, string Title, bool IsOpen, bool IsFavorite = false)
{
    public string IdText => Id.ToString(CultureInfo.InvariantCulture);
}
