namespace PzLauncher.Core.Steam.Workshop;

/// <summary>The copy Steam installed, from the library's <c>appworkshop_108600.acf</c>.</summary>
/// <param name="Folder">The item folder in that library.</param>
public sealed record WorkshopInstall(string Folder, string Manifest, DateTimeOffset Updated, long Size);
