namespace PzLauncher.Core.Mod.Collections;

/// <param name="EnabledIds">The enabled list in load order: each mod after its installed requirements.</param>
/// <param name="MissingIds">Collection ids with no installed copy, left out.</param>
/// <param name="SkippedIncompatible">Installed mods left out because an enabled mod is incompatible with them.</param>
public sealed record ApplyModCollectionResult(
    IReadOnlyList<string> EnabledIds,
    IReadOnlyList<string> MissingIds,
    IReadOnlyList<ModConflict> SkippedIncompatible)
{
    public bool DropsAny => MissingIds.Count > 0 || SkippedIncompatible.Count > 0;
}
