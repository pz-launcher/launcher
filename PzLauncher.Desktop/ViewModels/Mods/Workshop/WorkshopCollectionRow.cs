using System.Globalization;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

/// <summary>A Steam Workshop collection listed to open: one opened before, one of the Steam account's, or a starred one.</summary>
/// <param name="Title">The Steam title; <c>null</c> when unknown.</param>
/// <param name="ChildCount">The item count Steam listed it with; <c>null</c> when it wasn't listed with one, and the id shows instead.</param>
/// <param name="IsOpen">The collection the Workshop collections section shows.</param>
/// <param name="IsFavorite">Starred in the launcher's local favorites.</param>
public sealed record WorkshopCollectionRow(ulong Id, string? Title, uint? ChildCount, bool IsOpen, bool IsFavorite)
{
    public string IdText => Id.ToString(CultureInfo.InvariantCulture);

    /// <summary>The title, else the id.</summary>
    public string DisplayTitle => Title ?? IdText;

    public bool HasChildCount => ChildCount is not null;
}
