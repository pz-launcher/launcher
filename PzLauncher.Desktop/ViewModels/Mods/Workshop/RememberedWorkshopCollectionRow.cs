using System.Globalization;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

/// <param name="Title">The Steam title, else the id.</param>
/// <param name="IsOpen">The collection the section shows.</param>
public sealed record RememberedWorkshopCollectionRow(ulong Id, string Title, bool IsOpen)
{
    public string IdText => Id.ToString(CultureInfo.InvariantCulture);
}
