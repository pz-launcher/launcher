namespace PzLauncher.Desktop.Services.Preferences;

/// <summary>A Steam Workshop collection opened in the launcher.</summary>
/// <param name="Title"><c>null</c> when the Web API gave none.</param>
public sealed record SavedWorkshopCollection(ulong Id, string? Title);
