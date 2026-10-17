namespace PzLauncher.Core.Mod;

/// <param name="WorkshopId">The Steam Workshop item folder name; <c>null</c> for other sources.</param>
/// <param name="Require">Bare mod ids, as are the other id lists.</param>
public sealed record InstalledMod(
    string Id,
    string Name,
    string? WorkshopId,
    InstalledModSource Source,
    IReadOnlyList<string> Require,
    IReadOnlyList<string> LoadAfter,
    IReadOnlyList<string> LoadBefore,
    IReadOnlyList<string> Incompatible);
