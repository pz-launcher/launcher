namespace PzLauncher.Desktop.Services.Mods;

/// <summary>A Steam Workshop collection or item the launcher keeps, with its last known title.</summary>
/// <param name="Title"><c>null</c> when unknown.</param>
public sealed record WorkshopEntry(ulong Id, string? Title);
