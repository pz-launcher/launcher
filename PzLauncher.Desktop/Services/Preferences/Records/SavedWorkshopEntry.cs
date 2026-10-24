namespace PzLauncher.Desktop.Services.Preferences.Records;

/// <summary>A Steam Workshop collection or item the launcher keeps, with its last known title.</summary>
/// <param name="Title"><c>null</c> when the Web API gave none.</param>
public sealed record SavedWorkshopEntry(ulong Id, string? Title);
