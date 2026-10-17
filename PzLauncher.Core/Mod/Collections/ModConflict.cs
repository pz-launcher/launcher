namespace PzLauncher.Core.Mod.Collections;

/// <param name="Id">The mod left out.</param>
/// <param name="ConflictsWith">The enabled mod it is incompatible with.</param>
public sealed record ModConflict(string Id, string ConflictsWith);
