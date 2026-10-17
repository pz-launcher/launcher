using PzLauncher.Core.Common;

namespace PzLauncher.Core.Mod.Collections;

/// <summary>
/// The game's mod collections and enabled mod list in an install's cache folder. Every write re-reads the file and
/// changes only the affected collection or block.
/// </summary>
public interface IModCollectionService
{
    Task<Result<ModCollectionState>> GetAsync(string installRoot, string extraGameArgs, CancellationToken ct = default);

    /// <summary>Creates, edits, reorders, renames or overwrites one collection; the order is remembered by the launcher.</summary>
    Task<Result> SaveAsync(SaveModCollectionRequest request, CancellationToken ct = default);

    Task<Result> DeleteAsync(string installRoot, string extraGameArgs, string name, CancellationToken ct = default);

    /// <summary>Rewrites <c>default.txt</c>'s enabled list as <see cref="ModLoadOrder.Resolve"/> gives it for the collection.</summary>
    Task<Result<ApplyModCollectionResult>> ApplyAsync(string installRoot, string extraGameArgs, string name, CancellationToken ct = default);
}
